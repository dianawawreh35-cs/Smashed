namespace CallCenter.AgentApp.Data;

/// <summary>
/// One thing the agent did that has not reached the server yet (A-04).
/// </summary>
/// <remarks>
/// <b>Deliberately generic.</b> The row says what kind of thing it is and
/// carries its payload as JSON, rather than having a table per kind. Two
/// reasons.
///
/// The first is ordering. A-04 queues call logs and classifications, and a
/// classification belongs to a call: replay them out of order and the
/// classification arrives for a call the server has never heard of. One table
/// with one sequence keeps them in the order they happened, whatever they are.
///
/// The second is that this shape never has to change. Adding notes, or app
/// orders, or anything else later is a new <see cref="Kind"/> and no schema
/// change at all — which is what makes it safe to create this database with
/// <c>EnsureCreated</c> rather than carrying migration tooling onto every
/// agent's laptop. A buffer that needed a migration would have to choose
/// between losing an offline shift's work and shipping a migration runner.
/// </remarks>
public class PendingUpload
{
    /// <summary>
    /// Auto-incrementing, and the replay order. Time is not used: two calls in
    /// the same second, or a laptop whose clock is corrected mid-shift, would
    /// both reorder the queue.
    /// </summary>
    public long Id { get; set; }

    /// <summary>What this is — see <see cref="PendingUploadKinds"/>.</summary>
    public string Kind { get; set; } = null!;

    /// <summary>The request body, as the server expects it, serialised.</summary>
    public string Payload { get; set; } = null!;

    /// <summary>
    /// What this refers to, for the app's own use: the SIP Call-ID and
    /// extension. Lets a classification find the call it belongs to while both
    /// are still queued, and makes the queue readable when something goes wrong.
    /// </summary>
    public string? Reference { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// How many times sending has been tried. Kept so a row that keeps failing
    /// can be found, rather than silently retried forever.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>Why the last attempt failed, for the log and for support.</summary>
    public string? LastError { get; set; }
}

/// <summary>The kinds of thing the buffer holds (A-04).</summary>
public static class PendingUploadKinds
{
    /// <summary>A finished call (A-14).</summary>
    public const string Call = "Call";

    /// <summary>
    /// A classification (A-40). Not written yet — the form does not exist — but
    /// named here because the ordering guarantee only means something if both
    /// kinds share the queue.
    /// </summary>
    public const string Classification = "Classification";
}
