namespace CallCenter.Shared.Contracts.Users;

/// <summary>
/// An account as the supervisor sees it (S-42).
/// </summary>
/// <remarks>
/// The extension number is here; its SIP secret is not, and there is no endpoint
/// that returns it. The supervisor sets a secret and can replace it, but never
/// reads one back (N-05) — <see cref="HasSipCredentials"/> is how the screen
/// shows that a secret is set without disclosing it.
/// </remarks>
/// <param name="AppVersion">
/// The Agent App version of this account's latest sign-in to the Agent App,
/// and <paramref name="AppSignedInAt"/> when that was (S-42). Null for one that
/// has never signed in to it, which is every supervisor.
/// </param>
public record UserDto(
    Guid Id,
    string Login,
    string DisplayName,
    string Role,
    bool IsActive,
    string? Extension,
    bool HasSipCredentials,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    string? AppVersion = null,
    DateTimeOffset? AppSignedInAt = null)
{
    /// <summary>True once the extension and its secret are set (A-01).</summary>
    public bool CanTakeCalls => IsActive && HasSipCredentials;
}
