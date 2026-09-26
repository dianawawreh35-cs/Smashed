using CallCenter.Server.Features.Auth;
using CallCenter.Server.Features.Reports;
using CallCenter.Shared.Contracts.Pbx;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// The abandoned-call import (S-55): its settings, how the last check went, and
/// a fetch on demand. Supervisors only - the settings include the PBX login.
/// </summary>
[ApiController]
[Route("api/pbx/abandoned-import")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class PbxController(AbandonedCallImport import) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AbandonedImportDto>> Status(CancellationToken ct) =>
        Ok(await import.StatusAsync(ct));

    /// <summary>Saves the settings. Rejected fields come back one by one, and nothing is saved.</summary>
    [HttpPut]
    [ProducesResponseType<AbandonedImportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AbandonedImportDto>> Save(UpdateAbandonedImportRequest request, CancellationToken ct)
    {
        var problems = await import.SaveAsync(request, User.GetRequiredUserId(), ct);
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

        return Ok(await import.StatusAsync(ct));
    }

    /// <summary>
    /// Downloads the period from the PBX now. <c>from</c> and <c>to</c> are the
    /// reports' own (S-07): instants, <c>to</c> exclusive. Without them, today.
    /// A PBX that cannot be reached or refuses the login is an answer, not an
    /// error: <c>ok</c> is false and <c>error</c> says why.
    /// </summary>
    [HttpPost("fetch")]
    public async Task<ActionResult<AbandonedFetchResultDto>> Fetch(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(ReportScope.Local(DateTimeOffset.UtcNow));
        var first = from is { } f ? DateOnly.FromDateTime(ReportScope.Local(f)) : today;
        // The last instant before `to`, since `to` is the start of the day after.
        var last = to is { } t ? DateOnly.FromDateTime(ReportScope.Local(t.AddTicks(-1))) : today;

        return Ok(await import.FetchAsync(first, last, ct));
    }
}
