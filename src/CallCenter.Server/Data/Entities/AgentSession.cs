namespace CallCenter.Server.Data.Entities;

/// <summary>
/// One agent login on one laptop. Table <c>agent_sessions</c>.
/// </summary>
/// <remarks>
/// Laptops are shared across shifts (A-05), so this is what ties a stretch of
/// work to a person and a machine, and what the idle-logout timer closes.
/// </remarks>
public class AgentSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Machine name of the laptop.</summary>
    public string LaptopId { get; set; } = null!;

    public string? AppVersion { get; set; }

    public DateTimeOffset LoggedInAt { get; set; }

    public DateTimeOffset? LoggedOutAt { get; set; }

    /// <summary>Manual, Idle, AppClosed or Forced.</summary>
    public string? LogoutReason { get; set; }
}
