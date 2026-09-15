using System.Text.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Data.Seed;

/// <summary>
/// Puts the starting data from <c>docs/SCHEMA.md</c> section 7 into an empty
/// database, and creates the first supervisor account.
/// </summary>
/// <remarks>
/// Every step is idempotent: rows are inserted only when missing, and nothing is
/// overwritten. Running it twice is harmless, which matters because it is typed
/// by hand during installation (runbook step 7) and is easy to repeat after a
/// half-finished attempt.
/// </remarks>
public class DatabaseSeeder(CallCenterDbContext db, ILogger<DatabaseSeeder> logger)
{
    /// <summary>What a run changed, so the caller can report it.</summary>
    public record Result(
        int BranchesAdded,
        int ChannelsAdded,
        int TypesAdded,
        bool FormAdded,
        int SettingsAdded,
        bool UserCreated,
        string? UserSkippedReason);

    /// <summary>
    /// Seeds reference data, and creates the first user when
    /// <paramref name="adminLogin"/> is supplied.
    /// </summary>
    /// <param name="branches">
    /// Overrides the default branch names. Ignored if branches already exist.
    /// </param>
    public async Task<Result> SeedAsync(
        string? adminLogin = null,
        string? adminPassword = null,
        string? adminDisplayName = null,
        IReadOnlyList<string>? branches = null,
        CancellationToken cancellationToken = default)
    {
        var branchesAdded = await SeedBranchesAsync(branches, cancellationToken);
        var channelsAdded = await SeedChannelsAsync(cancellationToken);
        var typesAdded = await SeedClassificationTypesAsync(cancellationToken);
        var formAdded = await SeedFormDefinitionAsync(cancellationToken);
        var settingsAdded = await SeedSettingsAsync(cancellationToken);

        var (userCreated, skipped) = await SeedFirstUserAsync(
            adminLogin, adminPassword, adminDisplayName, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var result = new Result(
            branchesAdded, channelsAdded, typesAdded, formAdded, settingsAdded, userCreated, skipped);

        logger.LogInformation(
            "Seed complete: {Branches} branches, {Channels} channels, {Types} types, "
            + "form v1 {Form}, {Settings} settings, user {User}",
            branchesAdded, channelsAdded, typesAdded,
            formAdded ? "created" : "already present",
            settingsAdded,
            userCreated ? "created" : skipped ?? "not requested");

        return result;
    }

    private async Task<int> SeedBranchesAsync(IReadOnlyList<string>? names, CancellationToken ct)
    {
        if (await db.Branches.AnyAsync(ct))
        {
            return 0;
        }

        var toAdd = (names is { Count: > 0 } ? names : SeedData.Branches)
            .Select((name, i) => new Branch { Name = name, SortOrder = i })
            .ToList();

        db.Branches.AddRange(toAdd);
        return toAdd.Count;
    }

    private async Task<int> SeedChannelsAsync(CancellationToken ct)
    {
        var existing = await db.Channels.Select(c => c.Name).ToListAsync(ct);
        var added = 0;

        for (var i = 0; i < SeedData.Channels.Length; i++)
        {
            var (name, isSystem) = SeedData.Channels[i];
            if (existing.Contains(name))
            {
                continue;
            }

            db.Channels.Add(new Channel { Name = name, IsSystem = isSystem, SortOrder = i });
            added++;
        }

        return added;
    }

    private async Task<int> SeedClassificationTypesAsync(CancellationToken ct)
    {
        var existing = await db.ClassificationTypes.Select(t => t.Name).ToListAsync(ct);
        var added = 0;

        for (var i = 0; i < SeedData.ClassificationTypes.Length; i++)
        {
            var (name, ar, en, colour, isSystem) = SeedData.ClassificationTypes[i];
            if (existing.Contains(name))
            {
                continue;
            }

            db.ClassificationTypes.Add(new ClassificationType
            {
                Name = name,
                LabelAr = ar,
                LabelEn = en,
                Colour = colour,
                IsSystem = isSystem,
                SortOrder = i,
            });
            added++;
        }

        return added;
    }

    private async Task<bool> SeedFormDefinitionAsync(CancellationToken ct)
    {
        if (await db.FormDefinitions.AnyAsync(ct))
        {
            return false;
        }

        db.FormDefinitions.Add(new FormDefinition
        {
            Version = 1,
            Definition = JsonDocument.Parse(SeedData.FormDefinitionV1),
            IsCurrent = true,
        });

        return true;
    }

    private async Task<int> SeedSettingsAsync(CancellationToken ct)
    {
        var existing = await db.Settings.Select(s => s.Key).ToListAsync(ct);
        var added = 0;

        foreach (var (key, value) in SeedData.Settings)
        {
            if (existing.Contains(key))
            {
                continue;
            }

            db.Settings.Add(new Setting { Key = key, Value = value });
            added++;
        }

        return added;
    }

    /// <summary>
    /// Creates the first supervisor. Refuses if any user already exists — this
    /// is an installation command, not a way to add accounts, and it must not be
    /// usable to mint a supervisor on a running system.
    /// </summary>
    private async Task<(bool Created, string? SkippedReason)> SeedFirstUserAsync(
        string? login, string? password, string? displayName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return (false, null);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, "no password supplied");
        }

        if (await db.Users.AnyAsync(ct))
        {
            logger.LogWarning(
                "Users already exist; not creating {Login}. Add accounts through the supervisor app (S-42).",
                login);
            return (false, "users already exist");
        }

        db.Users.Add(new User
        {
            Login = login.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? login.Trim() : displayName.Trim(),
            Role = SeedData.FirstUserRole,
            IsActive = true,
        });

        return (true, null);
    }
}
