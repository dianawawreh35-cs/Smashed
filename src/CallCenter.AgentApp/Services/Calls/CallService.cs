using CallCenter.AgentApp.Services.Sip;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;

namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// Taking a call: the ring, the answer, the hang-up (A-10, A-12, A-17).
/// </summary>
/// <remarks>
/// One call at a time, because there is one extension (SRS 2.3). A second call
/// arriving while one is up is refused as busy, which is what the PBX expects
/// and what lets it move the caller on to another agent.
///
/// Every event here arrives on a SIPSorcery background thread. Nothing in this
/// class touches the UI; it raises events and the view model marshals them.
/// </remarks>
public class CallService(
    SipTransportHost transport,
    BlockListCache blockList,
    ILogger<CallService> logger) : IDisposable
{
    private readonly Lock _gate = new();

    private SIPUserAgent? _agent;
    private SIPServerUserAgent? _pending;
    private VoIPMediaSession? _media;
    private CallState _state = CallState.Idle;

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    public event EventHandler<CallState>? StateChanged;

    public CallState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>
    /// Starts listening for calls. Called once the extension is registered —
    /// before that there is nothing to listen for.
    /// </summary>
    public void Start()
    {
        Stop();

        lock (_gate)
        {
            _agent = new SIPUserAgent(transport.Transport, null);
            _agent.OnIncomingCall += OnIncomingCall;
            _agent.OnCallHungup += OnRemoteHangup;
            _agent.ServerCallCancelled += (_, _) => Finish("the caller gave up");
        }

        logger.LogInformation("Listening for calls");
    }

    /// <summary>Stops listening and ends anything in progress.</summary>
    public void Stop()
    {
        SIPUserAgent? agent;

        lock (_gate)
        {
            agent = _agent;
            _agent = null;
        }

        if (agent is null)
        {
            return;
        }

        try
        {
            agent.Hangup();
            agent.Close();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A call did not end cleanly while stopping");
        }

        CloseMedia();
        Set(CallState.Idle);
    }

    /// <summary>Answers the ringing call (A-12).</summary>
    public async Task AnswerAsync()
    {
        SIPUserAgent? agent;
        SIPServerUserAgent? pending;

        lock (_gate)
        {
            agent = _agent;
            pending = _pending;
        }

        if (agent is null || pending is null)
        {
            return;
        }

        try
        {
            var media = CreateMedia();

            lock (_gate)
            {
                _media = media;
            }

            var answered = await agent.Answer(pending, media);

            if (!answered)
            {
                logger.LogWarning("The call could not be answered");
                Finish("the call could not be answered");
                return;
            }

            lock (_gate)
            {
                _pending = null;
            }

            Set(State with { Status = CallStatus.Connected, ConnectedAt = DateTimeOffset.Now });
            logger.LogInformation("Call answered");
        }
        catch (Exception ex)
        {
            // An audio device that has vanished is the usual cause, and it must
            // not take the app down mid-call.
            logger.LogError(ex, "Answering the call failed");
            Finish("answering failed");
        }
    }

    /// <summary>Rejects the ringing call (A-12). Logged as Rejected (A-14).</summary>
    public void Reject() => RejectWith(SIPResponseStatusCodesEnum.Decline, "rejected by the agent");

    /// <summary>Ends the call in progress (A-12).</summary>
    public void HangUp()
    {
        SIPUserAgent? agent;

        lock (_gate)
        {
            agent = _agent;
        }

        try
        {
            agent?.Hangup();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hanging up did not complete cleanly");
        }

        Finish("hung up by the agent");
    }

    /// <summary>
    /// An incoming INVITE. The block check is the first thing that happens here
    /// and the reason this method is not async before it: A-17 requires no
    /// pop-up and no ringing, so nothing may be shown or played until the
    /// caller has been cleared.
    /// </summary>
    private void OnIncomingCall(SIPUserAgent agent, SIPRequest request)
    {
        var caller = CallerNumberOf(request);

        // A-17: the answer comes from the local cache, so it is immediate and
        // works with the server down.
        if (blockList.IsBlocked(caller))
        {
            logger.LogInformation("Call from {Caller} rejected: the number is blocked (A-17)", caller);

            try
            {
                agent.AcceptCall(request).Reject(SIPResponseStatusCodesEnum.Decline, null, null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A blocked call could not be declined cleanly");
            }

            // Deliberately no state change: nothing rings, nothing is shown, and
            // the agent never learns this happened. Recording it for the
            // supervisor's reports is A-14, with the call logging.
            return;
        }

        if (State.Status is not CallStatus.Idle)
        {
            // One extension, one call. Busy lets the PBX offer the caller to
            // somebody else rather than leaving them ringing at a full agent.
            logger.LogInformation("Call from {Caller} refused as busy: another call is in progress", caller);

            try
            {
                agent.AcceptCall(request).Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A second call could not be refused cleanly");
            }

            return;
        }

        var uas = agent.AcceptCall(request);

        lock (_gate)
        {
            _pending = uas;
        }

        // 180 Ringing: the caller hears ringback rather than silence while the
        // agent decides.
        try
        {
            uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send ringing to the caller");
        }

        logger.LogInformation("Incoming call from {Caller}", caller);

        Set(new CallState(CallStatus.Ringing, caller, DateTimeOffset.Now, null));
    }

    private void OnRemoteHangup(SIPDialogue? dialogue) => Finish("the other party hung up");

    private void RejectWith(SIPResponseStatusCodesEnum status, string why)
    {
        SIPServerUserAgent? pending;

        lock (_gate)
        {
            pending = _pending;
            _pending = null;
        }

        try
        {
            pending?.Reject(status, null, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The call could not be rejected cleanly");
        }

        logger.LogInformation("Call ended: {Why}", why);
        Finish(why);
    }

    /// <summary>
    /// The caller's number as the PBX presented it. The user part of the From
    /// header — the display name is whatever the PBX felt like sending and is
    /// never what matching uses (A-13).
    /// </summary>
    private static string? CallerNumberOf(SIPRequest request) =>
        request.Header.From?.FromURI?.User;

    /// <summary>
    /// The microphone and speaker, as a media session. Created per call rather
    /// than held open, so the app does not sit on the microphone between calls —
    /// on a shared laptop that is both rude and a privacy question.
    /// </summary>
    private VoIPMediaSession CreateMedia()
    {
        var audio = new WindowsAudioEndPoint(new AudioEncoder());

        var media = new VoIPMediaSession(new MediaEndPoints
        {
            AudioSource = audio,
            AudioSink = audio,
        });

        // The PBX and the media may come from different addresses across the
        // VPN, and refusing the audio because it did not arrive from exactly
        // where the SDP said would be a call with no sound.
        media.AcceptRtpFromAny = true;

        return media;
    }

    private void CloseMedia()
    {
        VoIPMediaSession? media;

        lock (_gate)
        {
            media = _media;
            _media = null;
        }

        try
        {
            media?.Close("call ended");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The audio session did not close cleanly");
        }
    }

    private void Finish(string why)
    {
        lock (_gate)
        {
            _pending = null;
        }

        CloseMedia();

        if (State.Status is not CallStatus.Idle)
        {
            logger.LogInformation("Call finished: {Why}", why);
        }

        Set(CallState.Idle);
    }

    private void Set(CallState state)
    {
        lock (_gate)
        {
            _state = state;
        }

        StateChanged?.Invoke(this, state);
    }

    public void Dispose() => Stop();
}
