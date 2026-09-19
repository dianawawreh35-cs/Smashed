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
    string? AgentDisplayName);
