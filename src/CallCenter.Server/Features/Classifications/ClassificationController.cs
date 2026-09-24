using System.Security.Claims;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Classifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Classifications;

/// <summary>
/// What each call was about (A-40 to A-43, S-40).
/// </summary>
/// <remarks>
/// The split is the same as everywhere else here: agents write what happened on
/// their own calls, supervisors decide what the form asks and can correct
/// anything. Reading the form is open to any signed-in account, because an agent
/// needs it to classify at all.
/// </remarks>
[ApiController]
[Route("api/classifications")]
[Authorize(AuthPolicies.SignedIn)]
public class ClassificationController(
    ClassificationService classifications,
    ClassificationTypeService types) : ControllerBase
{
    /// <summary>
    /// The form to draw, with its types and branches (A-40).
    /// </summary>
    /// <remarks>
    /// Fetched at sign-in and kept. The version in the response is sent back
    /// when classifying, so a form filled in as a change was published is stored
    /// against the questions the agent actually answered.
    /// </remarks>
    [HttpGet("form")]
    [ProducesResponseType<ClassificationFormDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClassificationFormDto>> Form(CancellationToken ct) =>
        Ok(await classifications.FormAsync(ct));

    /// <summary>Publishes a new version of the form (S-40).</summary>
    [HttpPut("form")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<ClassificationFormDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClassificationFormDto>> PublishForm(
        PublishFormRequest request, CancellationToken ct)
    {
        var (form, failure) = await classifications.PublishFormAsync(
            request.Definition, User.GetRequiredUserId(), ct);

        return failure is not null ? Problem(failure.Value) : Ok(form);
    }

    // ---- types -------------------------------------------------------------

    [HttpGet("types")]
    [ProducesResponseType<IReadOnlyList<ClassificationTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClassificationTypeDto>>> Types(
        [FromQuery] bool includeInactive, CancellationToken ct) =>
        Ok(await types.ListAsync(includeInactive, ct));

    [HttpPost("types")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<ClassificationTypeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClassificationTypeDto>> CreateType(
        UpsertClassificationTypeRequest request, CancellationToken ct)
    {
        var (type, failure) = await types.CreateAsync(request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(type);
    }

    [HttpPut("types/{id:guid}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<ClassificationTypeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClassificationTypeDto>> UpdateType(
        Guid id, UpsertClassificationTypeRequest request, CancellationToken ct)
    {
        var (type, failure) = await types.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return failure is not null ? Problem(failure.Value) : Ok(type);
    }

    [HttpDelete("types/{id:guid}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteType(Guid id, CancellationToken ct)
    {
        var failure = await types.DeleteAsync(id, ct);
        return failure is not null ? Problem(failure.Value) : NoContent();
    }

    // ---- classifying -------------------------------------------------------

    [HttpGet("{communicationId:guid}")]
    [ProducesResponseType<ClassificationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClassificationDto>> Get(Guid communicationId, CancellationToken ct)
    {
        var found = await classifications.GetAsync(
            communicationId, User.GetRequiredUserId(), IsSupervisor, ct);

        return found is null ? NotFound() : Ok(found);
    }

    /// <summary>Classifies a call, or edits it (A-40, A-42).</summary>
    [HttpPut("{communicationId:guid}")]
    [ProducesResponseType<ClassificationDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClassificationDto>> Save(
        Guid communicationId, SaveClassificationRequest request, CancellationToken ct)
    {
        var (saved, failure) = await classifications.SaveAsync(
            communicationId, request, User.GetRequiredUserId(), IsSupervisor, ct);

        return failure is not null ? Problem(failure.Value) : Ok(saved);
    }

    /// <summary>
    /// Classifies a call the server has not been told about yet (A-04).
    /// </summary>
    /// <remarks>
    /// For the Agent App's offline queue. A 404 here is not an error the agent
    /// should ever see: it means the call has not arrived yet, and the app keeps
    /// the classification queued and tries again.
    /// </remarks>
    [HttpPut("by-call")]
    [ProducesResponseType<ClassificationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClassificationDto>> SaveByCall(
        SaveClassificationByCallRequest request, CancellationToken ct)
    {
        var (saved, failure) = await classifications.SaveByCallAsync(
            request, User.GetRequiredUserId(), IsSupervisor, ct);

        return failure is not null ? Problem(failure.Value) : Ok(saved);
    }

    /// <summary>Who changed this classification, when, and what it said before (A-43).</summary>
    [HttpGet("{communicationId:guid}/history")]
    [ProducesResponseType<IReadOnlyList<ClassificationHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClassificationHistoryDto>>> History(
        Guid communicationId, CancellationToken ct) =>
        Ok(await classifications.HistoryAsync(communicationId, ct));

    private bool IsSupervisor => User.IsInRole(UserRoles.Supervisor);

    private ObjectResult Problem(ClassificationService.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            ClassificationService.Failure.CommunicationNotFound =>
                (StatusCodes.Status404NotFound, "call_not_found",
                    "That call is not on the server yet."),
            ClassificationService.Failure.NotAnswered =>
                (StatusCodes.Status409Conflict, "not_answered",
                    "Only an answered call is classified. A missed, rejected or unanswered call takes a note instead."),
            ClassificationService.Failure.UnknownType =>
                (StatusCodes.Status400BadRequest, "unknown_type",
                    "No such classification type, or it is no longer offered."),
            ClassificationService.Failure.UnknownBranch =>
                (StatusCodes.Status400BadRequest, "unknown_branch", "No such branch."),
            ClassificationService.Failure.EditWindowClosed =>
                (StatusCodes.Status403Forbidden, "edit_window_closed",
                    "This call can no longer be changed. Ask a supervisor."),
            ClassificationService.Failure.NotYours =>
                (StatusCodes.Status403Forbidden, "not_your_call",
                    "That call belongs to another agent."),
            ClassificationService.Failure.TypeInUse =>
                (StatusCodes.Status409Conflict, "type_in_use",
                    "Calls already use this type. Hide it instead of deleting it."),
            ClassificationService.Failure.BadForm =>
                (StatusCodes.Status400BadRequest, "bad_form",
                    "That form cannot be drawn. Every field needs a key and a kind, and there must be a type field."),
            ClassificationService.Failure.BadName =>
                (StatusCodes.Status400BadRequest, "bad_name",
                    "The type needs a name in both languages, and it must not already exist."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "It could not be saved."),
        };

        var problem = new ProblemDetails
        {
            Title = "Classification not saved",
            Detail = detail,
            Status = status,
        };
        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
