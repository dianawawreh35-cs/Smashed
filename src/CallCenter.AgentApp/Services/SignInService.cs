using CallCenter.AgentApp.Services.Calls;
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
    BlockListCache blockList,
    CallService calls,
    CallLogReporter callLog,
    ClassificationCatalog classification,
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

        // Before the phone comes up, not after: a call could arrive the moment
        // the extension registers, and it has to be checked against a list that
        // is already loaded (A-17). A failure here is not fatal - the cached
        // list from the last shift stays in place, which is what it is for.
        //
        // And it keeps refreshing from here, so a number unblocked mid-shift
        // stops being rejected without the agent signing out and in.
        await blockList.StartRefreshingAsync(ct);

        if (session.Extensions is { } extensions)
        {
            // The phone comes up as soon as the agent is in, rather than waiting
            // for them to do something (A-02).
            calls.Start();
            sip.Start(extensions);
        }

        // Anything the last shift could not send. A laptop that was offline all
        // evening catches up the moment somebody signs in on it (A-04, A-14).
        await callLog.FlushAsync(ct);

        // The classification form, so the fields are in hand before the first
        // call rather than being fetched while an agent waits with a customer on
        // the line. This is also how a supervisor's change to the form reaches
        // agents without anything being reinstalled (S-40).
        await classification.LoadAsync(ct);

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
        // before the agent is told they are signed out. Then end anything still
        // up: handing a live call to the next shift would be worse than
        // dropping it.
        sip.Stop();
        calls.Stop();
        blockList.StopRefreshing();

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
