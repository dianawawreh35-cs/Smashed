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

    /// <summary>
    /// The laptop and the install of the app on it: machine name and a tag
    /// made once per install (since 27 Sep; rows before that have the machine
    /// name alone).
    /// </summary>
    public string LaptopId { get; set; } = null!;

    public string? AppVersion { get; set; }

    public DateTimeOffset LoggedInAt { get; set; }

    public DateTimeOffset? LoggedOutAt { get; set; }

    /// <summary>
    /// The last request the app made with this session's token, to within
    /// <see cref="Features.Auth.SessionPresence.WriteEvery"/>. What "online"
    /// is judged by: a laptop that crashed or lost power never signs out, but
    /// it stops being heard from.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>
    /// Whether the app on this laptop has do not disturb on (A-18), as it last
    /// said, for the break monitor (S-66). Null until it says: an app from
    /// before 3 Oct 2026 never does.
    /// </summary>
    public bool? DoNotDisturb { get; set; }

    /// <summary>
    /// When <see cref="DoNotDisturb"/> was last switched, by the laptop's clock,
    /// no earlier than the sign-in and no later than the server's now.
    /// </summary>
    public DateTimeOffset? DoNotDisturbSince { get; set; }

    /// <summary>One of <see cref="CallCenter.Shared.Contracts.Auth.LogoutReasons"/>.</summary>
    public string? LogoutReason { get; set; }
}
