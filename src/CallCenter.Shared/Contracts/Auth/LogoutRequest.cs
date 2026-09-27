using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Auth;

/// <summary>Closes the session opened by a login (A-05).</summary>
/// <param name="SessionId">From <see cref="LoginResponse.SessionId"/>.</param>
/// <param name="Reason">One of <see cref="LogoutReasons"/>.</param>
public record LogoutRequest(
    Guid SessionId,
    [MaxLength(16)] string? Reason = null);

/// <summary>Allowed values of <c>agent_sessions.logout_reason</c>.</summary>
public static class LogoutReasons
{
    /// <summary>The agent pressed Log out at the end of the shift.</summary>
    public const string Manual = "Manual";

    /// <summary>The idle timer fired (A-05, setting <c>agent.idle_logout_minutes</c>).</summary>
    public const string Idle = "Idle";

    /// <summary>The app was closed with the agent still signed in.</summary>
    public const string AppClosed = "AppClosed";

    /// <summary>A supervisor ended the session.</summary>
    public const string Forced = "Forced";

    /// <summary>
    /// The account's password was reset from the server console, which closes
    /// every session it had open. Told apart from <see cref="Forced"/> because a
    /// row of sessions ending at once is worth being able to explain.
    /// </summary>
    public const string PasswordReset = "PasswordReset";

    /// <summary>
    /// The agent signed in to the Agent App on another laptop, and the server
    /// closed this session so that one agent has one phone (N-05, 27 Sep
    /// evening). Set by the server only, so it is not in <see cref="All"/>,
    /// the list a client may send. The app it closes is told through
    /// <see cref="SessionClosedHeader"/>, and stops its phone without the
    /// un-REGISTER that would take the new laptop's registration away too.
    /// </summary>
    public const string SignedInElsewhere = "SignedInElsewhere";

    /// <summary>
    /// The response header a 401 carries when the token was refused because
    /// its session was closed, with the session's <c>logout_reason</c>. The
    /// Agent App reads it to tell "signed in on another laptop" from every
    /// other way a sign-in can end.
    /// </summary>
    public const string SessionClosedHeader = "X-Session-Closed";

    /// <summary>What a client may send in <see cref="LogoutRequest.Reason"/>.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { Manual, Idle, AppClosed, Forced, PasswordReset };
}
