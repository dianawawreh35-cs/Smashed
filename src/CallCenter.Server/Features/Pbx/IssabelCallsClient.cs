using System.Globalization;
using System.Net;
using System.Net.Security;

namespace CallCenter.Server.Features.Pbx;

/// <summary>Where the PBX's web interface is, and the login to it (S-55).</summary>
public sealed record PbxConnection(Uri BaseUri, string Username, string Password);

/// <summary>
/// The PBX's Calls Detail report, as CSV, for whole days (S-55). An interface
/// so the import can be tested without a PBX.
/// </summary>
public interface IPbxCallsDetailSource
{
    /// <summary>Every call from the start of <paramref name="from"/> to the end of <paramref name="to"/>, the PBX's local days.</summary>
    /// <exception cref="PbxImportException">The PBX could not be reached, refused the login, or answered with something that is not the report.</exception>
    Task<string> DownloadCsvAsync(PbxConnection pbx, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>Why a download from the PBX failed, in words a supervisor can act on.</summary>
public sealed class PbxImportException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Downloads the Issabel Call Center module's Calls Detail report (S-55): the
/// same request as the report page's own "Export CSV" button.
/// </summary>
/// <remarks>
/// <b>One session, kept.</b> Issabel's login is a form post that answers with a
/// session cookie. This client keeps that cookie between checks and logs in
/// again only when the PBX answers with its login page instead of the report,
/// so a check every minute is one request, not a login plus a request - the
/// PBX would otherwise hold 1,440 sessions a day.
///
/// <b>The PBX's own certificate.</b> Issabel ships with a self-signed HTTPS
/// certificate, which no machine trusts. It is accepted for the configured PBX
/// address only; any other host still has to present a valid certificate. The
/// traffic stays inside the VPN either way.
///
/// A singleton, and one request at a time: the cookie jar is shared, and two
/// logins racing would each end the other's session.
/// </remarks>
public sealed class IssabelCallsClient(ILogger<IssabelCallsClient> logger) : IPbxCallsDetailSource, IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private HttpClient? _http;
    private PbxConnection? _sessionFor;

    public async Task<string> DownloadCsvAsync(PbxConnection pbx, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var http = ClientFor(pbx);
            var report = ExportUri(pbx.BaseUri, from, to);

            var body = await GetAsync(http, report, ct);
            if (!NeedsLogin(body))
            {
                return body;
            }

            // Never logged in, or the PBX ended the session. Once more, after a login.
            await LoginAsync(http, pbx, ct);
            body = await GetAsync(http, report, ct);

            return NeedsLogin(body)
                ? throw new PbxImportException(
                    "The PBX accepted the login but still shows its login page instead of the report. "
                    + "Check that this user can open Call Center → Reports → Calls Detail on the PBX.")
                : body;
        }
        catch (HttpRequestException ex)
        {
            throw new PbxImportException($"Could not reach the PBX at {pbx.BaseUri}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PbxImportException(
                $"The PBX at {pbx.BaseUri} did not answer within {Timeout.TotalSeconds:0} seconds.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The report's export link. Dates are <c>dd MMM yyyy</c> with English month
    /// names, as the PBX's own date picker writes them ("21 Sep 2026"); every
    /// other filter is left empty, meaning all agents, queues and numbers.
    /// </summary>
    public static Uri ExportUri(Uri baseUri, DateOnly from, DateOnly to)
    {
        static string Day(DateOnly d) =>
            Uri.EscapeDataString(d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));

        var query = "menu=calls_detail"
            + "&date_start=" + Day(from)
            + "&date_end=" + Day(to)
            + "&calltype=&agent=&queue=&phone=&id_campaign_out=&id_campaign_in="
            + "&exportcsv=yes&rawmode=yes";

        return new UriBuilder(new Uri(baseUri, "/index.php")) { Query = query }.Uri;
    }

    /// <summary>
    /// The PBX answers a request it will not serve with 200 and something that
    /// is not the report: its login page, or - for the export link, without a
    /// session - <c>{"error":"Your session has expired. …","statusResponse":"ERROR_SESSION"}</c>
    /// (seen 26 Sep 2026). The report is CSV, so it never starts with a tag or
    /// a brace and never has a password field.
    /// </summary>
    public static bool NeedsLogin(string body)
    {
        var start = body.TrimStart();
        return start.StartsWith('<')
            || start.StartsWith('{')
            || body.Contains("ERROR_SESSION", StringComparison.Ordinal)
            || body.Contains("input_pass", StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoginAsync(HttpClient http, PbxConnection pbx, CancellationToken ct)
    {
        // The login page checks only that submit_login is present, so it is sent empty.
        using var form = new FormUrlEncodedContent(
        [
            new("input_user", pbx.Username),
            new("input_pass", pbx.Password),
            new("submit_login", ""),
        ]);

        using var response = await http.PostAsync(new Uri(pbx.BaseUri, "/index.php"), form, ct);
        response.EnsureSuccessStatusCode();

        // A refused login shows the form again; an accepted one redirects to the
        // dashboard, which has no password field.
        var body = await response.Content.ReadAsStringAsync(ct);
        if (body.Contains("input_pass", StringComparison.OrdinalIgnoreCase))
        {
            throw new PbxImportException(
                $"The PBX refused the login for user '{pbx.Username}'. Check the username and password.");
        }

        logger.LogInformation("Logged in to the PBX at {Pbx} as {User}", pbx.BaseUri, pbx.Username);
    }

    private static async Task<string> GetAsync(HttpClient http, Uri uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>The kept session, or a fresh one when the address or the login has changed.</summary>
    private HttpClient ClientFor(PbxConnection pbx)
    {
        if (_http is not null && _sessionFor == pbx)
        {
            return _http;
        }

        _http?.Dispose();

        var trustedHost = pbx.BaseUri.Host;
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                errors == SslPolicyErrors.None
                || string.Equals(request.RequestUri?.Host, trustedHost, StringComparison.OrdinalIgnoreCase),
        };

        _http = new HttpClient(handler) { Timeout = Timeout };
        _sessionFor = pbx;
        return _http;
    }

    public void Dispose()
    {
        _http?.Dispose();
        _gate.Dispose();
    }
}
