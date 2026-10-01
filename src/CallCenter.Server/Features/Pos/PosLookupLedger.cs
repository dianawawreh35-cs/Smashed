namespace CallCenter.Server.Features.Pos;

/// <summary>
/// What the POS lookup (A-67) remembers between runs: when the last one began,
/// what it did, and that only one runs at a time.
/// </summary>
/// <remarks>
/// Held in memory, not in a table. After a restart the settings screen shows
/// no run until the first one, a minute later, and nothing else is lost.
/// Until 1 Oct 2026 it also remembered each number it had asked about, so a
/// number the POS did not know waited an hour; now every run asks again.
/// </remarks>
public class PosLookupLedger
{
    /// <summary>What one run did, and when.</summary>
    public record Run(DateTimeOffset StartedAt, DateTimeOffset FinishedAt, PosCustomerSync.Result Result);

    /// <summary>When the last run began; null until the first one after startup.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>The last run that finished; null until one has.</summary>
    public Run? Last { get; set; }

    /// <summary>
    /// Held for the length of a run, so the timer and the Check now button never
    /// ask about the same number at once.
    /// </summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
