using System.ComponentModel.DataAnnotations.Schema;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// Key/value configuration so the supervisor can change behaviour without a
/// migration. Table <c>settings</c>.
/// </summary>
/// <remarks>
/// Seeded keys: <c>recording.retention_days</c>, <c>agent.idle_logout_minutes</c>,
/// <c>agent.edit_window</c>, <c>sla.answer_seconds</c>, <c>callback.extension</c>,
/// <c>pbx.ip</c>, <c>pbx.ami.enabled</c>.
/// </remarks>
public class Setting
{
    public string Key { get; set; } = null!;

    public string Value { get; set; } = null!;

    public Guid? UpdatedBy { get; set; }
    public User? UpdatedByUser { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
