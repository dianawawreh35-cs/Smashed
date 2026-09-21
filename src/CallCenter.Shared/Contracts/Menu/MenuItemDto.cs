using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Menu;

/// <summary>A group of menu items (A-66).</summary>
public record MenuCategoryDto(Guid Id, string Name, int SortOrder, bool IsActive, int ItemCount);

/// <summary>
/// One thing on the menu (A-66).
/// </summary>
/// <remarks>
/// The picture is <b>not</b> in here. A list of 44 items carrying 1.2 MB of
/// images would be sent in full every time an agent typed a letter; the client
/// asks for each image separately at <c>/api/menu/{id}/image</c>, and the
/// browser and the Agent App cache them.
/// </remarks>
/// <param name="Price">
/// The item alone, or the sandwich where there is also a meal. Null means the
/// menu prints no price — which is different from zero, the price of a free
/// extra.
/// </param>
/// <param name="MealPrice">With fries and a drink. Null when there is no meal.</param>
/// <param name="IsSurcharge">
/// <see cref="Price"/> is added to another item rather than being a price of its
/// own — the menu writes these as "+2".
/// </param>
/// <param name="HasImage">Whether asking for the image would return one.</param>
public record MenuItemDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string? Description,
    decimal? Price,
    decimal? MealPrice,
    bool IsSurcharge,
    bool HasImage,
    bool IsActive);

/// <summary>Creates or updates one item (S-59). The picture is sent separately.</summary>
public record UpsertMenuItemRequest(
    [Required] Guid CategoryId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    [Range(0, 100000)] decimal? Price,
    [Range(0, 100000)] decimal? MealPrice,
    bool IsSurcharge = false,
    bool IsActive = true);

/// <summary>Creates or renames a category (S-59).</summary>
public record UpsertMenuCategoryRequest(
    [Required, MaxLength(200)] string Name,
    int SortOrder = 0,
    bool IsActive = true);
