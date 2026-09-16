using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Users;

/// <summary>
/// Assigns an agent's two extensions and their SIP secrets (S-42, SRS 2.3).
/// </summary>
/// <remarks>
/// Both extensions are set together, because an agent with only one of them
/// cannot work: the app registers both at login or reports the phone as
/// unconfigured. The secrets are write-only — they are encrypted on arrival and
/// never returned (N-05).
/// </remarks>
/// <param name="CustomerExtension">The one the queue rings, and that calls customers.</param>
/// <param name="InternalExtension">Other agents and the four branches.</param>
/// <param name="CustomerSecret">
/// Leave null to keep the secret already stored — so a supervisor can correct an
/// extension number without being asked for the password again.
/// </param>
public record SetExtensionsRequest(
    [Required, MaxLength(32)] string CustomerExtension,
    [Required, MaxLength(32)] string InternalExtension,
    [MaxLength(256)] string? CustomerSecret = null,
    [MaxLength(256)] string? InternalSecret = null);
