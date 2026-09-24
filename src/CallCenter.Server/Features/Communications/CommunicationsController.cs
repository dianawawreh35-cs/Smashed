using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
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
    /// <remarks>
    /// The filters are applied here rather than by the caller. Filtering a
    /// fetched page would answer "no calls match" whenever the match is older
    /// than the page, which reads as "this never happened".
    /// </remarks>
    [HttpGet("mine")]
    [Authorize(AuthPolicies.AgentOnly)]
    [ProducesResponseType<IReadOnlyList<CommunicationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CommunicationDto>>> Mine(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? q,
        [FromQuery] bool unclassified,
        [FromQuery] int limit,
        CancellationToken ct) =>
        Ok(await communications.ForAgentAsync(
            User.GetRequiredUserId(), from, to, q, unclassified, Clamp(limit), ct));

    /// <summary>
    /// The note on a missed, rejected or unanswered outbound call — why it went
    /// that way (A-41).
    /// </summary>
    /// <remarks>
    /// Those calls are never classified; this is what they take instead. The
    /// same edit window as a classification applies (A-42).
    /// </remarks>
    [HttpPut("{id:guid}/notes")]
    [ProducesResponseType<CommunicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CommunicationDto>> SaveNotes(
        Guid id, SaveCallNotesRequest request, CancellationToken ct)
    {
        var (call, failure) = await communications.SaveNotesAsync(
            id, request.Notes, User.GetRequiredUserId(),
            User.IsInRole(UserRoles.Supervisor), ct);

        return failure is not null ? Problem(failure.Value) : Ok(call);
    }

    /// <summary>
    /// The same note, keyed on the call rather than its server id (A-04).
    /// </summary>
    /// <remarks>
    /// For the Agent App's offline queue, as with a classification: a 404 means
    /// the call has not arrived yet, and the app keeps the note queued.
    /// </remarks>
    [HttpPut("by-call/notes")]
    [ProducesResponseType<CommunicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommunicationDto>> SaveNotesByCall(
        SaveCallNotesByCallRequest request, CancellationToken ct)
    {
        var (call, failure) = await communications.SaveNotesByCallAsync(
            request, User.GetRequiredUserId(), User.IsInRole(UserRoles.Supervisor), ct);

        return failure is not null ? Problem(failure.Value) : Ok(call);
    }

    /// <summary>
    /// Uploads the audio of a call the agent has just finished (A-31).
    /// </summary>
    /// <remarks>
    /// Multipart rather than JSON: the audio is megabytes, and base64 inside a
    /// JSON body would inflate it by a third and hold the whole thing in memory
    /// at both ends. The file streams to disk instead.
    ///
    /// The call is named by the phone system's reference and the extension —
    /// what the Agent App has in hand — rather than by an id it has never seen.
    ///
    /// <b>404 means "not yet", not "never".</b> The call and its recording
    /// travel as two items in one queue, and if the recording somehow arrives
    /// first the app is expected to hold it and try again.
    /// </remarks>
    [HttpPost("recordings")]
    [Authorize(AuthPolicies.AgentOnly)]
    [RequestSizeLimit(RecordingStore.MaxBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadRecording(
        [FromForm] string sipCallId,
        [FromForm] string extension,
        IFormFile? audio,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sipCallId)
            || string.IsNullOrWhiteSpace(extension)
            || audio is null || audio.Length == 0)
        {
            return Problem(CommunicationsService.Failure.UnknownValue);
        }

        await using var stream = audio.OpenReadStream();

        var (_, failure) = await communications.SaveRecordingAsync(
            sipCallId, extension, User.GetRequiredUserId(), stream, ct);

        return failure is not null ? Problem(failure.Value) : NoContent();
    }

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
            CommunicationsService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "call_not_found", "No such call."),
            CommunicationsService.Failure.NotesNotTaken =>
                (StatusCodes.Status409Conflict, "notes_not_taken",
                    "Only a missed, rejected or unanswered outbound call takes a note. An answered call is classified instead."),
            CommunicationsService.Failure.NotYours =>
                (StatusCodes.Status403Forbidden, "not_your_call",
                    "That call belongs to another agent."),
            CommunicationsService.Failure.EditWindowClosed =>
                (StatusCodes.Status403Forbidden, "edit_window_closed",
                    "This call can no longer be changed. Ask a supervisor."),
            _ => (StatusCodes.Status400BadRequest, "invalid_request", "The call could not be logged."),
        };

        var problem = new ProblemDetails
        {
            Title = "Call not saved",
            Detail = detail,
            Status = status,
        };

        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
