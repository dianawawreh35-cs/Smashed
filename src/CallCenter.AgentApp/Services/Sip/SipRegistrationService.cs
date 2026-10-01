using System.Net.Sockets;
using CallCenter.Shared.Contracts.Auth;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace CallCenter.AgentApp.Services.Sip;

/// <summary>
/// Registers the agent's extension with the PBX and keeps it registered (A-02).
/// </summary>
/// <remarks>
/// Registration is how the PBX learns where an extension is right now, so it
/// knows where to send an incoming call. It expires after a couple of minutes
/// and is refreshed; if the laptop sleeps or the VPN drops, it lapses and the
/// PBX stops offering calls to an address nobody is listening on.
///
/// One extension handles every call the agent makes or takes (SRS 2.3).
/// Internal calls are told apart by the other party's number against the list in
/// S-48, which is a reporting concern rather than anything this class does.
///
/// Nothing here is configured on the laptop. The host and the credentials arrive
/// in the login response (A-01) — the supervisor sets them in the web app, and a
/// change reaches every laptop at the next sign-in.
/// </remarks>
public class SipRegistrationService(
    SipTransportHost transport, ILogger<SipRegistrationService> logger) : IDisposable
{
    /// <summary>
    /// How long a registration lasts before it is refreshed, in seconds. Short
    /// enough that a laptop which vanished stops being offered calls quickly.
    /// </summary>
    private const int ExpirySeconds = 120;

    /// <summary>
    /// How long to wait before trying again after a failure the PBX did not
    /// answer, in seconds. The library's default is 300, which would leave an
    /// agent unable to take calls for five minutes after a brief VPN blip.
    /// </summary>
    private const int RetryIntervalSeconds = 30;

    /// <summary>
    /// How many times to retry. High on purpose: while an agent is signed in,
    /// the app should keep trying to get the phone back rather than give up and
    /// require a re-login. A refusal the PBX is certain about — bad credentials —
    /// stops immediately regardless, through exitOnUnequivocalFailure, unless
    /// this sign-in has already registered (<see cref="ReRegistration"/>).
    /// </summary>
    private const int MaxRegisterAttempts = 1000;

    /// <summary>
    /// How long the PBX has to confirm forgetting the old addresses before the
    /// registration goes ahead anyway (N-05).
    /// </summary>
    private static readonly TimeSpan ForgetAllLimit = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the un-REGISTER may take: two round trips, the REGISTER and the
    /// answer to the PBX's challenge, inside the 5 s an app has to close (M-A01).
    /// </summary>
    private static readonly TimeSpan UnregisterLimit = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();

    private SIPRegistrationUserAgent? _agent;
    private RegistrationState? _state;

    /// <summary>What the current sign-in registers, for the un-REGISTER.</summary>
    private AgentExtensionsDto? _extensions;

    /// <summary>The Contact the registration agent sends, for the un-REGISTER.</summary>
    private SIPContactHeader? _contact;

    /// <summary>Whether the PBX has accepted this sign-in's registration at least once.</summary>
    private bool _registered;

    /// <summary>
    /// Password challenges the PBX has refused in a row since this sign-in
    /// last registered, for <see cref="ReRegistration"/>.
    /// </summary>
    private int _refusalsInARow;

    /// <summary>Cancels a start still waiting for the PBX to forget the old addresses.</summary>
    private CancellationTokenSource? _starting;

    /// <summary>Raised whenever the registration changes state.</summary>
    public event EventHandler? Changed;

    public RegistrationState? State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>True once the extension is registered — the phone is usable.</summary>
    public bool IsReady => State?.IsUsable == true;

    /// <summary>
    /// Starts registering. Safe to call again — any previous registration is
    /// torn down first, which is what a second sign-in on a shared laptop does
    /// (A-05).
    /// </summary>
    /// <remarks>
    /// N-05, guard 2 (27 Sep evening): before registering, the PBX is told to
    /// forget every address it has for the extension
    /// (<see cref="RegisterRequests.RemoveAll"/>), so a hidden copy, a crashed
    /// one, or the same agent on another laptop stops getting this agent's
    /// calls the moment the agent signs in here. Off the caller's thread, so
    /// the sign-in does not wait for the PBX; the status says Registering
    /// meanwhile, as it did before.
    /// </remarks>
    public void Start(AgentExtensionsDto extensions)
    {
        StopRefreshing();

        var starting = new CancellationTokenSource();

        lock (_gate)
        {
            _starting = starting;
            _extensions = extensions;
            _state = new RegistrationState(extensions.Extension, RegistrationStatus.Registering);
        }

        Changed?.Invoke(this, EventArgs.Empty);

        var ct = starting.Token;
        _ = Task.Run(() => ForgetOldAddressesThenRegisterAsync(extensions, ct));
    }

    private async Task ForgetOldAddressesThenRegisterAsync(AgentExtensionsDto extensions, CancellationToken ct)
    {
        try
        {
            var answer = await SendAsync(
                RegisterRequests.RemoveAll(extensions.Extension, extensions.SipServer),
                extensions, ForgetAllLimit, ct);

            if (answer?.Status == SIPResponseStatusCodesEnum.Ok)
            {
                logger.LogInformation(
                    "The PBX forgot every address it had for extension {Extension} (Contact: *) before this laptop registers",
                    extensions.Extension);
            }
            else
            {
                // Registering anyway: on chan_sip the REGISTER that follows
                // replaces the one address an extension can have.
                logger.LogWarning(
                    "The PBX did not confirm forgetting the old addresses of extension {Extension}: {Detail}. Registering anyway",
                    extensions.Extension, answer is null ? "no answer" : Describe(answer, null));
            }
        }
        catch (OperationCanceledException)
        {
            // Signed out, or signed in again, while the PBX was answering.
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not ask the PBX to forget the old addresses; registering anyway");
        }

        lock (_gate)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            _agent = CreateAgent(extensions);
            _agent.Start();
        }

        logger.LogInformation(
            "Registering extension {Extension} with {Server}", extensions.Extension, extensions.SipServer);
    }

    private SIPRegistrationUserAgent CreateAgent(AgentExtensionsDto extensions)
    {
        // The shared transport, not one of our own: the address the PBX
        // learns from this REGISTER is where it will send the INVITE, and
        // that has to be the socket the call agent is listening on.
        var agent = new SIPRegistrationUserAgent(
            transport.Transport,
            username: extensions.Extension,
            password: extensions.Secret,
            server: extensions.SipServer,
            expiry: ExpirySeconds,
            maxRegistrationAttemptTimeout: 60,
            registerFailureRetryInterval: RetryIntervalSeconds,
            maxRegisterAttempts: MaxRegisterAttempts,
            exitOnUnequivocalFailure: true,

            // Without this the Contact header is "sip:<ip>:<port>" with no
            // user part, and Asterisk has nothing to tie the registration to
            // extension 2001. Registration still succeeds and calls are
            // never offered — which looks exactly like a working phone that
            // nobody rings. The proof-of-concept app against this same PBX
            // sets it for the same reason.
            sendUsernameInContactHeader: true);

        // The Contact it registers, so the un-REGISTER at sign-out names the
        // same binding (N-05). Taken before the transport fills in the local
        // address, and sent through the same transport, so it is filled in
        // the same way.
        agent.AdjustRegister = request =>
        {
            if (request.Header.Contact is [var contact, ..])
            {
                lock (_gate)
                {
                    _contact ??= contact.CopyOf();
                }
            }

            return request;
        };

        var extension = extensions.Extension;

        // Parameter types are inferred from the library's delegates.
        agent.RegistrationSuccessful += (_, _) =>
        {
            logger.LogInformation("Extension {Extension} registered", extension);

            lock (_gate)
            {
                _registered = true;
                _refusalsInARow = 0;
            }

            Update(s => s with { Status = RegistrationStatus.Registered, Detail = null });
        };

        // The PBX answered and refused, and the library has stopped. On the
        // first registration of a sign-in that means the extension or its
        // password is wrong, and the agent needs their supervisor. After this
        // sign-in has registered, the password is known to be right, so a 401
        // to the answered challenge is the PBX asking again, as Issabel does
        // when the answer came too late (1 Oct 20:01, after a 30 s dropout,
        // with no "Wrong password" in its log): the app logs in again instead
        // (A-02).
        agent.RegistrationFailed += (uri, response, error) =>
        {
            var detail = Describe(response, error);
            var challenged = response?.Status is SIPResponseStatusCodesEnum.Unauthorised
                or SIPResponseStatusCodesEnum.ProxyAuthenticationRequired;

            TimeSpan? wait = null;
            int refusals = 0;
            var ct = CancellationToken.None;

            lock (_gate)
            {
                if (!ReferenceEquals(_agent, agent))
                {
                    // A registration this sign-in has already replaced.
                    return;
                }

                if (challenged && _registered)
                {
                    refusals = ++_refusalsInARow;
                    wait = ReRegistration.Wait(refusals);
                    ct = _starting?.Token ?? CancellationToken.None;
                }
            }

            if (wait is not { } delay)
            {
                logger.LogError("Extension {Extension} was refused: {Detail}", extension, detail);
                Update(s => s with { Status = RegistrationStatus.Failed, Detail = detail });
                return;
            }

            logger.LogWarning(
                "Extension {Extension} was asked for its password again ({Detail}) after registering on this sign-in; " +
                "logging in again in {Seconds} s (refusal {Count} in a row)",
                extension, detail, delay.TotalSeconds, refusals);

            // Still refused after a few minutes, the password may have been
            // changed at the PBX: the supervisor is the one to ask, though the
            // app keeps trying in case it clears.
            var status = ReRegistration.LooksWrong(refusals) ? RegistrationStatus.Failed : RegistrationStatus.Retrying;
            Update(s => s with { Status = status, Detail = detail });

            _ = RegisterAgainAsync(agent, delay, ct);
        };

        // Nothing came back, or something transient. Another attempt is coming.
        agent.RegistrationTemporaryFailure += (_, response, error) =>
        {
            var detail = Describe(response, error);
            logger.LogWarning("Extension {Extension} not registered yet: {Detail}", extension, detail);
            Update(s => s with { Status = RegistrationStatus.Retrying, Detail = detail });
        };

        agent.RegistrationRemoved += (_, _) =>
        {
            logger.LogInformation("Extension {Extension} is no longer registered", extension);
            Update(s => s with { Status = RegistrationStatus.Idle, Detail = null });
        };

        return agent;
    }

    /// <summary>
    /// Replaces a registration the library gave up on with a new one, which
    /// starts with a fresh REGISTER and answers the PBX's challenge with the
    /// password again. Nothing happens if the agent signed out, or signed in
    /// again, in the meantime.
    /// </summary>
    private async Task RegisterAgainAsync(SIPRegistrationUserAgent refused, TimeSpan wait, CancellationToken ct)
    {
        try
        {
            await Task.Delay(wait, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            if (ct.IsCancellationRequested || _extensions is null || !ReferenceEquals(_agent, refused))
            {
                return;
            }

            logger.LogInformation("Logging extension {Extension} in again", _extensions.Extension);

            _agent = CreateAgent(_extensions);
            _agent.Start();
        }

        try
        {
            refused.Stop(sendZeroExpiryRegister: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The refused registration agent did not stop cleanly");
        }
    }

    /// <summary>
    /// Ends the registration. With <paramref name="unregister"/>, the PBX is
    /// told to forget this laptop's address, and the answer is waited for, so
    /// it stops offering calls here straight away rather than when the
    /// registration lapses.
    /// </summary>
    /// <param name="unregister">
    /// False when the agent has signed in on another laptop (N-05). chan_sip
    /// keeps one address per extension and takes any <c>Expires: 0</c> as
    /// "unregister the extension", whoever sends it, and by then the address it
    /// holds is the other laptop's. This copy only stops refreshing.
    /// </param>
    /// <remarks>
    /// Awaited, for at most <see cref="UnregisterLimit"/> (M-A01, checked on
    /// 27 Sep evening): until then this was SIPSorcery's <c>Stop()</c>, which
    /// sends its zero-expiry REGISTER from the thread pool and returns, so a
    /// closing app could shut the transport before it went, or before the
    /// PBX's challenge to it was answered. The transport stays up: it belongs
    /// to the process, not to the session.
    /// </remarks>
    public async Task StopAsync(bool unregister, CancellationToken ct = default)
    {
        var (extensions, contact, registered) = StopRefreshing();

        if (extensions is null)
        {
            return;
        }

        if (!unregister)
        {
            logger.LogInformation(
                "Extension {Extension} is left registered to the laptop the agent signed in on; not unregistering",
                extensions.Extension);
            return;
        }

        if (!registered)
        {
            // Never registered: there is nothing of this laptop's at the PBX.
            return;
        }

        try
        {
            // The address it registered; if that was never seen, every
            // address, which on chan_sip is the same thing.
            var request = contact is null
                ? RegisterRequests.RemoveAll(extensions.Extension, extensions.SipServer)
                : RegisterRequests.Remove(extensions.Extension, extensions.SipServer, contact);

            var answer = await SendAsync(request, extensions, UnregisterLimit, ct);

            if (answer?.Status == SIPResponseStatusCodesEnum.Ok)
            {
                logger.LogInformation("Extension {Extension} unregistered", extensions.Extension);
            }
            else
            {
                logger.LogWarning(
                    "The PBX did not confirm the un-REGISTER of {Extension}: {Detail}. It lapses within {Seconds} s",
                    extensions.Extension, answer is null ? "no answer" : Describe(answer, null), ExpirySeconds);
            }
        }
        catch (Exception ex)
        {
            // Signing out must not fail because the PBX is unreachable.
            logger.LogWarning(ex, "The registration could not be ended cleanly");
        }
    }

    /// <summary>
    /// Stops the registration agent without a word to the PBX, and says what
    /// was registered, for <see cref="StopAsync"/>.
    /// </summary>
    private (AgentExtensionsDto? Extensions, SIPContactHeader? Contact, bool Registered) StopRefreshing()
    {
        SIPRegistrationUserAgent? agent;
        AgentExtensionsDto? extensions;
        SIPContactHeader? contact;
        bool registered;

        lock (_gate)
        {
            _starting?.Cancel();
            _starting?.Dispose();
            _starting = null;

            agent = _agent;
            extensions = _extensions;
            contact = _contact;
            registered = _registered;

            _agent = null;
            _extensions = null;
            _contact = null;
            _registered = false;
            _refusalsInARow = 0;
            _state = null;
        }

        if (extensions is null)
        {
            return (null, null, false);
        }

        try
        {
            agent?.Stop(sendZeroExpiryRegister: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The registration agent did not stop cleanly");
        }

        logger.LogInformation("Registration ended");
        Changed?.Invoke(this, EventArgs.Empty);

        return (extensions, contact, registered);
    }

    /// <summary>
    /// Sends one REGISTER of our own and waits for the final answer, answering
    /// the PBX's challenge once. Null when nothing came back in time.
    /// </summary>
    private async Task<SIPResponse?> SendAsync(
        SIPRequest request, AgentExtensionsDto extensions, TimeSpan limit, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);

        try
        {
            var answer = await SendOnceAsync(request, timeout.Token);

            if (answer?.Status is SIPResponseStatusCodesEnum.Unauthorised
                or SIPResponseStatusCodesEnum.ProxyAuthenticationRequired)
            {
                answer = await SendOnceAsync(
                    RegisterRequests.Authenticated(request, answer, extensions.Extension, extensions.Secret),
                    timeout.Token);
            }

            return answer;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<SIPResponse?> SendOnceAsync(SIPRequest request, CancellationToken ct)
    {
        var answered = new TaskCompletionSource<SIPResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var transaction = new SIPNonInviteTransaction(transport.Transport, request, null);
        transaction.NonInviteTransactionFinalResponseReceived += (_, _, _, response) =>
        {
            answered.TrySetResult(response);
            return Task.FromResult(SocketError.Success);
        };
        transaction.NonInviteTransactionFailed += (_, _) => answered.TrySetResult(null);

        transaction.SendRequest();

        return await answered.Task.WaitAsync(ct);
    }

    /// <summary>The PBX's own words, so a refusal can be acted on rather than guessed at.</summary>
    private static string Describe(SIPResponse? response, string? error) =>
        response is not null
            ? $"{(int)response.Status} {response.ReasonPhrase ?? response.Status.ToString()}"
            : error ?? "no answer";

    private void Update(Func<RegistrationState, RegistrationState> change)
    {
        lock (_gate)
        {
            if (_state is null) return;
            _state = change(_state);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The process is going. Signing out has already unregistered, if there
    /// was a sign-in; this only makes sure nothing is left refreshing.
    /// </summary>
    public void Dispose() => StopRefreshing();
}
