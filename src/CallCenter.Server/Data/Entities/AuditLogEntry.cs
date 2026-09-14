using System.Text.Json;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// Who changed what, and when (N-06). Table <c>audit_log</c>.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>contact, user, settings, form or flag.</summary>
    public string Entity { get; set; } = null!;

    public string? EntityId { get; set; }

    /// <summary>create, update, merge, flag, unflag or delete.</summary>
    public string Action { get; set; } = null!;

    public JsonDocument? Before { get; set; }

    public JsonDocument? After { get; set; }
}
