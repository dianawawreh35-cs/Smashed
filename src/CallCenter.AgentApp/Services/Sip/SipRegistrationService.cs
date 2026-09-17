using System.Net;
using CallCenter.Shared.Contracts.Auth;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace CallCenter.AgentApp.Services.Sip;

/// <summary>
/// Registers the agent's two extensions with the PBX and keeps them registered
/// (A-02).
/// </summary>
/// <remarks>
/// Registration is how the PBX learns where an extension is right now, so it
/// knows where to send an incoming call. It expires after a couple of minutes
/// and is refreshed; if the laptop sleeps or the VPN drops, it lapses and the
/// PBX stops offering calls to an address nobody is listening on.
///
/// Nothing here is configured on the laptop. The host and both credentials
/// arrive in the login response (A-01) — the supervisor sets them in the web
/// app, and a change reaches every laptop at the next sign-in.
/// </remarks>
public class SipRegistrationService(ILogger<SipRegistrationService> logger) : IDisposable
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

    private SIPTransport? _transport;
    private readonly List<SIPRegistrationUserAgent> _agents = [];

    private RegistrationState? _customer;
    private RegistrationState? _internal;

    /// <summary>Raised whenever either extension changes state.</summary>
    public event EventHandler? Changed;

    public RegistrationState? Customer
    {
        get { lock (_gate) return _customer; }
    }

    public RegistrationState? Internal
    {
        get { lock (_gate) return _internal; }
    }

    /// <summary>True once both extensions are registered — the phone is usable.</summary>
    public bool IsReady => Customer?.IsUsable == true && Internal?.IsUsable == true;

    /// <summary>
    /// Starts registering both extensions. Safe to call again — the previous
    /// registrations are torn down first, which is what a second sign-in on a
    /// shared laptop does (A-05).
    /// </summary>
    public void Start(AgentExtensionsDto extensions)
    {
        Stop();

        lock (_gate)
        {
            _transport = new SIPTransport();

            // Any free local port. The PBX learns where to reach us from the
            // REGISTER itself, so nothing here has to be predictable.
            _transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0)));

            _customer = new RegistrationState(
                ExtensionRole.Customer, extensions.CustomerExtension, RegistrationStatus.Registering);
            _internal = new RegistrationState(
                ExtensionRole.Internal, extensions.InternalExtension, RegistrationStatus.Registering);

            Register(ExtensionRole.Customer, extensions.CustomerExtension, extensions.CustomerSecret, extensions.SipServer);
            Register(ExtensionRole.Internal, extensions.InternalExtension, extensions.InternalSecret, extensions.SipServer);
        }

        logger.LogInformation(
            "Registering extensions {Customer} and {Internal} with {Server}",
            extensions.CustomerExtension, extensions.InternalExtension, extensions.SipServer);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Unregisters both extensions and drops the transport, so the PBX stops
    /// offering calls to this laptop straight away rather than waiting for the
    /// registrations to lapse.
    /// </summary>
    public void Stop()
    {
        List<SIPRegistrationUserAgent> agents;

        lock (_gate)
        {
            if (_transport is null && _agents.Count == 0)
            {
                return;
            }

            agents = [.. _agents];
            _agents.Clear();
            _customer = null;
            _internal = null;
        }

        foreach (var agent in agents)
        {
            try
            {
                agent.Stop();
            }
            catch (Exception ex)
            {
                // Signing out must not fail because the PBX is unreachable.
                logger.LogWarning(ex, "A registration could not be ended cleanly");
            }
        }

        lock (_gate)
        {
            _transport?.Shutdown();
            _transport = null;
        }

        logger.LogInformation("Registrations ended");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts one extension's registration agent. Called under the lock.</summary>
    private void Register(ExtensionRole role, string extension, string secret, string server)
    {
        var agent = new SIPRegistrationUserAgent(
            _transport!,
            username: extension,
            password: secret,
            server: server,
            expiry: ExpirySeconds,
            maxRegistrationAttemptTimeout: 60,
            registerFailureRetryInterval: RetryIntervalSeconds,
            maxRegisterAttempts: MaxRegisterAttempts,
            exitOnUnequivocalFailure: true);

        // Parameter types are inferred from the library's delegates.
        agent.RegistrationSuccessful += (_, _) =>
        {
            logger.LogInformation("Extension {Extension} registered", extension);
            Update(role, s => s with { Status = RegistrationStatus.Registered, Detail = null });
        };

        // The PBX answered and refused. Retrying will not help; the agent needs
        // their supervisor to check the extension or its password.
        agent.RegistrationFailed += (_, response, error) =>
        {
            var detail = Describe(response, error);
            logger.LogError("Extension {Extension} was refused: {Detail}", extension, detail);
            Update(role, s => s with { Status = RegistrationStatus.Failed, Detail = detail });
        };

        // Nothing came back, or something transient. Another attempt is coming.
        agent.RegistrationTemporaryFailure += (_, response, error) =>
        {
            var detail = Describe(response, error);
            logger.LogWarning("Extension {Extension} not registered yet: {Detail}", extension, detail);
            Update(role, s => s with { Status = RegistrationStatus.Retrying, Detail = detail });
        };

        agent.RegistrationRemoved += (_, _) =>
        {
            logger.LogInformation("Extension {Extension} is no longer registered", extension);
            Update(role, s => s with { Status = RegistrationStatus.Idle, Detail = null });
        };

        _agents.Add(agent);
        agent.Start();
    }

    /// <summary>The PBX's own words, so a refusal can be acted on rather than guessed at.</summary>
    private static string Describe(SIPResponse? response, string? error) =>
        response is not null
            ? $"{(int)response.Status} {response.ReasonPhrase ?? response.Status.ToString()}"
            : error ?? "no answer";

    private void Update(ExtensionRole role, Func<RegistrationState, RegistrationState> change)
    {
        lock (_gate)
        {
            if (role == ExtensionRole.Customer)
            {
                if (_customer is null) return;
                _customer = change(_customer);
            }
            else
            {
                if (_internal is null) return;
                _internal = change(_internal);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop();
}
