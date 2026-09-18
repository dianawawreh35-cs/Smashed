namespace CallCenter.Shared.Contracts.Auth;

/// <summary>
/// The signed-in account, as returned by <c>GET /api/auth/me</c>. Carries no
/// secrets, so it is safe for both the Agent App and the supervisor SPA.
/// </summary>
/// <param name="Role">One of <see cref="UserRoles"/>.</param>
public record CurrentUserDto(
    Guid Id,
    string Login,
    string DisplayName,
    string Role)
{
    public bool IsSupervisor => Role == UserRoles.Supervisor;

    public bool IsAgent => Role == UserRoles.Agent;
}
