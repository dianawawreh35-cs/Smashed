using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Contacts;

/// <summary>
/// Sets, changes or removes a contact's VIP and Blocked flags (S-45).
/// </summary>
/// <remarks>
/// Both flags travel together rather than one endpoint each, because they are
/// mutually exclusive and a single request is the only way to check that
/// without a race: two calls could leave a contact marked VIP *and* Blocked
/// between them.
///
/// Removing a flag is this same request with both set to false — S-45 asks for
/// the flag to be removable at any time, and a separate delete endpoint would
/// have produced a second audit vocabulary for the same event.
/// </remarks>
/// <param name="Reason">
/// Why. Required whenever a flag is being set: a block with no reason is one
/// nobody can review later. Ignored when both flags are false.
/// </param>
public record SetContactFlagsRequest(
    bool IsVip,
    bool IsBlocked,
    [MaxLength(500)] string? Reason);

/// <summary>
/// Flags a bare phone number (S-45) — a nuisance caller who is not a customer
/// and has no contact record.
/// </summary>
/// <remarks>
/// The number is matched against existing contacts first (A-13), so flagging
/// <c>0599123456</c> flags the customer already saved as <c>+970599123456</c>
/// rather than creating a second, nameless record for the same person. Only
/// when nothing matches is a nameless contact created to carry the flag.
/// </remarks>
public record FlagNumberRequest(
    [Required, MaxLength(40)] string Number,
    bool IsVip,
    bool IsBlocked,
    [MaxLength(500)] string? Reason);

/// <summary>
/// One row of the supervisor's VIP and blocked list (S-45).
/// </summary>
/// <param name="Numbers">As they were typed, for reading. Matching uses the normalised form.</param>
/// <param name="ChangedByDisplayName">Who last set the flag — the accountability S-45 asks for.</param>
public record FlaggedContactDto(
    Guid Id,
    string? Name,
    string? Address,
    bool IsVip,
    bool IsBlocked,
    string? FlagReason,
    string? ChangedByDisplayName,
    DateTimeOffset? ChangedAt,
    IReadOnlyList<string> Numbers);

/// <summary>
/// The block list as the Agent App caches it (A-17).
/// </summary>
/// <remarks>
/// Normalised numbers only, no names: the app compares an incoming caller id
/// against this and rejects a match, and it must keep working when the server
/// is unreachable, so the payload is small enough to write to disk on every
/// sign-in. <paramref name="AsOf"/> is what the app shows when it is working
/// from a cached copy.
/// </remarks>
public record BlockedNumbersDto(
    IReadOnlyList<string> Numbers,
    DateTimeOffset AsOf);

/// <summary>
/// One flag change, read back out of the audit log (S-45, N-06).
/// </summary>
/// <remarks>
/// Read from <c>audit_log</c> rather than a table of its own. The flags' own
/// columns hold only the current state, which answers "who blocked this
/// number?" but not "who unblocked it in March?" — and the second question is
/// the one a dispute turns on.
/// </remarks>
public record ContactFlagChangeDto(
    DateTimeOffset At,
    string? ByDisplayName,
    bool IsVip,
    bool IsBlocked,
    string? Reason);
