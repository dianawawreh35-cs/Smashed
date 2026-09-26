namespace CallCenter.Server.Features.Pos;

/// <summary>
/// The restaurant POS's customer lookup (A-67). Section <c>PosLookup</c>.
/// </summary>
/// <remarks>
/// <b>Off until a token is set.</b> The token is a deployment secret:
/// <c>POS_LOOKUP_TOKEN</c> in the server's <c>.env</c> (runbook step 5), or
/// <c>dotnet user-secrets</c> on a developer's machine. A blank token means no
/// request is ever made, which is also what keeps the test suite from asking
/// the real POS about made-up numbers.
/// </remarks>
public class PosLookupOptions
{
    public const string SectionName = "PosLookup";

    /// <summary>The lookup address; the number is appended to it.</summary>
    public string BaseUrl { get; set; } = "https://smashed-ps.com/api/CustLookup/";

    /// <summary>Bearer token the POS issued for this server.</summary>
    public string Token { get; set; } = "";

    // How often it runs is not here: it is the supervisor's setting
    // pos.lookup.interval_minutes (S-47), so it can change without a restart.

    /// <summary>How far back a call still counts as recent.</summary>
    public TimeSpan Lookback { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// How long before a number the POS did not know is asked about again. The
    /// order is usually typed into the POS during or after the call, so the
    /// first answer is often "not found" and a later one is not.
    /// </summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The most numbers one run asks about, so a backlog is spread over several runs.</summary>
    public int MaxPerRun { get; set; } = 100;

    public bool Enabled => !string.IsNullOrWhiteSpace(Token);
}
