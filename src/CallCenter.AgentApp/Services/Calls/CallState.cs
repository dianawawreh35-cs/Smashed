namespace CallCenter.AgentApp.Services.Calls;

/// <summary>Where a call has got to (A-12).</summary>
public enum CallStatus
{
    /// <summary>No call. The pop-up is not shown.</summary>
    Idle,

    /// <summary>Ringing, not yet answered. Answer and Reject are offered.</summary>
    Ringing,

    /// <summary>Answered and talking. Hang up is offered, and the timer runs.</summary>
    Connected,
}

/// <summary>
/// The current call, as everything outside the SIP stack sees it (A-10, A-12).
/// </summary>
/// <remarks>
/// A record rather than properties on the service so that a state change is one
/// atomic swap. A view model reading "ringing" and then asking separately for
/// the number could otherwise catch the two mid-change and show a ringing call
/// with nobody on it.
///
/// Blocked calls never appear here at all: A-17 rejects them before anything is
/// shown, so there is no state for "blocked" to be in.
/// </remarks>
/// <param name="Number">
/// As the PBX presented it. Null when the caller withheld it — the pop-up says
/// so rather than showing an empty line.
/// </param>
/// <param name="CallerName">
/// The name the PBX sent, when it is not the number again. A stopgap until the
/// contact lookup puts the customer's real name here (A-10, A-11).
/// </param>
/// <param name="Queue">
/// Which queue the call came through, from the <c>X-Queue-Name</c> header the
/// dialplan sets. Null for a direct call. The agent needs it before they speak
/// — "Delivery" and "Complaints" are answered differently — and the
/// classification needs it afterwards, because complaints per queue is one of
/// the reports this system exists to produce.
/// </param>
/// <param name="StartedAt">When the call arrived, so a missed call can be timed.</param>
/// <param name="ConnectedAt">When it was answered, which is where the timer counts from.</param>
/// <param name="SipCallId">
/// The phone system's own reference for this call.
/// <para>
/// Carried here because the classification form opens while the call is still
/// in progress (A-40), and at that point the server has never heard of the call
/// — it is not reported until it ends. This, with the extension, is the same
/// pair the server keys a call on, so it is what a classification is attached
/// to. Without it the form would have nothing to point at.
/// </para>
/// </param>
public record CallState(
    CallStatus Status,
    string? Number,
    string? CallerName,
    string? Queue,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ConnectedAt,
    string? SipCallId = null)
{
    /// <summary>No call in progress.</summary>
    public static readonly CallState Idle = new(CallStatus.Idle, null, null, null, null, null);

    /// <summary>Whether the pop-up should be on screen.</summary>
    public bool IsActive => Status is not CallStatus.Idle;

    /// <summary>How long the agent has been talking, or null before the answer.</summary>
    public TimeSpan? Duration =>
        ConnectedAt is { } connected ? DateTimeOffset.Now - connected : null;
}

/// <summary>How a call ended (A-14). What the supervisor's reports count.</summary>
public enum CallOutcome
{
    /// <summary>The agent answered and spoke.</summary>
    Answered,

    /// <summary>The agent pressed Reject.</summary>
    RejectedByAgent,

    /// <summary>The number is on the block list and was declined automatically (A-17).</summary>
    Blocked,

    /// <summary>A second call arrived while this agent was already talking.</summary>
    Busy,

    /// <summary>It rang and nobody answered — the caller gave up, or the PBX moved on.</summary>
    Missed,
}

/// <summary>
/// A call that is over, as reported to the server (A-14).
/// </summary>
/// <remarks>
/// Separate from <see cref="CallState"/>, which describes a call in progress and
/// is what the pop-up binds to. A finished call is a different thing with
/// different fields — it always has an end, and it has an outcome rather than a
/// status — and merging the two would mean a record whose meaning depends on
/// which half is filled in.
///
/// Blocked calls produce one of these even though they never appear on screen:
/// A-17 requires them in the supervisor's reports.
/// </remarks>
public record FinishedCall(
    string SipCallId,
    string? Number,
    string? CallerName,
    string? Queue,
    CallOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset? AnsweredAt,
    DateTimeOffset EndedAt);
