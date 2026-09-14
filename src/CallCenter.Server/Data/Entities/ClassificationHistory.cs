using System.Text.Json;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// Audit trail of classification changes (A-43, N-06).
/// Table <c>classification_history</c>.
/// </summary>
public class ClassificationHistory
{
    public Guid Id { get; set; }

    public Guid CommunicationId { get; set; }

    public Guid ChangedBy { get; set; }
    public User ChangedByUser { get; set; } = null!;

    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>Null on the first classification.</summary>
    public JsonDocument? Before { get; set; }

    public JsonDocument After { get; set; } = null!;
}
