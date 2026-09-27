using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Users;

/// <summary>
/// Renames an account or enables/disables it (S-42). The login cannot be
/// changed — it is what the audit trail and the call log are written against.
/// </summary>
public record UpdateUserRequest(
    [Required, MaxLength(128)] string DisplayName,
    bool IsActive);

/// <summary>Sets a new password for an account (S-42).</summary>
/// <param name="CurrentPassword">
/// Needed only when a supervisor changes their own password, and refused when
/// wrong (27 Sep review): otherwise anyone at a browser left signed in could
/// change it and lock them out. Another account's password needs none.
/// </param>
public record ResetPasswordRequest(
    [Required, MinLength(8), MaxLength(256)] string NewPassword,
    [MaxLength(256)] string? CurrentPassword = null);
