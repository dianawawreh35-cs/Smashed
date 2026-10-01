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

        // An agent session belongs to a laptop running the Agent App (A-05): the
        // row records which machine, and the SIP secret is for the softphone on
        // it. The web app sends no laptop id, and it refuses agents once it sees
        // the role. So an agent who signs in there must leave nothing behind: no
        // session row counting them as signed in, and no secret in a browser
        // (N-05).
        var fromAgentApp = !string.IsNullOrWhiteSpace(request.LaptopId);

        // Only agents get a session row: it is what the idle-logout timer closes
        // and what presence is reported against (A-05, A-83).
        AgentSession? session = null;
        if (user.Role == UserRoles.Agent && fromAgentApp)
        {
            var now = DateTimeOffset.UtcNow;
            var laptopId = request.LaptopId!.Trim();

            await CloseOtherSessionsAsync(user, laptopId, now, ct);

            session = new AgentSession
            {
                UserId = user.Id,
                LaptopId = laptopId,
                AppVersion = request.AppVersion,
                LoggedInAt = now,
                LastSeenAt = now,
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
            "{Role} {Login} signed in from {LaptopId}",
            user.Role,
            user.Login,
            fromAgentApp ? request.LaptopId!.Trim() : "the web app");

        return (new LoginResponse(
            token,
            expiresAt,
            ToDto(user),
            sessionId,
            fromAgentApp ? await BuildExtensionsAsync(user, ct) : null), null);
    }

    /// <summary>
    /// One Agent App sign-in per agent (N-05, 27 Sep evening): signing in
    /// closes every session the agent still has open, saved with the new one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On 27 Sep calls were missed because a second copy of the app was signed
    /// in as the same agent and held the extension's registration at the PBX,
    /// so the rings went where nobody was looking. A closed session ends its
    /// token (M-S01), so the other copy's next request is a 401 and it signs
    /// itself out.
    /// </para>
    /// <para>
    /// <b>Another laptop</b> gets <see cref="LogoutReasons.SignedInElsewhere"/>,
    /// which its app shows the agent, and which tells it to leave the PBX
    /// registration alone: on chan_sip any un-REGISTER removes the extension's
    /// one address, and by then that address is this laptop's.
    /// <b>The same install signing in again</b> (the same laptop id, which
    /// since 27 Sep carries a tag per install) is closing a session whose app
    /// is gone, since one install runs one copy; it is recorded as
    /// <see cref="LogoutReasons.AppClosed"/>, which is what happened to it.
    /// </para>
    /// </remarks>
    private async Task CloseOtherSessionsAsync(User user, string laptopId, DateTimeOffset now, CancellationToken ct)
    {
        var open = await db.AgentSessions
            .Where(s => s.UserId == user.Id && s.LoggedOutAt == null)
            .ToListAsync(ct);

        foreach (var other in open)
        {
            other.LoggedOutAt = now;
            other.LogoutReason = other.LaptopId == laptopId
                ? LogoutReasons.AppClosed
                : LogoutReasons.SignedInElsewhere;

            logger.LogInformation(
                "Agent {Login} signed in from {LaptopId}: closed session {SessionId} on {OtherLaptopId} ({Reason})",
                user.Login, laptopId, other.Id, other.LaptopId, other.LogoutReason);
        }

        // A-86: a break left going on those laptops ends with them.
        await Breaks.BreakClock.CloseForSessionsAsync(db, open.Select(s => s.Id).ToList(), now, ct);
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

        // A-86: the app ends a break before it signs out; this is for the one
        // whose Break out could not be sent first.
        await Breaks.BreakClock.CloseForSessionsAsync(db, [session.Id], session.LoggedOutAt.Value, ct);

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
    /// The agent's extension with its secret decrypted (A-01). Null when the
    /// account is a supervisor, when the supervisor has not assigned an extension
    /// yet, or when the PBX address is still blank — in each case the app signs
    /// in and reports the phone as unconfigured.
    /// </summary>
    private async Task<AgentExtensionsDto?> BuildExtensionsAsync(User user, CancellationToken ct)
    {
        if (user.Role != UserRoles.Agent)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(user.Extension))
        {
            logger.LogWarning("Agent {Login} has no extension configured; signing in without a phone.", user.Login);
            return null;
        }

        var secret = secrets.Unprotect(user.SipSecret);

        if (secret is null)
        {
            logger.LogWarning("Agent {Login} has an unreadable SIP secret; signing in without a phone.", user.Login);
            return null;
        }

        var sipServer = await GetSipServerAsync(ct);
        if (string.IsNullOrWhiteSpace(sipServer))
        {
            logger.LogWarning("Setting 'pbx.host' is blank; signing in without a phone.");
            return null;
        }

        return new AgentExtensionsDto(
            sipServer,
            user.Extension,
            secret);
    }

    /// <summary>
    /// The PBX address the app registers to: the <c>pbx.host</c> setting the
    /// supervisor fills in at installation, falling back to configuration so a
    /// developer machine can point at a test PBX without touching the database.
    /// </summary>
    private async Task<string?> GetSipServerAsync(CancellationToken ct)
    {
        var fromDatabase = await db.Settings
            .Where(s => s.Key == "pbx.host")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(fromDatabase) ? configuration["Sip:Server"] : fromDatabase;
    }
}
