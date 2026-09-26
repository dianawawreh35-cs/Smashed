namespace CallCenter.Shared.Contracts.Pbx;

/// <summary>
/// Whether the call queue is open, as far as the server knows (S-60).
/// </summary>
/// <param name="IsOpen">
/// Null until a supervisor has said which it is: <c>*280</c> switches the queue
/// either way, so the server cannot switch it to a state before it knows the
/// current one.
/// </param>
/// <param name="ChangedAt">When it was last switched or corrected.</param>
/// <param name="ChangedBy">Who did it, by display name. Null when it was the daily opening.</param>
/// <param name="ChangedAutomatically">The last change was the daily opening, not a person.</param>
/// <param name="Configured">The server's extension is set up, so the switch can dial.</param>
/// <param name="AutoOpenAt">The daily opening time, <c>HH:mm</c>; null when it is off.</param>
/// <param name="AutoOpenProblem">Why today's opening did not happen, if it did not.</param>
public record QueueStatusDto(
    bool? IsOpen,
    DateTimeOffset? ChangedAt,
    string? ChangedBy,
    bool ChangedAutomatically,
    bool Configured,
    string? AutoOpenAt,
    string? AutoOpenProblem);

/// <summary>Open or close the queue, or say which it is.</summary>
public record SetQueueRequest(bool Open);

/// <summary>What pressing Open or Close did (S-60).</summary>
/// <param name="Code">
/// Null when it worked. Otherwise <c>not_configured</c>, <c>unknown_state</c>,
/// <c>busy</c> (someone else is switching it right now) or <c>pbx_failed</c>,
/// with <paramref name="Error"/> saying what the PBX did.
/// </param>
public record QueueSwitchResultDto(
    bool Ok,
    string? Code,
    string? Error,
    QueueStatusDto Status);
