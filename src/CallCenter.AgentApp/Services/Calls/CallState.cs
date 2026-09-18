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
public record CallState(
    CallStatus Status,
    string? Number,
    string? CallerName,
    string? Queue,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ConnectedAt)
{
    /// <summary>No call in progress.</summary>
    public static readonly CallState Idle = new(CallStatus.Idle, null, null, null, null, null);

    /// <summary>Whether the pop-up should be on screen.</summary>
    public bool IsActive => Status is not CallStatus.Idle;

    /// <summary>How long the agent has been talking, or null before the answer.</summary>
    public TimeSpan? Duration =>
        ConnectedAt is { } connected ? DateTimeOffset.Now - connected : null;
}
