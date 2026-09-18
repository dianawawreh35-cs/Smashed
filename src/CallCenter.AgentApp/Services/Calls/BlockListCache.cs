using System.IO;
using System.Text.Json;
using CallCenter.Shared.Contracts.Contacts;
using CallCenter.Shared.Phone;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// The blocked numbers, kept on this laptop (A-17).
/// </summary>
/// <remarks>
/// A-17 requires a blocked caller to be rejected "even when the server is
/// unreachable", so the decision cannot be a request made while the phone is
/// ringing. Two reasons, and the second is the one that matters: a call has to
/// be rejected in the second or so before the PBX gives up on this extension,
/// and a server round trip is not something to put in that window even when the
/// server is up.
///
/// So the list is fetched at sign-in, written to disk, and answered from memory.
/// A laptop that starts with no network works from the copy left by the last
/// shift.
///
/// Plain JSON rather than the SQLite offline buffer: this is one list of
/// strings, it is replaced wholesale rather than edited, and the buffer's
/// library is still pinned to a version carrying a security advisory. Nothing
/// here is worth a database.
/// </remarks>
public class BlockListCache(ApiClient api, ILogger<BlockListCache> logger)
{
    /// <summary>What is written to disk, so the file says when it was true.</summary>
    private record CachedList(IReadOnlyList<string> Numbers, DateTimeOffset AsOf);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(App.AppDataDirectory, "blocked-numbers.json");
    private readonly Lock _gate = new();

    private BlockList _list = BlockList.Empty;
    private DateTimeOffset? _asOf;

    /// <summary>Raised when the list changes, so a screen can show how old it is.</summary>
    public event EventHandler? Changed;

    /// <summary>How many numbers are blocked.</summary>
    public int Count
    {
        get { lock (_gate) return _list.Count; }
    }

    /// <summary>
    /// When the list was last confirmed with the server, or null when nothing
    /// has ever been loaded. Worth showing: an agent rejecting calls from a
    /// week-old list should be able to find that out.
    /// </summary>
    public DateTimeOffset? AsOf
    {
        get { lock (_gate) return _asOf; }
    }

    /// <summary>
    /// Whether a call from this number must be rejected without ringing (A-17).
    /// Answered from memory — no network, no file read, no waiting.
    /// </summary>
    public bool IsBlocked(string? callerNumber)
    {
        lock (_gate)
        {
            return _list.IsBlocked(callerNumber);
        }
    }

    /// <summary>
    /// Reads whatever the last shift left behind. Called at startup, before any
    /// sign-in, so a laptop with no network is not defenceless.
    /// </summary>
    public void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_path))
            {
                logger.LogInformation("No cached block list yet; nothing is blocked until the first refresh.");
                return;
            }

            var cached = JsonSerializer.Deserialize<CachedList>(File.ReadAllText(_path));
            if (cached is null)
            {
                return;
            }

            Replace(new BlockList(cached.Numbers), cached.AsOf);

            logger.LogInformation(
                "Loaded {Count} blocked number(s) from the cache, last confirmed {AsOf:u}",
                Count, cached.AsOf);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt cache must not stop the app starting. Nothing is blocked
            // until the next refresh, which is the safer failure: a nuisance
            // caller gets through once, rather than customers being dropped.
            logger.LogWarning(ex, "The cached block list at {Path} could not be read; ignoring it.", _path);
        }
    }

    /// <summary>
    /// Fetches the current list and writes it to disk. Called at sign-in.
    /// </summary>
    /// <remarks>
    /// A failure is deliberately quiet and leaves the previous list in place. An
    /// agent signing in on a laptop that cannot reach the server still gets the
    /// blocking their last shift had, which is the whole point of the cache.
    /// </remarks>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var result = await api.GetBlockedNumbersAsync(ct);

        if (!result.IsOk || result.Value is null)
        {
            logger.LogWarning(
                "The block list could not be refreshed ({Error}); keeping {Count} number(s) from the cache.",
                result.ErrorCode ?? "no answer", Count);
            return;
        }

        Replace(new BlockList(result.Value.Numbers), result.Value.AsOf);
        Save(result.Value);

        logger.LogInformation("Block list refreshed: {Count} number(s)", Count);
    }

    private void Replace(BlockList list, DateTimeOffset asOf)
    {
        lock (_gate)
        {
            _list = list;
            _asOf = asOf;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save(BlockedNumbersDto list)
    {
        try
        {
            Directory.CreateDirectory(App.AppDataDirectory);
            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(new CachedList(list.Numbers, list.AsOf), JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The list still works for this session; only the next offline start
            // loses out.
            logger.LogWarning(ex, "The block list could not be cached to {Path}", _path);
        }
    }
}
