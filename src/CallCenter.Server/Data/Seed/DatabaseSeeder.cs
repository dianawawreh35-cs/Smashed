using System.Text.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared.Text;
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
public class DatabaseSeeder(
    CallCenterDbContext db,
    Features.Menu.MenuImageStore images,
    ILogger<DatabaseSeeder> logger)
{
    /// <summary>What a run changed, so the caller can report it.</summary>
    public record Result(
        int BranchesAdded,
        int ChannelsAdded,
        int TypesAdded,
        bool FormAdded,
        int SettingsAdded,
        int DeliveryAreasAdded,
        int MenuItemsAdded,
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

        // After the branches, and after their save: each area needs a branch id.
        await db.SaveChangesAsync(cancellationToken);
        var areasAdded = await SeedDeliveryAreasAsync(cancellationToken);
        var menuAdded = await SeedMenuAsync(cancellationToken);

        var (userCreated, skipped) = await SeedFirstUserAsync(
            adminLogin, adminPassword, adminDisplayName, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var result = new Result(
            branchesAdded, channelsAdded, typesAdded, formAdded, settingsAdded,
            areasAdded, menuAdded, userCreated, skipped);

        logger.LogInformation(
            "Seed complete: {Branches} branches, {Channels} channels, {Types} types, "
            + "form v1 {Form}, {Settings} settings, {Areas} delivery areas, "
            + "{Menu} menu items, user {User}",
            branchesAdded, channelsAdded, typesAdded,
            formAdded ? "created" : "already present",
            settingsAdded, areasAdded, menuAdded,
            userCreated ? "created" : skipped ?? "not requested");

        return result;
    }

    /// <summary>
    /// The branches' delivery price lists (A-65, S-58).
    /// </summary>
    /// <remarks>
    /// Skipped entirely once any area exists, like every other part of this
    /// seeder: these are starting contents, and a supervisor who has since
    /// edited a price must not have it put back on the next run.
    ///
    /// An area whose branch is not in the database is skipped with a warning
    /// rather than failing the seed. It means somebody renamed a branch before
    /// seeding, and losing one area is better than losing the installation.
    /// </remarks>
    private async Task<int> SeedDeliveryAreasAsync(CancellationToken ct)
    {
        if (await db.DeliveryAreas.AnyAsync(ct))
        {
            return 0;
        }

        var branchIds = await db.Branches
            .ToDictionaryAsync(b => b.Name, b => b.Id, ct);

        var added = 0;
        var seen = new HashSet<string>();

        foreach (var (area, branch, price) in SeedData.DeliveryAreas)
        {
            if (!branchIds.TryGetValue(branch, out var branchId))
            {
                logger.LogWarning(
                    "Delivery area {Area} names branch {Branch}, which does not exist; skipped",
                    area, branch);
                continue;
            }

            var normalised = NameNormalizer.Normalize(area);

            // The unique index would refuse a repeat anyway; catching it here
            // means the seed reports it rather than throwing.
            if (normalised.Length == 0 || !seen.Add(normalised))
            {
                logger.LogWarning("Delivery area {Area} is a duplicate or has no name; skipped", area);
                continue;
            }

            db.DeliveryAreas.Add(new DeliveryArea
            {
                Name = area,
                NameNormalised = normalised,
                BranchId = branchId,
                Price = price,
            });

            added++;
        }

        return added;
    }

    /// <summary>
    /// The menu, with its pictures (A-66).
    /// </summary>
    /// <remarks>
    /// Skipped once any item exists, like everything else here: a supervisor who
    /// has since changed a price must not have it put back on the next run.
    ///
    /// The pictures are embedded resources, so a fresh install needs the one DLL
    /// and no folder beside it. An item whose picture is missing from the
    /// assembly is still created — a menu without a photograph is usable, and
    /// failing the whole seed over one image would not be.
    /// </remarks>
    private async Task<int> SeedMenuAsync(CancellationToken ct)
    {
        if (await db.MenuItems.AnyAsync(ct))
        {
            await RestoreMenuImagesAsync(ct);
            return 0;
        }

        var categories = new Dictionary<string, MenuCategory>();

        for (var i = 0; i < SeedData.MenuCategories.Length; i++)
        {
            var name = SeedData.MenuCategories[i];

            var category = new MenuCategory
            {
                Name = name,
                NameNormalised = NameNormalizer.Normalize(name),
                SortOrder = i,
            };

            categories[name] = category;
            db.MenuCategories.Add(category);
        }

        var added = 0;
        var position = new Dictionary<string, int>();

        foreach (var seed in SeedData.MenuItems)
        {
            if (!categories.TryGetValue(seed.Category, out var category))
            {
                logger.LogWarning(
                    "Menu item {Name} names category {Category}, which is not seeded; skipped",
                    seed.Name, seed.Category);
                continue;
            }

            position.TryGetValue(seed.Category, out var order);
            position[seed.Category] = order + 1;

            var item = new MenuItem
            {
                Id = Guid.NewGuid(),
                Category = category,
                Name = seed.Name,
                NameNormalised = NameNormalizer.Normalize(seed.Name),
                Description = seed.Description,
                Price = seed.Price,
                MealPrice = seed.MealPrice,
                IsSurcharge = seed.IsSurcharge,
                SortOrder = order,
            };

            // The id is assigned here rather than by the database, because the
            // picture is written to a file named after it before the row is
            // saved.
            item.ImageFileName = await WriteMenuImageAsync(item.Id, seed.Image, ct);

            db.MenuItems.Add(item);
            added++;
        }

        return added;
    }

    /// <summary>
    /// Puts back any seeded photograph whose file is missing, for a menu that is
    /// already in the database.
    /// </summary>
    /// <remarks>
    /// The row and the file are separate things now, so they can be separated:
    /// by a restore that brought the database back without the images folder, or
    /// by the migration that moved the pictures out of the database in the first
    /// place. Either way the fix is to run <c>seed</c> again, and this is what
    /// makes that work.
    ///
    /// Only the items this seed knows about are touched, matched on category and
    /// folded name. A picture a supervisor uploaded is not ours to recreate, and
    /// an item that already has its file is left alone.
    /// </remarks>
    private async Task RestoreMenuImagesAsync(CancellationToken ct)
    {
        var known = SeedData.MenuItems
            .Where(i => i.Image is not null)
            .ToDictionary(
                i => (NameNormalizer.Normalize(i.Category), NameNormalizer.Normalize(i.Name)),
                i => i.Image!);

        var items = await db.MenuItems
            .Include(i => i.Category)
            .ToListAsync(ct);

        var restored = 0;

        foreach (var item in items)
        {
            if (images.Open(item.ImageFileName) is { } open)
            {
                open.Stream.Dispose();
                continue;
            }

            var key = (item.Category!.NameNormalised, item.NameNormalised);

            if (!known.TryGetValue(key, out var resource))
            {
                continue;
            }

            var fileName = await WriteMenuImageAsync(item.Id, resource, ct);

            if (fileName is not null)
            {
                item.ImageFileName = fileName;
                restored++;
            }
        }

        if (restored > 0)
        {
            logger.LogInformation("Restored {Count} menu pictures to disk", restored);
        }
    }

    /// <summary>
    /// Copies one embedded menu photograph out to the images folder, and returns
    /// the file name to record on the row.
    /// </summary>
    /// <remarks>
    /// The pictures ship inside the assembly so a fresh install needs the one
    /// DLL and no folder beside it; the seed then writes them where the server
    /// serves them from and the nightly backup copies them.
    ///
    /// A picture that cannot be written leaves the item without one rather than
    /// failing the seed: a menu without a photograph is usable, and losing the
    /// whole installation over one image would not be.
    /// </remarks>
    private async Task<string?> WriteMenuImageAsync(Guid itemId, string? resourceName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return null;
        }

        var assembly = typeof(DatabaseSeeder).Assembly;
        var resource = $"CallCenter.Server.Data.Seed.MenuImages.{resourceName}";

        await using var stream = assembly.GetManifestResourceStream(resource);

        if (stream is null)
        {
            logger.LogWarning("Menu picture {File} is not embedded in the assembly; skipped", resourceName);
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);

        return await images.SaveAsync(itemId, buffer.ToArray(), "image/png", ct);
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
