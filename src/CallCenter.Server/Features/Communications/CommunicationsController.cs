using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Communications;

/// <summary>
/// The record of every call (A-14).
/// </summary>
/// <remarks>
/// Logging a call is <see cref="AuthPolicies.AgentOnly"/>: the call is always
/// attributed to the caller's own account, taken from the token rather than the
/// body, so one agent cannot file a call against another. A supervisor has no
/// extension and takes no calls, which is why they are not allowed here either —
/// they read the same data through the reports.
/// </remarks>
[ApiController]
[Route("api/communications")]
[Authorize(AuthPolicies.SignedIn)]
public class CommunicationsController(CommunicationsService communications) : ControllerBase
{
    /// <summary>
    /// Records one call as it ends (A-14). Safe to call again with the same
    /// call: the second report updates the first rather than duplicating it,
    /// which is what lets the Agent App resend from its offline queue.
    /// </summary>
    [HttpPost("calls")]
    [Authorize(AuthPolicies.AgentOnly)]
    [ProducesResponseType<CommunicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CommunicationDto>> LogCall(
        LogCallRequest request, CancellationToken ct)
    {
        var (call, failure) =
            await communications.LogCallAsync(request, User.GetRequiredUserId(), ct);

        return failure is not null ? Problem(failure.Value) : Ok(call);
    }

    /// <summary>
    /// The signed-in agent's own calls, newest first (A-50). An agent sees only
    /// their own, which is why there is no id in the route — the token decides.
    /// </summary>
    [HttpGet("mine")]
    [Authorize(AuthPolicies.AgentOnly)]
    [ProducesResponseType<IReadOnlyList<CommunicationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CommunicationDto>>> Mine(
        [FromQuery] int limit, CancellationToken ct) =>
        Ok(await communications.ForAgentAsync(User.GetRequiredUserId(), Clamp(limit), ct));

    /// <summary>
    /// One contact's history of calls, newest first — the panel in A-62.
    /// </summary>
    /// <remarks>
    /// Open to any signed-in account: A-62 says every agent sees a contact's
    /// full history from all agents. Recordings are the part that is restricted,
    /// and they are not served from here.
    /// </remarks>
    [HttpGet("by-contact/{contactId:guid}")]
    [ProducesResponseType<IReadOnlyList<CommunicationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CommunicationDto>>> ByContact(
        Guid contactId, [FromQuery] int limit, CancellationToken ct) =>
        Ok(await communications.ForContactAsync(contactId, Clamp(limit), ct));

    /// <summary>
    /// Keeps a page size sane whatever the caller asks for. Zero means "not
    /// specified" rather than "none", because an omitted query parameter binds
    /// to zero and returning nothing would be a confusing answer.
    /// </summary>
    private static int Clamp(int limit) => limit is <= 0 or > 500 ? 100 : limit;

    private ObjectResult Problem(CommunicationsService.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            CommunicationsService.Failure.UnknownValue =>
                (StatusCodes.Status400BadRequest, "unknown_value",
                    "The status or direction is not one this system knows."),
            CommunicationsService.Failure.NoPhoneChannel =>
                (StatusCodes.Status500InternalServerError, "not_seeded",
                    "The Phone channel is missing. The database has not been seeded."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The call could not be logged."),
        };

        var problem = new ProblemDetails
        {
            Title = "Call not logged",
            Detail = detail,
            Status = status,
        };

        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
