using System.Text.Json;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// What a communication was about. Table <c>classifications</c>, keyed by the
/// communication it describes - a communication has at most one.
/// </summary>
/// <remarks>
/// Agents may edit their own classifications only on the day of the call (A-42);
/// supervisors may edit any at any time. Every change is written to
/// <see cref="ClassificationHistory"/> (A-43).
/// </remarks>
public class Classification
{
    public Guid CommunicationId { get; set; }
    public Communication Communication { get; set; } = null!;

    public Guid TypeId { get; set; }
    public ClassificationType Type { get; set; } = null!;

    public decimal? OrderValue { get; set; }

    public string? Notes { get; set; }

    public bool FollowUp { get; set; }

    /// <summary>Meaningful for Complaint. Feeds R-17.</summary>
    public bool? Resolved { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public Guid? ResolvedBy { get; set; }

    /// <summary>The form version this was captured under.</summary>
    public int FormVersion { get; set; }
    public FormDefinition Form { get; set; } = null!;

    /// <summary>Values for fields that are not built-in columns, keyed by field key.</summary>
    public JsonDocument CustomValues { get; set; } = null!;

    public Guid ClassifiedBy { get; set; }
    public User ClassifiedByUser { get; set; } = null!;

    public DateTimeOffset ClassifiedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
