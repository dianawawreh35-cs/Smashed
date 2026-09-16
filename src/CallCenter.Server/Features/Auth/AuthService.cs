using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Auth;

/// <summary>
/// Login, logout and the SIP credentials handed to the Agent App (A-01, A-05, S-01).
/// </summary>
public class AuthService(
    CallCenterDbContext db,
    TokenService tokens,
    ISipSecretProtector secrets,
    IConfiguration configuration,
    ILogger<AuthService> logger)
{
    /// <summary>Why a login was refused. The API turns every value into the same reply.</summary>
    public enum LoginFailure
    {
        UnknownLoginOrPassword,
        AccountDisabled,
    }

    /// <summary>
    /// Verifies the credentials, opens an agent session and issues a token.
    /// </summary>
    /// <returns>The response, or the reason it was refused.</returns>
    public async Task<(LoginResponse? Response, LoginFailure? Failure)> LoginAsync(
        LoginRequest request, CancellationToken ct = default)
    {
        var login = request.Login.Trim();

        // users.login has a unique index but is stored as typed, so match
        // case-insensitively: agents type their name in whatever case they like.
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Login.ToLower() == login.ToLower(), ct);

        // Verify even when the user is missing, so a wrong username and a wrong
        // password take the same time to answer.
        var passwordOk = VerifyPassword(request.Password, user?.PasswordHash);

        if (user is null || !passwordOk)
        {
            logger.LogInformation("Failed login for {Login}", login);
            return (null, LoginFailure.UnknownLoginOrPassword);
        }

        if (!user.IsActive)
        {
            logger.LogInformation("Login refused for disabled account {Login}", login);
            return (null, LoginFailure.AccountDisabled);
        }

        // Only agents get a session row: it is what the idle-logout timer closes
        // and what presence is reported against (A-05, A-83).
        AgentSession? session = null;
        if (user.Role == UserRoles.Agent)
        {
            session = new AgentSession
            {
                UserId = user.Id,
                LaptopId = string.IsNullOrWhiteSpace(request.LaptopId) ? "unknown" : request.LaptopId.Trim(),
                AppVersion = request.AppVersion,
                LoggedInAt = DateTimeOffset.UtcNow,
            };

            db.AgentSessions.Add(session);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // agent_sessions.id is filled in by Postgres (gen_random_uuid), so the
        // value is only there once the insert has run.
        var sessionId = session?.Id;

        var (token, expiresAt) = tokens.Issue(user, sessionId);

        logger.LogInformation(
            "{Role} {Login} signed in from {LaptopId}", user.Role, user.Login, request.LaptopId ?? "unknown");

        return (new LoginResponse(
            token,
            expiresAt,
            ToDto(user),
            sessionId,
            await BuildExtensionsAsync(user, ct)), null);
    }

    /// <summary>
    /// Closes the session opened by a login. Idempotent: closing a session that
    /// is already closed, or one belonging to someone else, changes nothing.
    /// </summary>
    public async Task LogoutAsync(Guid userId, Guid sessionId, string? reason, CancellationToken ct = default)
    {
        var session = await db.AgentSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.LoggedOutAt == null, ct);

        if (session is null)
        {
            return;
        }

        session.LoggedOutAt = DateTimeOffset.UtcNow;
        session.LogoutReason = LogoutReasons.All.Contains(reason) ? reason : LogoutReasons.Manual;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Session {SessionId} closed: {Reason}", sessionId, session.LogoutReason);
    }

    /// <summary>The signed-in account, or null when the row has since been removed.</summary>
    public async Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null || !user.IsActive ? null : ToDto(user);
    }

    private static CurrentUserDto ToDto(User user) =>
        new(user.Id, user.Login, user.DisplayName, user.Role);

    /// <summary>
    /// A hash that never matches, so an unknown username still costs one BCrypt
    /// verification. Without it the response time says whether a name exists.
    /// </summary>
    private const string DummyHash =
        "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

    private static bool VerifyPassword(string password, string? hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash ?? DummyHash) && hash is not null;
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    /// <summary>
    /// The agent's two extensions with their secrets decrypted (A-01). Null when
    /// the account is a supervisor, when the supervisor has not filled the
    /// extensions in yet, or when the PBX address is still blank — in each case
    /// the app signs in and reports the phone as unconfigured.
    /// </summary>
    private async Task<AgentExtensionsDto?> BuildExtensionsAsync(User user, CancellationToken ct)
    {
        if (user.Role != UserRoles.Agent)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(user.CustomerExtension) ||
            string.IsNullOrWhiteSpace(user.InternalExtension))
        {
            logger.LogWarning("Agent {Login} has no extensions configured; signing in without a phone.", user.Login);
            return null;
        }

        var customerSecret = secrets.Unprotect(user.CustomerSipSecret);
        var internalSecret = secrets.Unprotect(user.InternalSipSecret);

        if (customerSecret is null || internalSecret is null)
        {
            logger.LogWarning("Agent {Login} has an unreadable SIP secret; signing in without a phone.", user.Login);
            return null;
        }

        var sipServer = await GetSipServerAsync(ct);
        if (string.IsNullOrWhiteSpace(sipServer))
        {
            logger.LogWarning("Setting 'pbx.ip' is blank; signing in without a phone.");
            return null;
        }

        return new AgentExtensionsDto(
            sipServer,
            user.CustomerExtension,
            customerSecret,
            user.InternalExtension,
            internalSecret);
    }

    /// <summary>
    /// The PBX address the app registers to: the <c>pbx.ip</c> setting the
    /// supervisor fills in at installation, falling back to configuration so a
    /// developer machine can point at a test PBX without touching the database.
    /// </summary>
    private async Task<string?> GetSipServerAsync(CancellationToken ct)
    {
        var fromDatabase = await db.Settings
            .Where(s => s.Key == "pbx.ip")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(fromDatabase) ? configuration["Sip:Server"] : fromDatabase;
    }
}
