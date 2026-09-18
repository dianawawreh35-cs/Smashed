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
public record ResetPasswordRequest(
    [Required, MinLength(8), MaxLength(256)] string NewPassword);
