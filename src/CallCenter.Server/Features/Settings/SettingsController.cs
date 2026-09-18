using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Settings;

/// <summary>
/// System settings (S-47). Supervisors only — these change how the whole system
/// behaves, including which PBX the Agent Apps register to.
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SettingDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SettingDto>>> List(CancellationToken ct) =>
        Ok(await settings.ListAsync(ct));

    /// <summary>
    /// Applies a batch of changes. Rejected values come back per setting, so the
    /// screen can mark the offending field rather than showing one vague message.
    /// </summary>
    [HttpPut]
    [ProducesResponseType<IReadOnlyList<SettingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<SettingDto>>> Update(
        UpdateSettingsRequest request, CancellationToken ct)
    {
        var problems = await settings.UpdateAsync(request.Values, User.GetRequiredUserId(), ct);

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

        return Ok(await settings.ListAsync(ct));
    }
}
