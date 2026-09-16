using System.Security.Claims;

namespace CallCenter.Server.Features.Auth;

/// <summary>Reads the claims this API issues off the signed-in caller.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's <c>users.id</c>, or null when not signed in.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>The caller's <c>users.id</c>. For endpoints that require authentication.</summary>
    public static Guid GetRequiredUserId(this ClaimsPrincipal principal) =>
        principal.GetUserId()
        ?? throw new InvalidOperationException("The caller has no user id claim.");

    /// <summary>The <c>agent_sessions</c> row this token was issued for, if any.</summary>
    public static Guid? GetSessionId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(AppClaims.SessionId), out var id) ? id : null;
}
