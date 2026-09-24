using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Communications;

/// <summary>
/// Hearing a recording back (A-51, S-04) and how much room they take (S-43).
/// </summary>
/// <remarks>
/// Recordings are the one part of a call that is not open to every signed-in
/// account, which is why they are served from here rather than from
/// <see cref="CommunicationsController"/>:
///
/// <list type="bullet">
/// <item>a <b>supervisor</b> may play and download any agent's recording (S-04);</item>
/// <item>an <b>agent</b> may play their own call's recording (A-51) and no one
/// else's (A-52);</item>
/// <item>nobody reaches the folder itself — it is served through the API and
/// never as a share (N-05).</item>
/// </list>
///
/// <b>Download is supervisors only</b>, while playing is not. S-04 grants the
/// supervisor "play and download"; A-51 grants the agent "play the recording
/// (play/pause/seek)" and no more. Separating the two costs one endpoint, so
/// the narrower reading is the one built.
///
/// <b>Everything here streams.</b> The bytes go from the disk to the response
/// without being buffered, and range requests are answered, so a player can
/// seek into an hour-long call without fetching the hour.
/// </remarks>
[ApiController]
[Route("api/recordings")]
[Authorize(AuthPolicies.SignedIn)]
public class RecordingsController(
    CommunicationsService communications,
    RecordingRetention retention) : ControllerBase
{
    /// <summary>
    /// Plays the recording of one call: a supervisor any call (S-04), an agent
    /// their own (A-51).
    /// </summary>
    /// <remarks>
    /// Seeking works because the response supports range requests; the file is
    /// on disk and seekable, so that came free.
    ///
    /// <b>404 <c>recording_expired</c> is not <c>recording_not_found</c>.</b>
    /// The first means the call was recorded and retention has since removed
    /// the audio (A-33); the second means no recording was ever attached. The
    /// screen must be able to say "expired" rather than leave a supervisor
    /// thinking the call was never recorded.
    /// </remarks>
    [HttpGet("{communicationId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Play(Guid communicationId, CancellationToken ct) =>
        ServeAsync(communicationId, asDownload: false, ct);

    /// <summary>
    /// The same audio as a file to keep (S-04). <b>Supervisors only</b> — see
    /// the note on this controller.
    /// </summary>
    [HttpGet("{communicationId:guid}/download")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Download(Guid communicationId, CancellationToken ct) =>
        ServeAsync(communicationId, asDownload: true, ct);

    /// <summary>
    /// How much disk the recordings occupy and how many are kept (S-43).
    /// </summary>
    /// <remarks>
    /// The other half of the retention requirement, and what tells the client
    /// whether 90 days is affordable: the estimate was about 50 GB for four
    /// agents and this is the measured number. Supervisors only, for the same
    /// reason the settings screen is.
    /// </remarks>
    [HttpGet("storage")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<RecordingStorageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RecordingStorageDto>> Storage(CancellationToken ct) =>
        Ok(await retention.UsageAsync(ct));

    private async Task<IActionResult> ServeAsync(Guid communicationId, bool asDownload, CancellationToken ct)
    {
        var (file, failure) = await communications.OpenRecordingAsync(
            communicationId, User.GetRequiredUserId(), User.IsInRole(UserRoles.Supervisor), ct);

        if (failure is not null)
        {
            return Problem(failure.Value);
        }

        // enableRangeProcessing: the player asks for the part it is about to
        // play rather than the whole call. The framework does the arithmetic
        // and the stream is disposed with the response either way.
        return File(
            file!.Content,
            file.ContentType,
            asDownload ? file.FileName : null,
            enableRangeProcessing: true);
    }

    private ObjectResult Problem(CommunicationsService.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            CommunicationsService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "recording_not_found",
                    "This call has no recording."),
            CommunicationsService.Failure.RecordingExpired =>
                (StatusCodes.Status404NotFound, "recording_expired",
                    "This call was recorded, and the recording has passed its retention period and been deleted."),
            CommunicationsService.Failure.NotYours =>
                (StatusCodes.Status403Forbidden, "not_your_call",
                    "That call belongs to another agent."),
            _ => (StatusCodes.Status404NotFound, "recording_not_found", "This call has no recording."),
        };

        var problem = new ProblemDetails
        {
            Title = "Recording not played",
            Detail = detail,
            Status = status,
        };

        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
