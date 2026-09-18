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
    /// stops immediately regardless, through exitOnUnequivocalFailure.
    /// </summary>
    private const int MaxRegisterAttempts = 1000;

    private readonly Lock _gate = new();

    private SIPRegistrationUserAgent? _agent;
    private RegistrationState? _state;

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
    public void Start(AgentExtensionsDto extensions)
    {
        Stop();

        lock (_gate)
        {
            _state = new RegistrationState(extensions.Extension, RegistrationStatus.Registering);

            // The shared transport, not one of our own: the address the PBX
            // learns from this REGISTER is where it will send the INVITE, and
            // that has to be the socket the call agent is listening on.
            _agent = new SIPRegistrationUserAgent(
                transport.Transport,
                username: extensions.Extension,
                password: extensions.Secret,
                server: extensions.SipServer,
                expiry: ExpirySeconds,
                maxRegistrationAttemptTimeout: 60,
                registerFailureRetryInterval: RetryIntervalSeconds,
                maxRegisterAttempts: MaxRegisterAttempts,
                exitOnUnequivocalFailure: true);

            var extension = extensions.Extension;

            // Parameter types are inferred from the library's delegates.
            _agent.RegistrationSuccessful += (_, _) =>
            {
                logger.LogInformation("Extension {Extension} registered", extension);
                Update(s => s with { Status = RegistrationStatus.Registered, Detail = null });
            };

            // The PBX answered and refused. Retrying will not help; the agent
            // needs their supervisor to check the extension or its password.
            _agent.RegistrationFailed += (_, response, error) =>
            {
                var detail = Describe(response, error);
                logger.LogError("Extension {Extension} was refused: {Detail}", extension, detail);
                Update(s => s with { Status = RegistrationStatus.Failed, Detail = detail });
            };

            // Nothing came back, or something transient. Another attempt is coming.
            _agent.RegistrationTemporaryFailure += (_, response, error) =>
            {
                var detail = Describe(response, error);
                logger.LogWarning("Extension {Extension} not registered yet: {Detail}", extension, detail);
                Update(s => s with { Status = RegistrationStatus.Retrying, Detail = detail });
            };

            _agent.RegistrationRemoved += (_, _) =>
            {
                logger.LogInformation("Extension {Extension} is no longer registered", extension);
                Update(s => s with { Status = RegistrationStatus.Idle, Detail = null });
            };

            _agent.Start();
        }

        logger.LogInformation(
            "Registering extension {Extension} with {Server}", extensions.Extension, extensions.SipServer);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Unregisters, so the PBX stops offering calls to this laptop straight away
    /// rather than waiting for the registration to lapse.
    /// </summary>
    /// <remarks>
    /// The transport stays up: it belongs to the process, not to the session,
    /// and unregistering is what stops calls arriving.
    /// </remarks>
    public void Stop()
    {
        SIPRegistrationUserAgent? agent;

        lock (_gate)
        {
            if (_agent is null)
            {
                return;
            }

            agent = _agent;
            _agent = null;
            _state = null;
        }

        try
        {
            agent?.Stop();
        }
        catch (Exception ex)
        {
            // Signing out must not fail because the PBX is unreachable.
            logger.LogWarning(ex, "The registration could not be ended cleanly");
        }

        logger.LogInformation("Registration ended");
        Changed?.Invoke(this, EventArgs.Empty);
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

    public void Dispose() => Stop();
}
