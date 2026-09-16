using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Users;

/// <summary>Creates an agent or supervisor account (S-42).</summary>
/// <param name="Role">One of <see cref="UserRoles"/>.</param>
/// <param name="Password">
/// The starting password, given to the person out of band. There is no
/// self-service reset in this version, so the supervisor sets a new one when it
/// is forgotten.
/// </param>
public record CreateUserRequest(
    [Required, MinLength(3), MaxLength(64)] string Login,
    [Required, MaxLength(128)] string DisplayName,
    [Required] string Role,
    [Required, MinLength(8), MaxLength(256)] string Password);
