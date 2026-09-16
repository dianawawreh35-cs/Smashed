using CallCenter.Shared.Contracts.Auth;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// Who is signed in right now, held in one place so every view model reads the
/// same answer. One instance per process.
/// </summary>
public class AgentSession
{
    /// <summary>Raised whenever <see cref="IsSignedIn"/> changes.</summary>
    public event EventHandler? Changed;

    public CurrentUserDto? User { get; private set; }

    public string? AccessToken { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>The server-side session row, needed to log out cleanly (A-05).</summary>
    public Guid? SessionId { get; private set; }

    /// <summary>
    /// The SIP credentials to register with, or null when the supervisor has not
    /// configured this agent's extensions yet.
    /// </summary>
    public AgentExtensionsDto? Extensions { get; private set; }

    public bool IsSignedIn => User is not null && AccessToken is not null;

    /// <summary>True when signed in but with no phone to register (A-02).</summary>
    public bool HasPhone => Extensions is not null;

    /// <summary>Records a successful login.</summary>
    public void SignIn(LoginResponse response)
    {
        User = response.User;
        AccessToken = response.AccessToken;
        ExpiresAt = response.ExpiresAt;
        SessionId = response.SessionId;
        Extensions = response.Extensions;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears everything the signed-in agent owns.</summary>
    public void SignOut()
    {
        User = null;
        AccessToken = null;
        ExpiresAt = null;
        SessionId = null;
        Extensions = null;

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
