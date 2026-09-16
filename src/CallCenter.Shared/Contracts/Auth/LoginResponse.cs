namespace CallCenter.Shared.Contracts.Auth;

/// <summary>
/// A successful login (A-01).
/// </summary>
/// <param name="AccessToken">Bearer token for the API and the SignalR hub.</param>
/// <param name="ExpiresAt">When <paramref name="AccessToken"/> stops being accepted.</param>
/// <param name="SessionId">
/// The <c>agent_sessions</c> row opened by this login, passed back to
/// <c>POST /api/auth/logout</c> so the session can be closed with a reason (A-05).
/// Null for a supervisor, who gets no session row.
/// </param>
/// <param name="Extensions">
/// The SIP credentials to register with. Null for a supervisor, and for an agent
/// whose extensions the supervisor has not filled in yet — the app then signs in
/// but shows the phone as unconfigured rather than failing the login.
/// </param>
public record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    CurrentUserDto User,
    Guid? SessionId,
    AgentExtensionsDto? Extensions);
