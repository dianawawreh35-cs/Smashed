namespace CallCenter.AgentApp.Services.Calls;

/// <summary>Where a call has got to (A-12).</summary>
public enum CallStatus
{
    /// <summary>No call. The pop-up is not shown.</summary>
    Idle,

    /// <summary>Ringing, not yet answered. Answer and Reject are offered.</summary>
    Ringing,

    /// <summary>
    /// A call this agent placed, not yet answered (A-20). Its own status rather
    /// than a reuse of <see cref="Ringing"/>: there is nothing to answer or
    /// reject on a call you made, only to give up on, and sharing the status
    /// would put an Answer button on screen for it.
    /// </summary>
    Dialling,

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
/// <param name="IsMuted">
/// The microphone is paused (A-12). The customer hears nothing; the agent still
/// hears the customer. A flag on a connected call rather than a status of its
/// own, so everything that asks "is the call connected" — the timer, the
/// classification form, the outcome at hang-up — is untouched by it.
/// </param>
/// <param name="IsOnHold">
/// The customer is on the PBX's hold music (A-12) and neither side hears the
/// other. Independent of <paramref name="IsMuted"/>: a mute set before the hold
/// is still there when the hold ends.
/// </param>
/// <param name="IsOutbound">
/// This agent placed the call (A-20). Carried all the way to the call log,
/// where it is the difference between a customer who rang us and one we rang
/// (A-21).
/// </param>
/// <param name="IsInternal">
/// Placed with the Dial tab's switch set to internal: another agent or a branch
/// (A-23). Dialled without the outside-line prefix, and never classified or
/// given a note — there is no customer on it.
/// </param>
/// <param name="IsSecondLine">
/// Placed while another call was parked on hold (A-24). It takes no form and no
/// note of its own: the pop-up keeps the held customer's form, which is the
/// conversation the agent is in the middle of.
/// </param>
/// <param name="Held">
/// The call parked on hold behind this one (A-24), or null. Carried on the
/// state rather than asked for separately, for the same reason as everything
/// else here: one swap, never half a picture.
/// </param>
public record CallState(
    CallStatus Status,
    string? Number,
    string? CallerName,
    string? Queue,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ConnectedAt,
    string? SipCallId = null,
    bool IsMuted = false,
    bool IsOnHold = false,
    bool IsOutbound = false,
    bool IsInternal = false,
    bool IsSecondLine = false,
    CallState? Held = null)
{
    /// <summary>No call in progress.</summary>
    public static readonly CallState Idle = new(CallStatus.Idle, null, null, null, null, null);

    /// <summary>Whether the pop-up should be on screen.</summary>
    public bool IsActive => Status is not CallStatus.Idle;

    /// <summary>
    /// Whether a call can be placed now: no call at all, or a connected call on
    /// hold with nothing already parked behind it (A-20, A-24).
    /// </summary>
    public bool AllowsDialling =>
        Status is CallStatus.Idle
        || (Status is CallStatus.Connected && IsOnHold && Held is null);

    /// <summary>
    /// Whether the call gets the classification form on answer and the note
    /// when unanswered (A-40, A-41). Not an internal call (A-23), and not one
    /// placed over a held customer, whose form stays on screen (A-24).
    /// </summary>
    public bool TakesForm => !IsInternal && !IsSecondLine;

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

    /// <summary>
    /// An outbound call the customer never picked up, or that the agent gave up
    /// on while it rang (A-20).
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Missed"/>, which means a customer rang and
    /// nobody here answered. Mixing the two would count a customer who was out
    /// as this call centre failing to answer its phone.
    /// </remarks>
    NoAnswer,

    /// <summary>
    /// The call could not be placed at all: the number is not routable, or the
    /// PBX refused it. Distinct from <see cref="NoAnswer"/>, because trying
    /// again is pointless until somebody looks at the number.
    /// </summary>
    Failed,
}

/// <summary>
/// A call that is over, as reported to the server (A-14).
/// </summary>
/// <param name="IsOutbound">This agent placed the call (A-20, A-21).</param>
/// <param name="IsInternal">An internal call from the Dial tab (A-23).</param>
/// <param name="IsSecondLine">Placed while another call was on hold (A-24).</param>
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
    DateTimeOffset EndedAt,
    bool IsOutbound = false,
    bool IsInternal = false,
    bool IsSecondLine = false)
{
    /// <summary>
    /// Whether an unanswered call gets the note (A-41); see
    /// <see cref="CallState.TakesForm"/>.
    /// </summary>
    public bool TakesForm => !IsInternal && !IsSecondLine;
}

/// <summary>
/// A finished recording and the call it belongs to (A-31).
/// </summary>
/// <remarks>
/// The call is named by its SIP Call-ID, the same reference the classification
/// uses, because that is what the server keys a call on. Recording and
/// classification are therefore two things hanging off one call rather than
/// being linked to each other.
/// </remarks>
public record CallRecording(string SipCallId, RecordedCall File);

/// <summary>
/// A recording waiting in the queue: which call it belongs to, and where the
/// file is on this laptop (A-31).
/// </summary>
/// <remarks>
/// The path rather than the audio. The file is already on disk; copying
/// megabytes into the queue's database would achieve nothing except filling it.
/// </remarks>
public record PendingRecording(string SipCallId, string Extension, string LocalPath);
