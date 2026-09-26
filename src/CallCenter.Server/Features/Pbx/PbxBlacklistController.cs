using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Pbx;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// The PBX blacklist (S-46): the extension the server dials <c>*30</c> and
/// <c>*31</c> from, and how far the PBX has caught up with the Blocked flags.
/// Supervisors only - the settings include the extension's password.
/// </summary>
/// <remarks>
/// There is no "block this number" call here. Blocking is the Blocked flag on
/// the contact (S-45); the PBX follows it by itself within a minute or so.
/// </remarks>
[ApiController]
[Route("api/pbx/blacklist")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class PbxBlacklistController(PbxBlacklistSync sync) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PbxBlacklistDto>> Status(CancellationToken ct) =>
        Ok(await sync.StatusAsync(ct));

    /// <summary>Saves the extension and its password. Rejected fields come back one by one, and nothing is saved.</summary>
    [HttpPut]
    [ProducesResponseType<PbxBlacklistDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PbxBlacklistDto>> Save(UpdatePbxBlacklistRequest request, CancellationToken ct)
    {
        var problems = await sync.SaveAsync(request, User.GetRequiredUserId(), ct);
        if (problems.Count > 0)
        {
            var problem = new ProblemDetails
            {
                Title = "Settings not changed",
                Detail = "One or more values were rejected; nothing was saved.",
                Status = StatusCodes.Status400BadRequest,
            };
            problem.Extensions["code"] = "invalid_settings";
            problem.Extensions["problems"] = problems;
            return BadRequest(problem);
        }

        return Ok(await sync.StatusAsync(ct));
    }

    /// <summary>Tries every failed number again at the next run, without waiting out the retry delay.</summary>
    [HttpPost("retry")]
    public async Task<ActionResult<PbxBlacklistDto>> Retry(CancellationToken ct)
    {
        await sync.RetryNowAsync(ct);
        return Ok(await sync.StatusAsync(ct));
    }
}
