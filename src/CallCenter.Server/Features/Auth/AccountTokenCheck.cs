using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CallCenter.Server.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Auth;

/// <summary>
/// Whether a token that is correctly signed and unexpired still speaks for the
/// account as it is now (N-05).
/// </summary>
/// <remarks>
/// <para>
/// A token is good for 12 hours from sign-in, and on its own it cannot know
/// what has happened to the account since then. Without this check a password
/// reset, a disabled account or a change of role left every token already
/// issued working until it expired. If a password was reset because it leaked,
/// whoever held a token kept it.
/// </para>
/// <para>
/// Each token carries a <see cref="AppClaims.Stamp"/>, a fingerprint of the
/// account's password hash and role at the moment it was issued. On every
/// request the fingerprint is worked out again from the row, and the token is
/// refused if the two differ or the account is disabled. So every way of
/// changing those things revokes the tokens with no extra code: the Users
/// screen, <c>reset-password</c> on the command line, a disable, a promotion.
/// </para>
/// <para>
/// It costs one primary-key read per signed-in request, two columns and a
/// flag. The fingerprint is a hash of a hash: it reveals nothing about the
/// password, and it changes whenever the stored hash does.
/// </para>
/// </remarks>
public class AccountTokenCheck(CallCenterDbContext db, ILogger<AccountTokenCheck> logger)
{
    /// <summary>The fingerprint a token issued now would carry.</summary>
    public static string Stamp(string passwordHash, string role)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{passwordHash}|{role}"));
        return Convert.ToBase64String(bytes, 0, 12);
    }

    /// <summary>
    /// True when the account still exists, is enabled, and has the password and
    /// role it had when <paramref name="principal"/>'s token was issued.
    /// </summary>
    public virtual async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return false;
        }

        // A token issued before stamps existed has none, and is refused like a
        // stale one: everybody signs in once more after the upgrade, and after
        // that every token can be checked.
        var stamp = principal.FindFirstValue(AppClaims.Stamp);
        if (stamp is null)
        {
            return false;
        }

        var account = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.PasswordHash, u.Role, u.IsActive })
            .FirstOrDefaultAsync(ct);

        return account is not null
               && account.IsActive
               && stamp == Stamp(account.PasswordHash, account.Role);
    }

    /// <summary>
    /// Plugged into <see cref="JwtBearerEvents.OnTokenValidated"/>: runs after the
    /// signature and expiry have been checked, and turns a stale token into a 401.
    /// </summary>
    public static Task OnTokenValidatedAsync(TokenValidatedContext context) =>
        context.HttpContext.RequestServices.GetRequiredService<AccountTokenCheck>().RefuseIfStaleAsync(context);

    private async Task RefuseIfStaleAsync(TokenValidatedContext context)
    {
        if (context.Principal is null
            || !await IsCurrentAsync(context.Principal, context.HttpContext.RequestAborted))
        {
            logger.LogInformation(
                "Refused a token for {UserId}: the account's password, role or status has changed since it was issued",
                context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown");

            context.Fail("The account has changed since this token was issued.");
        }
    }
}
