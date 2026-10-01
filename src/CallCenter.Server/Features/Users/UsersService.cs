using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Users;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Users;

/// <summary>
/// Managing agent and supervisor accounts, and assigning the two extensions
/// each agent registers with (S-42).
/// </summary>
public class UsersService(
    CallCenterDbContext db,
    ISipSecretProtector secrets,
    ILogger<UsersService> logger)
{
    /// <summary>Why a change was refused. The controller maps each to a reply.</summary>
    public enum Failure
    {
        NotFound,
        LoginTaken,
        UnknownRole,

        /// <summary>Only agents register an extension, so only agents can have one.</summary>
        NotAnAgent,

        /// <summary>Would leave no active supervisor, locking everyone out of S-42.</summary>
        LastSupervisor,

        /// <summary>A supervisor disabling their own account mid-session.</summary>
        CannotDisableSelf,

        /// <summary>Changing one's own password without giving the current one.</summary>
        CurrentPasswordRequired,

        /// <summary>Changing one's own password, and the current one given is wrong.</summary>
        CurrentPasswordWrong,
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var users = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Role == UserRoles.Supervisor ? 0 : 1)
            .ThenBy(u => u.DisplayName)
            .ToListAsync(ct);

        var apps = await LatestAppSignInsAsync(null, ct);
        return users.Select(u => ToDto(u, apps.GetValueOrDefault(u.Id))).ToList();
    }

    public async Task<UserDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? null : await ToDtoAsync(user, ct);
    }

    /// <summary>Creates an account (S-42).</summary>
    public async Task<(UserDto? User, Failure? Failure)> CreateAsync(
        CreateUserRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        if (!UserRoles.All.Contains(request.Role))
        {
            return (null, Failure.UnknownRole);
        }

        var login = request.Login.Trim();
        if (await db.Users.AnyAsync(u => u.Login.ToLower() == login.ToLower(), ct))
        {
            return (null, Failure.LoginTaken);
        }

        var user = new User
        {
            Login = login,
            DisplayName = request.DisplayName.Trim(),
            Role = request.Role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsActive = true,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        await AuditAsync(actingUserId, "create", user, before: null, ct);
        logger.LogInformation("{Role} {Login} created", user.Role, user.Login);

        return (ToDto(user), null);
    }

    /// <summary>Renames an account, or enables and disables it (S-42).</summary>
    public async Task<(UserDto? User, Failure? Failure)> UpdateAsync(
        Guid id, UpdateUserRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return (null, Failure.NotFound);
        }

        if (!request.IsActive)
        {
            // Signing yourself out of the only door is not a change worth
            // allowing by accident.
            if (id == actingUserId)
            {
                return (null, Failure.CannotDisableSelf);
            }

            if (user.Role == UserRoles.Supervisor && !await AnotherActiveSupervisorExistsAsync(id, ct))
            {
                return (null, Failure.LastSupervisor);
            }
        }

        var before = Snapshot(user);

        user.DisplayName = request.DisplayName.Trim();
        user.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);
        await AuditAsync(actingUserId, "update", user, before, ct);

        return (await ToDtoAsync(user, ct), null);
    }

    /// <summary>
    /// Assigns the agent's extension and, when supplied, its SIP secret (S-42).
    /// A null secret keeps whatever is stored, so a number can be corrected
    /// without re-entering the password.
    /// </summary>
    public async Task<(UserDto? User, Failure? Failure)> SetExtensionAsync(
        Guid id, SetExtensionRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return (null, Failure.NotFound);
        }

        if (user.Role != UserRoles.Agent)
        {
            return (null, Failure.NotAnAgent);
        }

        var before = Snapshot(user);

        user.Extension = request.Extension.Trim();

        if (!string.IsNullOrWhiteSpace(request.Secret))
        {
            user.SipSecret = secrets.Protect(request.Secret);
        }

        await db.SaveChangesAsync(ct);
        await AuditAsync(actingUserId, "update", user, before, ct);

        logger.LogInformation("Extension {Extension} set for {Login}", user.Extension, user.Login);

        return (await ToDtoAsync(user, ct), null);
    }

    /// <summary>
    /// Sets a new password (S-42). Another account's needs no old one; the
    /// supervisor's own needs the current one (27 Sep review), so a browser
    /// left signed in cannot be used to lock its owner out.
    /// </summary>
    public async Task<Failure?> ResetPasswordAsync(
        Guid id, ResetPasswordRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return Failure.NotFound;
        }

        if (id == actingUserId)
        {
            if (string.IsNullOrEmpty(request.CurrentPassword))
            {
                return Failure.CurrentPasswordRequired;
            }

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            {
                logger.LogWarning("{Login} tried to change their own password and gave the wrong current one", user.Login);
                return Failure.CurrentPasswordWrong;
            }
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await db.SaveChangesAsync(ct);

        // The audit records that it happened, never the password or its hash.
        await AuditAsync(actingUserId, "reset-password", user, before: null, ct);
        logger.LogInformation("Password reset for {Login}", user.Login);

        return null;
    }

    private Task<bool> AnotherActiveSupervisorExistsAsync(Guid excluding, CancellationToken ct) =>
        db.Users.AnyAsync(
            u => u.Id != excluding && u.Role == UserRoles.Supervisor && u.IsActive, ct);

    /// <summary>
    /// The version and time of each account's latest Agent App sign-in, or of
    /// <paramref name="userId"/>'s only. The app sends its version at every
    /// sign-in; a web sign-in sends none and has no session.
    /// </summary>
    private async Task<Dictionary<Guid, AppSignIn>> LatestAppSignInsAsync(Guid? userId, CancellationToken ct)
    {
        var sessions = db.AgentSessions.AsNoTracking().Where(s => s.AppVersion != null);
        if (userId is { } id)
        {
            sessions = sessions.Where(s => s.UserId == id);
        }

        var latest = await sessions
            .GroupBy(s => s.UserId)
            .Select(g => g.OrderByDescending(s => s.LoggedInAt)
                .Select(s => new { s.UserId, s.AppVersion, s.LoggedInAt })
                .First())
            .ToListAsync(ct);

        return latest.ToDictionary(s => s.UserId, s => new AppSignIn(s.AppVersion!, s.LoggedInAt));
    }

    private async Task<UserDto> ToDtoAsync(User user, CancellationToken ct) =>
        ToDto(user, (await LatestAppSignInsAsync(user.Id, ct)).GetValueOrDefault(user.Id));

    private static UserDto ToDto(User user, AppSignIn? app = null) => new(
        user.Id,
        user.Login,
        user.DisplayName,
        user.Role,
        user.IsActive,
        user.Extension,
        user.Extension is not null && user.SipSecret is not null,
        user.CreatedAt,
        user.LastLoginAt,
        app?.Version,
        app?.At);

    private sealed record AppSignIn(string Version, DateTimeOffset At);

    /// <summary>
    /// What an account looked like, for the audit trail. Never includes the
    /// password hash or the SIP secrets — an audit row is read by people.
    /// </summary>
    private static JsonDocument Snapshot(User user) => JsonSerializer.SerializeToDocument(new
    {
        user.Login,
        user.DisplayName,
        user.Role,
        user.IsActive,
        user.Extension,
    });

    private async Task AuditAsync(
        Guid actingUserId, string action, User user, JsonDocument? before, CancellationToken ct)
    {
        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = actingUserId,
            Entity = "user",
            EntityId = user.Id.ToString(),
            Action = action,
            Before = before,
            After = Snapshot(user),
        });

        await db.SaveChangesAsync(ct);
    }
}
