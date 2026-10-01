namespace CallCenter.Server.Data.Entities;

/// <summary>
/// One break an agent took, from Break in to Break out (A-86). Table
/// <c>agent_breaks</c>.
/// </summary>
/// <remarks>
/// The id is made by the Agent App when the agent presses Break in, so the
/// break can wait in the laptop's offline queue (A-04) and be sent again
/// without becoming two.
///
/// <b>A break the app never ended is not left open for ever.</b> While
/// <see cref="EndedAt"/> is null, the end is read from the sign-in it was taken
/// under (<see cref="Features.Breaks.BreakClock"/>): when that sign-in ended, or
/// when the app was last heard from. The server writes that end into the row
/// when the agent next starts a break.
/// </remarks>
public class AgentBreak
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>The sign-in it was taken under. Null only if that row has gone.</summary>
    public Guid? SessionId { get; set; }
    public AgentSession? Session { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while the break is going, or until the server works its end out.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>One of <see cref="CallCenter.Shared.BreakEndings"/>; null exactly when <see cref="EndedAt"/> is.</summary>
    public string? EndedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
