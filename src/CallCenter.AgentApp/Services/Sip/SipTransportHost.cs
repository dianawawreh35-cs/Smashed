using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;

namespace CallCenter.AgentApp.Services.Sip;

/// <summary>
/// The one SIP socket this app speaks on, shared by registration and calls.
/// </summary>
/// <remarks>
/// It has to be shared, and that is not a tidiness argument. Registration tells
/// the PBX where to reach this extension, and the address it gives is the one
/// the REGISTER was sent from. Calls then arrive at that address. Two transports
/// would mean the PBX sending its INVITE to the socket that registered, while
/// the call agent listened on a different one — the phone would appear
/// registered and never ring.
///
/// One transport for the life of the process, rather than one per sign-in.
/// Signing out unregisters, which is what stops calls being offered; tearing the
/// socket down as well would gain nothing and hand the next sign-in a different
/// port for no reason.
/// </remarks>
public sealed class SipTransportHost(ILogger<SipTransportHost> logger) : IDisposable
{
    private readonly Lock _gate = new();

    private SIPTransport? _transport;
    private bool _disposed;

    /// <summary>
    /// The transport, created on first use. Any free local port: the PBX learns
    /// where to reach us from the REGISTER itself, so nothing here has to be
    /// predictable or configured.
    /// </summary>
    public SIPTransport Transport
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_transport is not null)
                {
                    return _transport;
                }

                _transport = new SIPTransport();
                _transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0)));

                logger.LogInformation("SIP transport listening on {EndPoints}",
                    string.Join(", ", _transport.GetSIPChannels().Select(c => c.ListeningEndPoint)));

                return _transport;
            }
        }
    }

    public void Dispose()
    {
        SIPTransport? transport;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            transport = _transport;
            _transport = null;
        }

        try
        {
            transport?.Shutdown();
        }
        catch (Exception ex)
        {
            // Closing the app must not fail because a socket objected.
            logger.LogWarning(ex, "The SIP transport did not shut down cleanly");
        }
    }
}
