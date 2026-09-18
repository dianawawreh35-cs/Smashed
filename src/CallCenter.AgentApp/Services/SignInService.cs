using CallCenter.AgentApp.Services.Sip;
using CallCenter.Shared.Contracts.Auth;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// Sign-in and sign-out for the app as a whole (A-01, A-05): calls the server,
/// updates <see cref="AgentSession"/>, and remembers the username for next time.
/// </summary>
public class SignInService(
    ApiClient api,
    AgentSession session,
    AgentSettingsStore settings,
    SipRegistrationService sip,
    ILogger<SignInService> logger)
{
    /// <summary>
    /// Whether the sign-in worked, and if not, which of
    /// <see cref="LoginErrorCodes"/> says why. The caller turns the code into a
    /// message in the agent's language (A-80).
    /// </summary>
    public record SignInResult(bool Succeeded, string? ErrorCode)
    {
        public static readonly SignInResult Success = new(true, null);

        public static SignInResult Failed(string errorCode) => new(false, errorCode);
    }

    /// <summary>Signs in and opens a session on the server.</summary>
    public async Task<SignInResult> SignInAsync(string login, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
        {
            return SignInResult.Failed(LoginErrorCodes.EmptyFields);
        }

        var result = await api.LoginAsync(login.Trim(), password, ct);

        if (!result.IsOk || result.Value is null)
        {
            return SignInResult.Failed(result.ErrorCode ?? LoginErrorCodes.ServerError);
        }

        session.SignIn(result.Value);

        // Remembered so the next shift on this laptop only types a password.
        settings.Update(s => s with { LastLogin = login.Trim() });

        if (session.Extensions is { } extensions)
        {
            // The phone comes up as soon as the agent is in, rather than waiting
            // for them to do something (A-02).
            sip.Start(extensions);
        }

        if (!session.HasPhone)
        {
            // Not a failed sign-in: the agent can still use contacts and the app
            // orders tab. The phone is what is missing, and the status bar says so.
            logger.LogWarning(
                "{Login} signed in but the server returned no SIP extensions; the phone stays offline.",
                login);
        }

        return SignInResult.Success;
    }

    /// <summary>
    /// Signs out. The server call is best effort: a laptop that has lost the
    /// network still has to be able to hand over to the next shift, and the
    /// session is closed by the idle timer in that case.
    /// </summary>
    public async Task SignOutAsync(string reason = LogoutReasons.Manual, CancellationToken ct = default)
    {
        // Unregister first, so the PBX stops offering calls to this laptop
        // before the agent is told they are signed out.
        sip.Stop();

        if (session.SessionId is { } sessionId)
        {
            await api.LogoutAsync(sessionId, reason, ct);
        }

        session.SignOut();
        logger.LogInformation("Signed out ({Reason})", reason);
    }

    /// <summary>The username to pre-fill in the login box.</summary>
    public string? LastLogin => settings.Current.LastLogin;
}
