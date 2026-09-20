using CallCenter.Server.Data;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Delivery;

/// <summary>
/// The restaurant's branches, for anything that has to name one.
/// </summary>
/// <remarks>
/// Read-only, and deliberately so. S-41 covers creating, renaming and disabling
/// branches and is not built; this is the list every other screen needs to show
/// a dropdown, which was blocking the delivery areas (S-58). Adding the
/// management side later replaces this controller rather than working around it.
///
/// Open to any signed-in account: a branch name is not sensitive, and an agent's
/// delivery lookup shows one on every row (A-65).
/// </remarks>
[ApiController]
[Route("api/branches")]
[Authorize(AuthPolicies.SignedIn)]
public class BranchesController(CallCenterDbContext db) : ControllerBase
{
    /// <summary>Every branch, in the supervisor's order.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BranchDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BranchDto>>> List(
        [FromQuery] bool includeInactive, CancellationToken ct)
    {
        var branches = db.Branches.AsNoTracking();

        if (!includeInactive)
        {
            branches = branches.Where(b => b.IsActive);
        }

        return Ok(await branches
            .OrderBy(b => b.SortOrder)
            .Select(b => new BranchDto(b.Id, b.Name, b.IsActive))
            .ToListAsync(ct));
    }
}
