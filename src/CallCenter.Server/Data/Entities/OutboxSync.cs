namespace CallCenter.Server.Data.Entities;

/// <summary>
/// Records which offline operations from an Agent App have already been applied,
/// so replaying a queued batch twice is harmless. Table <c>outbox_sync</c>.
/// </summary>
/// <remarks>
/// The laptop generates <see cref="ClientOpId"/> when it queues the operation
/// (A-04). If the connection drops mid-upload the app retries; the server sees
/// the id again and skips it.
/// </remarks>
public class OutboxSync
{
    /// <summary>Generated on the laptop. The primary key, so a replay is a no-op.</summary>
    public Guid ClientOpId { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTimeOffset AppliedAt { get; set; }
}
