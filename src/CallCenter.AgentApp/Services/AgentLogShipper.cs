using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Shared.Contracts.AgentLogs;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// Sends this laptop's log to the server (N-12), so a fault can be read there
/// instead of on the laptop.
/// </summary>
/// <remarks>
/// <b>The log file is the queue.</b> The app writes its log to the laptop as
/// before, and this reads the part of each day's file the server does not have
/// yet and sends it. Nothing is held in memory or copied a second time, so a
/// line written while the server was down, or before anybody signed in, simply
/// waits in the file and goes with the next send. The laptop keeps three days
/// (<c>App.LocalLogDays</c>); what the server has not had by then is lost.
///
/// <b>The server says where its copy ends</b>, and every piece is sent with
/// where it starts (see the server's <c>AgentLogStore</c>). This asks once per
/// run of the app and then follows the server's answers, so a piece sent twice
/// is refused rather than written twice.
///
/// <b>Whole lines only.</b> The last line of the file may still be being
/// written, so a piece ends at the last line break in it.
///
/// <b>Its own requests are not logged.</b> The app logs four lines for every
/// request; had these been logged, each send would make the next one, for
/// ever. The client it uses has no loggers (<c>App.xaml.cs</c>), and this logs
/// only when sending starts or stops failing, not on every try.
/// </remarks>
public class AgentLogShipper(
    IHttpClientFactory clients, AgentSession session, ILogger<AgentLogShipper> logger, string directory)
{
    public const string HttpClientName = "server-logs";

    /// <summary>How many pieces of one file a single pass sends: 25 MB, a day's file many times over.</summary>
    private const int MaxPiecesPerFile = 50;

    private readonly SemaphoreSlim _shipping = new(1, 1);

    /// <summary>How long the server's copy of each file is. Null until it has been asked.</summary>
    private Dictionary<string, long>? _server;

    /// <summary>Days the server will take no more of (its size limit). Skipped until the app restarts.</summary>
    private readonly HashSet<string> _full = new(StringComparer.Ordinal);

    private bool _failing;

    /// <summary>The laptop's folder on the server: its machine name, made safe for a path.</summary>
    public string Laptop { get; } = AgentLogNames.Laptop(LaptopInfo.LaptopId);

    /// <summary>Sends at every sign-in and every <paramref name="interval"/> while signed in.</summary>
    public void RunEvery(TimeSpan interval)
    {
        session.Changed += (_, _) =>
        {
            if (session.IsSignedIn)
            {
                _ = ShipAsync();
            }
        };

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);

            while (await timer.WaitForNextTickAsync())
            {
                try
                {
                    await ShipAsync();
                }
                catch (Exception ex)
                {
                    // Anything unforeseen: this pass is lost, the next one
                    // tries again. Out of this loop it would end sending for
                    // good, and nobody would notice until the laptop's log was
                    // needed.
                    Failed(ex.Message);
                }
            }
        });
    }

    /// <summary>
    /// One pass: every file, oldest first, up to its last whole line. Does
    /// nothing while nobody is signed in, or while a pass is already running.
    /// </summary>
    public async Task ShipAsync(CancellationToken ct = default)
    {
        if (session.AccessToken is not { } token || !await _shipping.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            var http = clients.CreateClient(HttpClientName);

            _server ??= await AskAsync(http, token, ct);

            if (_server is not { } server)
            {
                return;
            }

            var files = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "agent-*.log")
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Where(AgentLogNames.IsFile)
                    .Order(StringComparer.Ordinal)
                    .ToList()
                : [];

            foreach (var file in files.Where(f => !_full.Contains(f)))
            {
                if (!await ShipFileAsync(http, token, server, file, ct))
                {
                    return;
                }
            }

            if (_failing)
            {
                _failing = false;
                logger.LogInformation("The log is reaching the server again");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or System.Text.Json.JsonException
                                   && !ct.IsCancellationRequested)
        {
            Failed(ex.Message);
        }
        finally
        {
            _shipping.Release();
        }
    }

    /// <returns>False when sending should stop for this pass: the server is down or refused.</returns>
    private async Task<bool> ShipFileAsync(
        HttpClient http, string token, Dictionary<string, long> server, string file, CancellationToken ct)
    {
        for (var piece = 0; piece < MaxPiecesPerFile; piece++)
        {
            var from = server.GetValueOrDefault(file);
            var bytes = ReadPiece(Path.Combine(directory, file), from);

            if (bytes is null)
            {
                return true;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{AgentLogNames.Route}/{Uri.EscapeDataString(Laptop)}/{Uri.EscapeDataString(file)}?offset={from}")
            {
                Content = new ByteArrayContent(bytes),
            };

            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, ct);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                case HttpStatusCode.Conflict:
                    // Added; or not, because the server's copy ends elsewhere.
                    // Either way its length is where the next piece starts.
                    var length = (await response.Content.ReadFromJsonAsync<AgentLogLengthDto>(ct))?.Length;

                    if (length is null || (response.StatusCode == HttpStatusCode.Conflict && length == from))
                    {
                        Failed($"the server answered {(int)response.StatusCode} without moving on");
                        return false;
                    }

                    server[file] = length.Value;
                    break;

                case HttpStatusCode.RequestEntityTooLarge:
                    logger.LogWarning("The server is keeping no more of {File}: it is at its size limit", file);
                    _full.Add(file);
                    return true;

                default:
                    Failed($"the server answered {(int)response.StatusCode}");
                    return false;
            }
        }

        return true;
    }

    /// <summary>What the server has of this laptop's files, or null if it could not be asked.</summary>
    private async Task<Dictionary<string, long>?> AskAsync(HttpClient http, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{AgentLogNames.Route}/{Uri.EscapeDataString(Laptop)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            Failed($"the server answered {(int)response.StatusCode}");
            return null;
        }

        var lengths = await response.Content.ReadFromJsonAsync<Dictionary<string, long>>(ct);
        return new Dictionary<string, long>(lengths ?? [], StringComparer.Ordinal);
    }

    /// <summary>
    /// The file from <paramref name="from"/> to its last line break, at most
    /// <see cref="AgentLogNames.MaxChunkBytes"/>; null when there is no whole
    /// line to send. A single line longer than a piece goes as it is.
    /// </summary>
    internal static byte[]? ReadPiece(string path, long from)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        // Shorter than the server's copy: not the file the server has. Leave it.
        if (stream.Length <= from)
        {
            return null;
        }

        var buffer = new byte[Math.Min(AgentLogNames.MaxChunkBytes, stream.Length - from)];
        stream.Seek(from, SeekOrigin.Begin);
        stream.ReadExactly(buffer);

        var end = Array.LastIndexOf(buffer, (byte)'\n');

        if (end < 0)
        {
            return buffer.Length == AgentLogNames.MaxChunkBytes ? buffer : null;
        }

        return end == buffer.Length - 1 ? buffer : buffer[..(end + 1)];
    }

    /// <summary>Logs the first failure of a run of them, not one every half a minute.</summary>
    private void Failed(string reason)
    {
        if (!_failing)
        {
            _failing = true;
            logger.LogWarning("The log could not be sent to the server ({Reason}); it waits on the laptop", reason);
        }
    }
}
