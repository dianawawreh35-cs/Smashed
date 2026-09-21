using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared.Contracts.Menu;
using CallCenter.Shared.Text;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Menu;

/// <summary>
/// The menu: what is on it, what is in it, what it costs (A-66, S-59).
/// </summary>
/// <remarks>
/// Agents read it mid-call — "what comes in the Overdose?", "how much is a
/// double as a meal?" — which today means a printed sheet beside the laptop that
/// goes stale the moment a price changes. Supervisors maintain it.
///
/// Names are folded by <see cref="NameNormalizer"/>, as contacts and delivery
/// areas are. It matters more here than anywhere: these are English words
/// written in Arabic — سماشد, ماشروم, كرسبي — and nobody spells them the
/// same way twice.
/// </remarks>
public class MenuService(
    CallCenterDbContext db,
    MenuImageStore images,
    ILogger<MenuService> logger)
{
    /// <summary>The largest picture accepted, in bytes. Menu photographs, not posters.</summary>
    public const int MaxImageBytes = 2 * 1024 * 1024;

    public enum Failure
    {
        NotFound,
        UnknownCategory,
        DuplicateName,
        NoName,

        /// <summary>The picture is too large, or not a picture.</summary>
        BadImage,

        /// <summary>
        /// Another category already has this name. Separate from
        /// <see cref="DuplicateName"/> so the screen can say "category" rather
        /// than telling a supervisor renaming a category about an item.
        /// </summary>
        DuplicateCategoryName,

        /// <summary>No such category.</summary>
        CategoryNotFound,

        /// <summary>A category still holding items cannot be removed.</summary>
        CategoryNotEmpty,
    }

    /// <summary>
    /// Items matching what was typed, newest categories first (A-66).
    /// </summary>
    /// <remarks>
    /// With no search text this returns the whole menu in printed order, so the
    /// screen is useful before anything is typed — an agent who cannot spell
    /// "ماشروم" can scroll to it.
    ///
    /// The description is searched as well as the name: "what has mushrooms in
    /// it?" is a question agents are asked, and the answer is in the contents.
    /// </remarks>
    public async Task<IReadOnlyList<MenuItemDto>> SearchAsync(
        string? query, Guid? categoryId = null, bool includeInactive = false,
        CancellationToken ct = default)
    {
        var items = db.MenuItems.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            items = items.Where(i => i.IsActive && i.Category.IsActive);
        }

        if (categoryId is { } category)
        {
            items = items.Where(i => i.CategoryId == category);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalised = NameNormalizer.Normalize(query);
            var raw = query.Trim();

            if (normalised.Length > 0)
            {
                items = items.Where(i =>
                    EF.Functions.ILike(i.NameNormalised, $"%{normalised}%")
                    || (i.Description != null && EF.Functions.ILike(i.Description, $"%{raw}%")));
            }
        }

        return await items
            .OrderBy(i => i.Category.SortOrder)
            .ThenBy(i => i.SortOrder)
            .Select(i => new MenuItemDto(
                i.Id,
                i.CategoryId,
                i.Category.Name,
                i.Name,
                i.Description,
                i.Price,
                i.MealPrice,
                i.IsSurcharge,
                i.ImageFileName != null,
                i.IsActive))
            .ToListAsync(ct);
    }

    /// <summary>Every category, in menu order, with how many items each holds.</summary>
    public async Task<IReadOnlyList<MenuCategoryDto>> CategoriesAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        var categories = db.MenuCategories.AsNoTracking();

        if (!includeInactive)
        {
            categories = categories.Where(c => c.IsActive);
        }

        return await categories
            .OrderBy(c => c.SortOrder)
            .Select(c => new MenuCategoryDto(
                c.Id, c.Name, c.SortOrder, c.IsActive, c.Items.Count(i => i.IsActive)))
            .ToListAsync(ct);
    }

    /// <summary>
    /// One item's picture, opened for streaming, or null when it has none.
    /// </summary>
    /// <remarks>
    /// One small query for the file name, then the file itself — the bytes never
    /// pass through the database. A row whose file is missing answers null and
    /// the endpoint gives a 404, which is what a restore that brought the
    /// database back without the folder would produce.
    /// </remarks>
    public async Task<(Stream Stream, string ContentType)?> ImageAsync(
        Guid id, CancellationToken ct = default)
    {
        var fileName = await db.MenuItems
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => i.ImageFileName)
            .FirstOrDefaultAsync(ct);

        return images.Open(fileName);
    }

    public async Task<(MenuItemDto? Item, Failure? Failure)> CreateAsync(
        UpsertMenuItemRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var normalised = NameNormalizer.Normalize(name);

        if (normalised.Length == 0)
        {
            return (null, Failure.NoName);
        }

        if (!await db.MenuCategories.AnyAsync(c => c.Id == request.CategoryId, ct))
        {
            return (null, Failure.UnknownCategory);
        }

        if (await db.MenuItems.AnyAsync(
                i => i.CategoryId == request.CategoryId && i.NameNormalised == normalised, ct))
        {
            return (null, Failure.DuplicateName);
        }

        // Appended to its category rather than dropped at the top: the order
        // follows the printed menu, and a new item belongs at the end of its
        // section until somebody says otherwise.
        var next = await db.MenuItems
            .Where(i => i.CategoryId == request.CategoryId)
            .Select(i => (int?)i.SortOrder)
            .MaxAsync(ct) ?? 0;

        var item = new MenuItem
        {
            CategoryId = request.CategoryId,
            Name = name,
            NameNormalised = normalised,
            Description = Trimmed(request.Description),
            Price = request.Price,
            MealPrice = request.MealPrice,
            IsSurcharge = request.IsSurcharge,
            IsActive = request.IsActive,
            SortOrder = next + 1,
            CreatedBy = actingUserId,
            UpdatedBy = actingUserId,
        };

        db.MenuItems.Add(item);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Menu item {Name} added", name);

        return (await GetAsync(item.Id, ct), null);
    }

    public async Task<(MenuItemDto? Item, Failure? Failure)> UpdateAsync(
        Guid id, UpsertMenuItemRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return (null, Failure.NotFound);
        }

        var name = request.Name.Trim();
        var normalised = NameNormalizer.Normalize(name);

        if (normalised.Length == 0)
        {
            return (null, Failure.NoName);
        }

        if (!await db.MenuCategories.AnyAsync(c => c.Id == request.CategoryId, ct))
        {
            return (null, Failure.UnknownCategory);
        }

        if (await db.MenuItems.AnyAsync(
                i => i.CategoryId == request.CategoryId
                     && i.NameNormalised == normalised
                     && i.Id != id, ct))
        {
            return (null, Failure.DuplicateName);
        }

        item.CategoryId = request.CategoryId;
        item.Name = name;
        item.NameNormalised = normalised;
        item.Description = Trimmed(request.Description);
        item.Price = request.Price;
        item.MealPrice = request.MealPrice;
        item.IsSurcharge = request.IsSurcharge;
        item.IsActive = request.IsActive;
        item.UpdatedBy = actingUserId;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return (await GetAsync(id, ct), null);
    }

    /// <summary>
    /// Replaces an item's picture, or removes it when <paramref name="bytes"/>
    /// is null (S-59).
    /// </summary>
    public async Task<Failure?> SetImageAsync(
        Guid id, byte[]? bytes, string? contentType, Guid actingUserId,
        CancellationToken ct = default)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return Failure.NotFound;
        }

        if (bytes is null)
        {
            images.Delete(id);
            item.ImageFileName = null;
        }
        else
        {
            if (bytes.Length == 0
                || bytes.Length > MaxImageBytes
                || !MenuImageStore.IsAllowed(contentType))
            {
                return Failure.BadImage;
            }

            var fileName = await images.SaveAsync(id, bytes, contentType!, ct);

            if (fileName is null)
            {
                return Failure.BadImage;
            }

            item.ImageFileName = fileName;
        }

        item.UpdatedBy = actingUserId;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return null;
    }

    /// <summary>
    /// Removes an item and its picture (S-59).
    /// </summary>
    /// <remarks>
    /// The row goes first. A file left behind because the delete failed wastes a
    /// few kilobytes; a row left behind pointing at a deleted file would show a
    /// broken picture to every agent.
    /// </remarks>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var removed = await db.MenuItems.Where(i => i.Id == id).ExecuteDeleteAsync(ct) > 0;

        if (removed)
        {
            images.Delete(id);
        }

        return removed;
    }

    public async Task<MenuItemDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.MenuItems
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new MenuItemDto(
                i.Id, i.CategoryId, i.Category.Name, i.Name, i.Description,
                i.Price, i.MealPrice, i.IsSurcharge, i.ImageFileName != null, i.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<(MenuCategoryDto? Category, Failure? Failure)> CreateCategoryAsync(
        UpsertMenuCategoryRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var normalised = NameNormalizer.Normalize(name);

        if (normalised.Length == 0)
        {
            return (null, Failure.NoName);
        }

        if (await db.MenuCategories.AnyAsync(c => c.NameNormalised == normalised, ct))
        {
            return (null, Failure.DuplicateCategoryName);
        }

        var category = new MenuCategory
        {
            Name = name,
            NameNormalised = normalised,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
        };

        db.MenuCategories.Add(category);
        await db.SaveChangesAsync(ct);

        return (new MenuCategoryDto(category.Id, category.Name, category.SortOrder, category.IsActive, 0), null);
    }

    /// <summary>
    /// Renames a category, moves it in the printed order, or hides it (S-59).
    /// </summary>
    /// <remarks>
    /// Hiding rather than removing is the usual case: a category that held items
    /// cannot be deleted, and "we do not sell these in Ramadan" is a thing a
    /// supervisor needs to be able to say without losing the items.
    ///
    /// Hiding a category does not hide its items. An agent searching for
    /// "كرسبي" should still find it; what the category controls is whether it
    /// appears as a heading in the list read in printed order.
    /// </remarks>
    public async Task<(MenuCategoryDto? Category, Failure? Failure)> UpdateCategoryAsync(
        Guid id, UpsertMenuCategoryRequest request, CancellationToken ct = default)
    {
        var category = await db.MenuCategories.FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null)
        {
            return (null, Failure.CategoryNotFound);
        }

        var name = request.Name.Trim();
        var normalised = NameNormalizer.Normalize(name);

        if (normalised.Length == 0)
        {
            return (null, Failure.NoName);
        }

        // Folded, so "كولا" and "كولا " are the same name. The category being
        // renamed is excluded, or saving it unchanged would collide with itself.
        if (await db.MenuCategories.AnyAsync(
                c => c.Id != id && c.NameNormalised == normalised, ct))
        {
            return (null, Failure.DuplicateCategoryName);
        }

        category.Name = name;
        category.NameNormalised = normalised;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        var itemCount = await db.MenuItems.CountAsync(i => i.CategoryId == id, ct);

        return (new MenuCategoryDto(
            category.Id, category.Name, category.SortOrder, category.IsActive, itemCount), null);
    }

    /// <summary>
    /// Removes a category (S-59). Refused while it still holds items.
    /// </summary>
    /// <remarks>
    /// The foreign key would refuse it anyway, with an error nobody can read.
    /// Checking first lets the screen say "move or delete its items" instead.
    /// </remarks>
    public async Task<Failure?> DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        if (await db.MenuItems.AnyAsync(i => i.CategoryId == id, ct))
        {
            return Failure.CategoryNotEmpty;
        }

        var removed = await db.MenuCategories.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        return removed > 0 ? null : Failure.CategoryNotFound;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
