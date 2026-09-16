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

    public static readonly IReadOnlyList<string> All = new[] { Manual, Idle, AppClosed, Forced };
}
