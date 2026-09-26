using System.Collections.Concurrent;

namespace CallCenter.Server.Features.Pos;

/// <summary>
/// When each number was last asked about, so the POS is not asked every five
/// minutes about a caller it did not know (A-67).
/// </summary>
/// <remarks>
/// Held in memory, not in a table. After a restart every recent unknown number
/// is asked about once more, which costs a few dozen half-second requests and
/// nothing else, and saves a table that would only ever hold this.
/// </remarks>
public class PosLookupLedger
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _askedAt = new();

    /// <summary>When the last run began; null until the first one after startup.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    public bool IsDue(string number, DateTimeOffset now, TimeSpan retryAfter) =>
        !_askedAt.TryGetValue(number, out var at) || now - at >= retryAfter;

    public void Asked(string number, DateTimeOffset now) => _askedAt[number] = now;

    /// <summary>Drops numbers asked about before <paramref name="cutoff"/>; their calls are no longer recent.</summary>
    public void Forget(DateTimeOffset cutoff)
    {
        foreach (var (number, at) in _askedAt)
        {
            if (at < cutoff)
            {
                _askedAt.TryRemove(number, out _);
            }
        }
    }
}
