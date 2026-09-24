using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Communications;

/// <summary>
/// One call, reported by the Agent App as it ends (A-14).
/// </summary>
/// <remarks>
/// Sent for <b>every</b> call the app saw, not only the answered ones: a missed
/// call, a call the agent rejected and a call rejected automatically because the
/// number is blocked all matter to the supervisor's reports, and the blocked one
/// is required by A-17 in particular.
///
/// The app reports what it knows and the server decides the rest. It does not
/// send a contact id — matching a number to a customer is the server's job and
/// its rules live there (A-13) — and it does not send the channel, because a
/// call is always Phone.
/// </remarks>
/// <param name="SipCallId">
/// The Call-ID from the INVITE. With <paramref name="Extension"/> it is what
/// makes reporting the same call twice harmless: a unique index on the pair
/// turns a retry into an update instead of a duplicate. The offline queue can
/// therefore resend without checking what already arrived.
/// </param>
/// <param name="Status">
/// One of <see cref="CommunicationStatuses"/>: Answered, Missed, Rejected,
/// Blocked or Failed.
/// </param>
/// <param name="Queue">
/// The queue the call came through, from the <c>X-Queue-Name</c> header the
/// dialplan sets. Null for a direct call.
/// </param>
/// <param name="LaptopId">
/// Which laptop logged it. Laptops are shared between shifts, so this is worth
/// having when a call and an agent do not appear to line up.
/// </param>
public record LogCallRequest(
    [Required, MaxLength(200)] string SipCallId,
    [Required, MaxLength(40)] string Extension,
    [Required] string Direction,
    [Required] string Status,
    [MaxLength(40)] string? RemoteNumber,
    [MaxLength(200)] string? RemoteName,
    DateTimeOffset StartedAt,
    DateTimeOffset? AnsweredAt,
    DateTimeOffset? EndedAt,
    [MaxLength(100)] string? Queue,
    [MaxLength(100)] string? LaptopId);

/// <summary>
/// A call as the server stored it, returned so the app can show what was saved
/// and, once the pop-up does the lookup, which customer it was matched to.
/// </summary>
/// <param name="ContactId">
/// The contact the number matched, or null for a number nobody has on file.
/// Decided by the server (A-13), never sent by the app.
/// </param>
/// <param name="DurationSec">Talk time: answered to ended. Null when never answered.</param>
/// <param name="Notes">
/// The agent's note on a missed, rejected or unanswered outbound call — why it
/// went that way. Null for every other call; an answered call's notes are on
/// its classification.
/// </param>
public record CommunicationDto(
    Guid Id,
    string Kind,
    string Direction,
    string Status,
    Guid? ContactId,
    string? ContactName,
    string? RemoteNumberRaw,
    string? RemoteName,
    DateTimeOffset StartedAt,
    DateTimeOffset? AnsweredAt,
    DateTimeOffset? EndedAt,
    int? DurationSec,
    string? QueueName,
    string? Extension,
    string? AgentDisplayName,
    bool IsClassified,
    string? Notes)
{
    /// <summary>
    /// Only an answered call is classified (A-40). Anything else had no
    /// conversation, so there is nothing for the form to describe.
    /// </summary>
    public bool CanBeClassified => Status == CommunicationStatuses.Answered;

    /// <summary>
    /// A missed, rejected or unanswered outbound call takes a note instead —
    /// why it went that way. Blocked and failed calls take neither.
    /// </summary>
    public bool TakesNotes => CommunicationStatuses.TakesNotes(Status);

    /// <summary>
    /// A call the agent still owes a classification for (A-41), highlighted in
    /// their call log until they deal with it.
    /// </summary>
    /// <remarks>
    /// Only answered calls. A missed, rejected or blocked call has nothing to
    /// classify — there was no conversation — and marking them as owing one
    /// would leave every agent with a list of work they can never clear.
    /// </remarks>
    public bool IsUnclassified => !IsClassified && CanBeClassified;
}

/// <summary>
/// The note on a missed, rejected or unanswered outbound call (A-41). Blank
/// clears it.
/// </summary>
public record SaveCallNotesRequest([MaxLength(4000)] string? Notes);

/// <summary>
/// The same note, keyed on the call rather than its server id (A-04, A-41).
/// </summary>
/// <remarks>
/// For the pop-up: an outbound call the customer did not pick up is noted the
/// moment it ends, before the server has necessarily heard of it. The SIP
/// Call-ID and extension are the pair the server keys a call on, so the note is
/// queued behind its call and lands on it — the same route a classification
/// takes.
/// </remarks>
public record SaveCallNotesByCallRequest(
    [Required, MaxLength(200)] string SipCallId,
    [Required, MaxLength(40)] string Extension,
    [MaxLength(4000)] string? Notes);
