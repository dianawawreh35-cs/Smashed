using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Menu;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Menu;

/// <summary>
/// The menu (A-66, S-59).
/// </summary>
/// <remarks>
/// Reading is open to any signed-in account — an agent mid-call is the main
/// reader. Changing it is the supervisor's, the same split as the delivery areas
/// and the VIP flags: an agent quotes a price, never sets one.
/// </remarks>
[ApiController]
[Route("api/menu")]
[Authorize(AuthPolicies.SignedIn)]
public class MenuController(MenuService menu) : ControllerBase
{
    /// <summary>
    /// Items matching what was typed (A-66). With no query, the whole menu in
    /// printed order.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MenuItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MenuItemDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool includeInactive,
        CancellationToken ct) =>
        Ok(await menu.SearchAsync(q, categoryId, includeInactive, ct));

    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<MenuCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MenuCategoryDto>>> Categories(
        [FromQuery] bool includeInactive, CancellationToken ct) =>
        Ok(await menu.CategoriesAsync(includeInactive, ct));

    /// <summary>
    /// One item's picture (A-66).
    /// </summary>
    /// <remarks>
    /// Cached for a day. The menu changes a few times a year and an agent's
    /// screen shows forty pictures at once; re-fetching them on every keystroke
    /// would make the search feel slow over a VPN. A changed picture takes up to
    /// a day to appear, which for a photograph of a burger is a fair trade.
    /// </remarks>
    [HttpGet("{id:guid}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Image(Guid id, CancellationToken ct)
    {
        var image = await menu.ImageAsync(id, ct);
        if (image is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=86400";

        // Streamed from disk rather than buffered: the bytes never pass through
        // the database, and ASP.NET closes the stream.
        return File(image.Value.Stream, image.Value.ContentType);
    }

    [HttpPost]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<MenuItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuItemDto>> Create(
        UpsertMenuItemRequest request, CancellationToken ct)
    {
        var (item, failure) = await menu.CreateAsync(request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(item);
    }

    [HttpPut("{id:guid}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<MenuItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuItemDto>> Update(
        Guid id, UpsertMenuItemRequest request, CancellationToken ct)
    {
        var (item, failure) = await menu.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(item);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await menu.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>
    /// Replaces an item's picture (S-59). An empty upload removes it.
    /// </summary>
    [HttpPut("{id:guid}/image")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [RequestSizeLimit(MenuService.MaxImageBytes + 4096)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetImage(Guid id, IFormFile? file, CancellationToken ct)
    {
        byte[]? bytes = null;
        string? contentType = null;

        if (file is { Length: > 0 })
        {
            if (file.Length > MenuService.MaxImageBytes)
            {
                return Problem(MenuService.Failure.BadImage);
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
            contentType = file.ContentType;
        }

        var failure = await menu.SetImageAsync(id, bytes, contentType, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : NoContent();
    }

    [HttpPost("categories")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<MenuCategoryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuCategoryDto>> CreateCategory(
        UpsertMenuCategoryRequest request, CancellationToken ct)
    {
        var (category, failure) = await menu.CreateCategoryAsync(request, ct);
        return failure is not null ? Problem(failure.Value) : Ok(category);
    }

    [HttpDelete("categories/{id:guid}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        var failure = await menu.DeleteCategoryAsync(id, ct);
        return failure is not null ? Problem(failure.Value) : NoContent();
    }

    private ObjectResult Problem(MenuService.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            MenuService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "item_not_found", "No such menu item."),
            MenuService.Failure.UnknownCategory =>
                (StatusCodes.Status400BadRequest, "unknown_category", "No such category."),
            MenuService.Failure.DuplicateName =>
                (StatusCodes.Status409Conflict, "duplicate_item",
                    "That category already has an item with this name."),
            MenuService.Failure.NoName =>
                (StatusCodes.Status400BadRequest, "no_name", "The item needs a name."),
            MenuService.Failure.BadImage =>
                (StatusCodes.Status400BadRequest, "bad_image",
                    "The picture must be a PNG, JPEG or WebP under 2 MB."),
            MenuService.Failure.CategoryNotEmpty =>
                (StatusCodes.Status409Conflict, "category_not_empty",
                    "Move or delete this category's items first."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "It could not be saved."),
        };

        var problem = new ProblemDetails { Title = "Menu not saved", Detail = detail, Status = status };
        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
