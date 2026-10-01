using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Pos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Pos;

/// <summary>
/// The POS customer lookup (A-67) on the settings screen: what the last run
/// did, and Check now. Supervisors only, like the rest of that screen.
/// </summary>
[ApiController]
[Route("api/pos/lookup")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class PosLookupController(PosCustomerSync sync) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PosLookupStatusDto>> Status(CancellationToken ct) =>
        Ok(await sync.StatusAsync(ct));

    /// <summary>
    /// Asks the POS now about every number that called in the last two days and
    /// still has no contact, then answers with what that run did.
    /// </summary>
    [HttpPost("run")]
    [ProducesResponseType<PosLookupStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PosLookupStatusDto>> Run(CancellationToken ct)
    {
        var status = await sync.StatusAsync(ct);
        if (!status.Enabled)
        {
            var problem = new ProblemDetails
            {
                Title = "The POS lookup is off",
                Detail = "The server has no POS token (POS_LOOKUP_TOKEN in .env).",
                Status = StatusCodes.Status409Conflict,
            };
            problem.Extensions["code"] = "pos_lookup_off";
            return Conflict(problem);
        }

        await sync.RunNowAsync(ct);
        return Ok(await sync.StatusAsync(ct));
    }
}
