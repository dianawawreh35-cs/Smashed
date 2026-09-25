using CallCenter.Server.Features.Auth;
using CallCenter.Server.Features.Classifications;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Applications;

/// <summary>
/// Messages: communications that did not arrive by phone (A-70, A-71).
/// </summary>
/// <remarks>
/// Under <c>api/communications</c>, because a message is a communication: the
/// supervisor searches them with <c>GET /api/communications/search?kind=App</c>
/// and opens one with <c>GET /api/communications/{id}</c>, and its
/// classification goes through <c>/api/classifications/{id}</c>, exactly as a
/// call's does. Only recording, editing and the agent's own list are new.
///
/// Recording is attributed to the caller's own account, from the token. A
/// supervisor may record one too — a message forwarded to them is still a
/// message — which is why this is not <see cref="AuthPolicies.AgentOnly"/>.
/// </remarks>
[ApiController]
[Route("api/communications/applications")]
[Authorize(AuthPolicies.SignedIn)]
public class ApplicationsController(ApplicationsService applications) : ControllerBase
{
    /// <summary>
    /// Records a message and, when the form was filled, classifies it in the
    /// same transaction (A-70). Not idempotent: the agent typed it, and nothing
    /// resends it.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CommunicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CommunicationDto>> Record(
        RecordApplicationRequest request, CancellationToken ct)
    {
        var outcome = await applications.RecordAsync(
            request, User.GetRequiredUserId(), IsSupervisor, ct);

        return outcome.Failure is not null ? Problem(outcome) : Ok(outcome.Message);
    }

    /// <summary>
    /// Changes a message's channel, customer or time (A-71): the agent's own
    /// within the edit window, a supervisor's any time (A-42).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<CommunicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommunicationDto>> Edit(
        Guid id, EditApplicationRequest request, CancellationToken ct)
    {
        var outcome = await applications.EditAsync(
            id, request, User.GetRequiredUserId(), IsSupervisor, ct);

        return outcome.Failure is not null ? Problem(outcome) : Ok(outcome.Message);
    }

    /// <summary>
    /// The signed-in agent's own messages, newest first (A-71). No agent id in
    /// the route: the token decides, as for the call log (A-52).
    /// </summary>
    [HttpGet("mine")]
    [Authorize(AuthPolicies.AgentOnly)]
    [ProducesResponseType<IReadOnlyList<CommunicationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CommunicationDto>>> Mine(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? q,
        [FromQuery] int limit,
        CancellationToken ct) =>
        Ok(await applications.ForAgentAsync(
            User.GetRequiredUserId(), from, to, q, limit is <= 0 or > 500 ? 100 : limit, ct));

    private bool IsSupervisor => User.IsInRole(UserRoles.Supervisor);

    private ObjectResult Problem(ApplicationsService.Outcome outcome)
    {
        var (status, code, detail) = outcome.Failure switch
        {
            ApplicationsService.Failure.UnknownChannel =>
                (StatusCodes.Status400BadRequest, "unknown_channel",
                    "No such channel, or the supervisor has hidden it."),
            ApplicationsService.Failure.PhoneChannel =>
                (StatusCodes.Status400BadRequest, "phone_channel",
                    "A phone conversation is a call. Calls are logged by the Agent App as they end."),
            ApplicationsService.Failure.NoCustomer =>
                (StatusCodes.Status400BadRequest, "no_customer",
                    "Give the customer's number, or pick a contact."),
            ApplicationsService.Failure.UnknownContact =>
                (StatusCodes.Status400BadRequest, "unknown_contact", "No such contact."),
            ApplicationsService.Failure.BadTime =>
                (StatusCodes.Status400BadRequest, "bad_time",
                    "The time must be today and not in the future. Ask a supervisor for another day."),
            ApplicationsService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "message_not_found", "No such message."),
            ApplicationsService.Failure.NotAnApplication =>
                (StatusCodes.Status409Conflict, "not_an_application",
                    "That is a call, not a message. Calls are not edited here."),
            ApplicationsService.Failure.NotYours =>
                (StatusCodes.Status403Forbidden, "not_your_call",
                    "That message belongs to another agent."),
            ApplicationsService.Failure.EditWindowClosed =>
                (StatusCodes.Status403Forbidden, "edit_window_closed",
                    "This message can no longer be changed. Ask a supervisor."),
            ApplicationsService.Failure.Classification => outcome.ClassificationFailure switch
            {
                ClassificationService.Failure.UnknownType =>
                    (StatusCodes.Status400BadRequest, "unknown_type",
                        "No such classification type, or it is no longer offered."),
                ClassificationService.Failure.UnknownBranch =>
                    (StatusCodes.Status400BadRequest, "unknown_branch", "No such branch."),
                ClassificationService.Failure.TypeNotOffered =>
                    (StatusCodes.Status400BadRequest, "type_not_offered",
                        "The Applications form does not offer that type."),
                _ => (StatusCodes.Status400BadRequest, "classification_refused",
                    "The message's classification was refused."),
            },
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The message could not be saved."),
        };

        var problem = new ProblemDetails { Title = "Message not saved", Detail = detail, Status = status };
        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
