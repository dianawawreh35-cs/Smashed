namespace CallCenter.Shared.Contracts.Communications;

/// <summary>
/// One row of the supervisor's search across every call (S-02).
/// </summary>
/// <remarks>
/// Wider than <see cref="CommunicationDto"/>, which is shaped for an agent's own
/// log: the supervisor also needs who took it, which branch it was for, what it
/// was and what it was worth, without opening each one.
/// </remarks>
/// <param name="Kind">
/// <c>Call</c> today. App communications (A-70) will join the same list, which
/// is why the search is over communications rather than calls.
/// </param>
/// <param name="Notes">
/// The classification's notes for a classified call, otherwise the call's own
/// note (why it was missed or rejected, A-41). Whichever there is, for the row.
/// </param>
/// <param name="HasRecording">The audio is on the server and can be played (S-04).</param>
/// <param name="RecordingExpired">
/// It was recorded and retention has since deleted the audio (A-33). Kept apart
/// from <paramref name="HasRecording"/> so the screen can say "expired" rather
/// than "never recorded".
/// </param>
public record CallSearchRowDto(
    Guid Id,
    string Kind,
    DateTimeOffset StartedAt,
    string Direction,
    string Status,
    Guid? AgentId,
    string? AgentDisplayName,
    Guid? ContactId,
    string? ContactName,
    string? RemoteNumberRaw,
    Guid? BranchId,
    string? BranchName,
    string? TypeName,
    string? TypeLabelAr,
    string? TypeLabelEn,
    decimal? OrderValue,
    int? DurationSec,
    string? Notes,
    bool IsClassified,
    bool HasRecording,
    bool RecordingExpired);

/// <summary>A page of <see cref="CallSearchRowDto"/>, newest first, with how many match in all.</summary>
public record CallSearchPageDto(
    IReadOnlyList<CallSearchRowDto> Rows,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// Everything about one call except its classification and the history of it,
/// which come from the classification endpoints (S-03).
/// </summary>
/// <param name="CallNotes">
/// The call's own note: why a missed, rejected or unanswered call went that way
/// (A-41). An answered call's notes are on its classification.
/// </param>
/// <param name="WaitSec">Time in the queue before an agent took it, from the CDR import (S-55). Null until that exists.</param>
public record CallDetailsDto(
    CallSearchRowDto Summary,
    string? RemoteName,
    DateTimeOffset? AnsweredAt,
    DateTimeOffset? EndedAt,
    int? WaitSec,
    string? QueueName,
    string? Extension,
    string? CallNotes);
