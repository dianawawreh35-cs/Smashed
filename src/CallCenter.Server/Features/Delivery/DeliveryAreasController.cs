using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Delivery;

/// <summary>
/// Delivery areas: which branch covers a place and what it costs (A-65, S-58).
/// </summary>
/// <remarks>
/// Searching is open to any signed-in account — an agent mid-call is the main
/// reader (A-65). Changing the list is the supervisor's (S-58), the same split
/// as the VIP and Blocked flags and for the same reason: an agent must be able
/// to quote a price, never to change one.
/// </remarks>
[ApiController]
[Route("api/delivery-areas")]
[Authorize(AuthPolicies.SignedIn)]
public class DeliveryAreasController(DeliveryAreasService areas) : ControllerBase
{
    /// <summary>
    /// Areas matching what was typed (A-65). A partial name is enough.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DeliveryAreaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DeliveryAreaDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] Guid? branchId,
        [FromQuery] bool includeInactive,
        CancellationToken ct) =>
        Ok(await areas.SearchAsync(q, branchId, includeInactive, ct));

    [HttpPost]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<DeliveryAreaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeliveryAreaDto>> Create(
        UpsertDeliveryAreaRequest request, CancellationToken ct)
    {
        var (area, failure) = await areas.CreateAsync(request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(area);
    }

    [HttpPut("{id:guid}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<DeliveryAreaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeliveryAreaDto>> Update(
        Guid id, UpsertDeliveryAreaRequest request, CancellationToken ct)
    {
        var (area, failure) = await areas.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(area);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await areas.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>
    /// Adds a whole branch's list at once, pasted from a spreadsheet (S-58).
    /// </summary>
    /// <remarks>
    /// Answers with what it did and every line it could not use, so the
    /// supervisor does not have to find the rejected ones themselves.
    /// </remarks>
    [HttpPost("import")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<ImportDeliveryAreasResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImportDeliveryAreasResult>> Import(
        ImportDeliveryAreasRequest request, CancellationToken ct)
    {
        var (result, failure) = await areas.ImportAsync(request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(result);
    }

    private ObjectResult Problem(DeliveryAreasService.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            DeliveryAreasService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "area_not_found", "No such delivery area."),
            DeliveryAreasService.Failure.UnknownBranch =>
                (StatusCodes.Status400BadRequest, "unknown_branch", "No such branch."),
            DeliveryAreasService.Failure.DuplicateName =>
                (StatusCodes.Status409Conflict, "duplicate_area",
                    "Another area already has this name."),
            DeliveryAreasService.Failure.NoName =>
                (StatusCodes.Status400BadRequest, "no_name", "The area needs a name."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The area could not be saved."),
        };

        var problem = new ProblemDetails { Title = "Delivery area not saved", Detail = detail, Status = status };
        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
