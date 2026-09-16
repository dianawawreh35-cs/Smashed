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

        /// <summary>Only agents register extensions, so only agents can have them.</summary>
        NotAnAgent,

        /// <summary>Would leave no active supervisor, locking everyone out of S-42.</summary>
        LastSupervisor,

        /// <summary>A supervisor disabling their own account mid-session.</summary>
        CannotDisableSelf,
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default) =>
        await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Role == UserRoles.Supervisor ? 0 : 1)
            .ThenBy(u => u.DisplayName)
            .Select(u => ToDto(u))
            .ToListAsync(ct);

    public async Task<UserDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? null : ToDto(user);
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

        return (ToDto(user), null);
    }

    /// <summary>
    /// Assigns the two extensions and, when supplied, their SIP secrets (S-42).
    /// A null secret keeps whatever is stored, so a number can be corrected
    /// without re-entering the password.
    /// </summary>
    public async Task<(UserDto? User, Failure? Failure)> SetExtensionsAsync(
        Guid id, SetExtensionsRequest request, Guid actingUserId, CancellationToken ct = default)
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

        user.CustomerExtension = request.CustomerExtension.Trim();
        user.InternalExtension = request.InternalExtension.Trim();

        if (!string.IsNullOrWhiteSpace(request.CustomerSecret))
        {
            user.CustomerSipSecret = secrets.Protect(request.CustomerSecret);
        }

        if (!string.IsNullOrWhiteSpace(request.InternalSecret))
        {
            user.InternalSipSecret = secrets.Protect(request.InternalSecret);
        }

        await db.SaveChangesAsync(ct);
        await AuditAsync(actingUserId, "update", user, before, ct);

        logger.LogInformation(
            "Extensions set for {Login}: customer {Customer}, internal {Internal}",
            user.Login, user.CustomerExtension, user.InternalExtension);

        return (ToDto(user), null);
    }

    /// <summary>Sets a new password (S-42). The old one is not needed.</summary>
    public async Task<Failure?> ResetPasswordAsync(
        Guid id, ResetPasswordRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return Failure.NotFound;
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

    private static UserDto ToDto(User user) => new(
        user.Id,
        user.Login,
        user.DisplayName,
        user.Role,
        user.IsActive,
        user.CustomerExtension,
        user.InternalExtension,
        user.CustomerExtension is not null
            && user.InternalExtension is not null
            && user.CustomerSipSecret is not null
            && user.InternalSipSecret is not null,
        user.CreatedAt,
        user.LastLoginAt);

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
        user.CustomerExtension,
        user.InternalExtension,
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
