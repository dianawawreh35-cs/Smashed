using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Users;

/// <summary>
/// Assigns an agent's extension and its SIP secret (S-42, SRS 2.3).
/// </summary>
/// <remarks>
/// The secret is write-only: it is encrypted on arrival and never returned
/// (N-05). Leave it null to keep the one already stored, so a supervisor can
/// correct an extension number without being asked for the password again.
/// </remarks>
public record SetExtensionRequest(
    [Required, MaxLength(32)] string Extension,
    [MaxLength(256)] string? Secret = null);
