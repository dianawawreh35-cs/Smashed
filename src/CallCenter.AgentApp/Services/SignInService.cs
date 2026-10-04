using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.Services.Sip;
using CallCenter.Shared;
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
    BreakService breaks,
    DoNotDisturbReporter doNotDisturb,
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
            return SignInResult.Failed(Known(result.ErrorCode));
        }

        // The API authenticates anyone; this app is for agents. A supervisor
        // gets no extension and no session, and every call they took would be
        // refused by the server, so they are told where to go instead. Nothing
        // to close on the server: only agents are given a session row.
        if (result.Value.User.Role != UserRoles.Agent)
        {
            logger.LogInformation("Refused {Role} {Login}: the Agent App is for agents.",
                result.Value.User.Role, login.Trim());
            return SignInResult.Failed(LoginErrorCodes.NotAnAgent);
        }

        // The password too, for the POS tab, which takes the same login (A-88).
        session.SignIn(result.Value, password);

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
            // The extensions go to the call service too, not just to
            // registration: placing a call needs the PBX address and the
            // credentials to answer its challenge (A-20).
            calls.Start(extensions);
            sip.Start(extensions);
        }

        // The classification form, so the fields are in hand before the first
        // call rather than being fetched while an agent waits with a customer on
        // the line. This is also how a supervisor's change to the form reaches
        // agents without anything being reinstalled (S-40).
        //
        // Before the queue, not after (M-A04): a laptop with an evening's calls
        // waiting kept the agent at the sign-in screen, with no form, until
        // every one of them had gone.
        await classification.LoadAsync(ct);

        // A-86: today's break time so far, for the rail's timer, and the do
        // not disturb a break left on if the app stopped during one.
        await breaks.LoadAsync(ct);

        // A-18, S-66: the switch as it is now, after a break left on has been
        // turned off, so the supervisor's monitor shows it.
        doNotDisturb.Start();

        // Anything this agent's last shift could not send (A-04, A-14), in the
        // background: the agent is in, and the queue catches up behind them.
        callLog.FlushInBackground();

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
        // The agent signed in on another laptop, and the server has closed
        // this session already (N-05). The PBX's one address for the
        // extension is that laptop's now, and an un-REGISTER from here would
        // take it away, so this laptop only stops. Nor is there a session to
        // close.
        var elsewhere = reason == LogoutReasons.SignedInElsewhere;

        // A-86: a break ends with the sign-in, and do not disturb with it.
        // First, while the server still takes this sign-in's token.
        await breaks.EndForSignOutAsync(ct);
        doNotDisturb.Stop();

        // Unregister first, so the PBX stops offering calls to this laptop
        // before the agent is told they are signed out. Then end anything still
        // up: handing a live call to the next shift would be worse than
        // dropping it.
        await sip.StopAsync(unregister: !elsewhere, ct);
        calls.Stop();
        blockList.StopRefreshing();

        if (session.SessionId is { } sessionId && !elsewhere)
        {
            await api.LogoutAsync(sessionId, reason, ct);
        }

        session.SignOut();
        logger.LogInformation("Signed out ({Reason})", reason);
    }

    /// <summary>The username to pre-fill in the login box.</summary>
    public string? LastLogin => settings.Current.LastLogin;

    /// <summary>
    /// The sign-in screen shows <c>login.errors.{code}</c>, so a code with no
    /// label there would reach the agent as a raw key (N-09). One the app does
    /// not know, such as <c>bad_request</c> or anything a newer server adds,
    /// is shown as a server error.
    /// </summary>
    private static string Known(string? code) => code switch
    {
        LoginErrorCodes.InvalidCredentials or LoginErrorCodes.AccountDisabled or LoginErrorCodes.EmptyFields
            or LoginErrorCodes.ServerUnreachable or LoginErrorCodes.NotAnAgent or LoginErrorCodes.SignedOut
            or LoginErrorCodes.SignedInElsewhere
            => code,
        _ => LoginErrorCodes.ServerError,
    };
}
