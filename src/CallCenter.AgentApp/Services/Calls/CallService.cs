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
/// Two things sit at the transport level rather than on the user agent, and
/// both had to be learned the hard way:
///
/// <b>Keep-alives.</b> The PBX sends OPTIONS every few seconds to ask whether
/// this extension is still there. SIPSorcery answers nothing but requests
/// inside an established dialogue, so those go unanswered, the PBX marks the
/// extension unreachable, and <b>inbound calls are never offered at all</b> —
/// while registration keeps succeeding. That is a phone that looks perfectly
/// healthy and never rings.
///
/// <b>Busy.</b> <see cref="SIPUserAgent"/> silently drops an INVITE that
/// arrives while it already has a dialogue, so <c>OnIncomingCall</c> never
/// fires for a second call and the caller would hear nothing until the PBX
/// timed out.
///
/// That path fires less often than it looks. A <b>queue</b> does not offer a
/// call to a member who is already on one, so a second INVITE only arrives on a
/// <b>direct dial</b> to the extension — an internal call from a branch or
/// another agent. A customer who rings while every agent is busy waits in the
/// queue, and this app never hears about them at all: they are captured from
/// the PBX's CDR (SRS S-55), not from here.
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

    /// <summary>
    /// The microphone and speaker of the call in progress. Kept beside the
    /// media session because Mute (A-12) is done here — pausing the source —
    /// and not at the SIP level, which has no notion of mute at all.
    /// </summary>
    private WindowsAudioEndPoint? _audio;
    private CallState _state = CallState.Idle;

    /// <summary>
    /// The Call-ID of the call in progress, so a re-INVITE for it can be told
    /// apart from a genuine second call.
    /// </summary>
    private string? _callId;

    /// <summary>
    /// Set while this app is the one hanging up.
    /// </summary>
    /// <remarks>
    /// <see cref="SIPUserAgent.OnCallHungup"/> fires for <b>both</b> ends: it is
    /// raised from inside <c>Hangup()</c> as well as when a BYE arrives. Without
    /// this flag every agent-initiated hang-up was logged as "the other party
    /// hung up", which is exactly backwards and is the sort of line somebody
    /// spends an hour believing while debugging something else.
    /// </remarks>
    private bool _hangingUpLocally;

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    public event EventHandler<CallState>? StateChanged;

    /// <summary>
    /// Raised once per call, when it is over, for the record the supervisor's
    /// reports are built from (A-14). Raised for blocked and busy calls too,
    /// which never appear on screen.
    /// </summary>
    public event EventHandler<FinishedCall>? CallFinished;

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
            _agent.OnCallHungup += OnCallHungup;
            _agent.ServerCallCancelled += (_, _) => Finish("the caller gave up");

            // The far end can hold us too - a PBX does it during a transfer.
            // Logged only: A-12 is about the agent's controls, and the pop-up
            // has nothing to offer the agent about a hold they did not choose.
            _agent.RemotePutOnHold += () => logger.LogInformation("Put on hold by the other side");
            _agent.RemoteTookOffHold += () => logger.LogInformation("Taken off hold by the other side");

            transport.Transport.SIPTransportRequestReceived += OnTransportRequest;
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

        transport.Transport.SIPTransportRequestReceived -= OnTransportRequest;
        agent.OnIncomingCall -= OnIncomingCall;

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
            var (media, audio) = CreateMedia();

            lock (_gate)
            {
                _media = media;
                _audio = audio;
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
    /// <remarks>
    /// The BYE goes first and the audio is closed afterwards, by
    /// <see cref="Finish"/>. The other order tears down the RTP session while
    /// the BYE is still being built, which is asking for a hang-up that does
    /// not leave cleanly.
    ///
    /// What the <i>caller</i> hears next is the PBX's business, not this app's.
    /// A BYE ends the leg between this extension and the PBX; whether the caller
    /// is then hung up, returned to the queue or sent somewhere else is decided
    /// by what follows <c>Queue()</c> in the dialplan.
    /// </remarks>
    public void HangUp()
    {
        SIPUserAgent? agent;

        lock (_gate)
        {
            agent = _agent;
            _hangingUpLocally = true;
        }

        try
        {
            agent?.Hangup();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hanging up did not complete cleanly");
        }

        Finish("hung up by the agent", CallOutcome.Answered);
    }

    /// <summary>
    /// Mutes or unmutes the microphone (A-12).
    /// </summary>
    /// <remarks>
    /// Pausing the audio source stops it producing samples, so <b>no</b> RTP
    /// leaves the laptop while muted — unlike a desk phone, which keeps sending
    /// frames of silence. Asterisk only minds if its RTP timeout is switched on,
    /// which it is not by default; if a long mute ever drops a call, switch to
    /// sending silence instead. The customer's audio is untouched, so the agent
    /// keeps hearing them.
    ///
    /// Nothing here goes near the SIP dialogue, so a failure is logged and the
    /// state left as it was. Mute must never end a call.
    /// </remarks>
    public void ToggleMute()
    {
        WindowsAudioEndPoint? audio;
        CallState state;

        lock (_gate)
        {
            audio = _audio;
            state = _state;
        }

        if (audio is null || state.Status is not CallStatus.Connected)
        {
            return;
        }

        var mute = !state.IsMuted;

        // On hold the microphone is paused regardless, so only the flag moves;
        // Resume reads it to decide whether the microphone comes back.
        if (!state.IsOnHold && !SetMicrophone(paused: mute))
        {
            return;
        }

        logger.LogInformation("Microphone {Action}", mute ? "muted" : "unmuted");
        Set(State with { IsMuted = mute });
    }

    /// <summary>
    /// Puts the customer on hold, or takes them off it (A-12).
    /// </summary>
    /// <remarks>
    /// Hold is the first thing in this app to change a call after it is
    /// answered. It is a <b>re-INVITE</b>: a second INVITE inside the same
    /// dialogue, carrying an SDP marked <c>a=sendonly</c>, which is the standard
    /// way (RFC 3264) a phone says "I will send but not receive". Asterisk reads
    /// that as hold, plays its hold music to the customer and stops sending us
    /// their voice. Taking off hold is another re-INVITE with <c>a=sendrecv</c>.
    /// SIPSorcery does both in <see cref="SIPUserAgent.PutOnHold"/> and
    /// <see cref="SIPUserAgent.TakeOffHold"/>.
    ///
    /// <b>The microphone is paused as well.</b> <c>sendonly</c> still permits
    /// sending, and SIPSorcery keeps transmitting the microphone; Asterisk
    /// ignores it, but there is no reason for the room's audio to leave the
    /// laptop while the agent thinks nobody can hear them. On resume the
    /// microphone comes back only if the agent had not muted before the hold.
    ///
    /// <b>The PBX's answer arrives later.</b> The re-INVITE is sent here and its
    /// response handled inside SIPSorcery on another thread, so this method
    /// cannot know whether the PBX accepted. A refusal (488) is logged by the
    /// library and the call carries on as it was; the state shown here would
    /// then be wrong until Resume is pressed. Accepted, because hold is in the
    /// SIP standard and a PBX that refuses it is misconfigured - the log is
    /// where that would be found.
    ///
    /// Same rule as mute: a failure is logged and the state left alone. Hold
    /// must never end a call.
    /// </remarks>
    public void ToggleHold()
    {
        SIPUserAgent? agent;
        CallState state;

        lock (_gate)
        {
            agent = _agent;
            state = _state;
        }

        if (agent is null || state.Status is not CallStatus.Connected)
        {
            return;
        }

        var hold = !state.IsOnHold;

        try
        {
            if (hold)
            {
                agent.PutOnHold();
            }
            else
            {
                agent.TakeOffHold();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The call could not be {Action}", hold ? "put on hold" : "taken off hold");
            return;
        }

        // The re-INVITE is away; the microphone follows. A microphone failure
        // here is logged inside SetMicrophone and does not undo the hold: the
        // customer is on hold music either way, and that is what the agent
        // asked for.
        SetMicrophone(paused: hold || state.IsMuted);

        logger.LogInformation("Call {Action}", hold ? "put on hold" : "taken off hold");
        Set(State with { IsOnHold = hold });
    }

    /// <summary>
    /// Pauses or resumes the microphone. False if it could not be done, and the
    /// reason is already in the log.
    /// </summary>
    private bool SetMicrophone(bool paused)
    {
        WindowsAudioEndPoint? audio;

        lock (_gate)
        {
            audio = _audio;
        }

        if (audio is null)
        {
            return false;
        }

        try
        {
            if (paused)
            {
                audio.PauseAudio().GetAwaiter().GetResult();
            }
            else
            {
                audio.ResumeAudio().GetAwaiter().GetResult();
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The microphone could not be {Action}", paused ? "paused" : "resumed");
            return false;
        }
    }

    /// <summary>
    /// Everything the PBX sends us, before the user agent sees it. Two jobs, and
    /// everything else is left alone — see the class remarks for why both have
    /// to be here.
    /// </summary>
    private async Task OnTransportRequest(
        SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, SIPRequest request)
    {
        // "Are you still there?" Answering is what keeps this extension
        // reachable, and therefore what makes inbound calls happen at all.
        if (request.Method is SIPMethodsEnum.OPTIONS or SIPMethodsEnum.NOTIFY)
        {
            await AnswerKeepAliveAsync(remoteEndPoint, request);
            return;
        }

        if (request.Method != SIPMethodsEnum.INVITE)
        {
            return;
        }

        // An attended-transfer INVITE carries Replaces and belongs to the user
        // agent, not here.
        if (!string.IsNullOrWhiteSpace(request.Header.Replaces))
        {
            return;
        }

        if (State.Status is CallStatus.Idle)
        {
            // Nothing in progress: the user agent will raise OnIncomingCall, and
            // the block check happens there.
            return;
        }

        if (string.Equals(request.Header.CallId, _callId, StringComparison.Ordinal))
        {
            // Same dialogue - a re-INVITE, typically hold or a codec change.
            return;
        }

        // One extension, one call. In practice this is a direct dial: a queue
        // skips a member who is already talking, so a second INVITE means
        // somebody rang the extension itself.
        logger.LogInformation(
            "Second call from {Remote} refused as busy: another call is in progress", remoteEndPoint);

        Decline(request, SIPResponseStatusCodesEnum.BusyHere, "busy");

        // Reported as Missed: from the caller's side that is what it was. Note
        // this counts direct dials only, not customers waiting in a queue -
        // those never reach this app and come from the CDR import (S-55).
        var identity = CallerId.FromInvite(request);
        var now = DateTimeOffset.Now;

        Report(new FinishedCall(
            request.Header?.CallId ?? Guid.NewGuid().ToString(),
            identity.Number, identity.DisplayName, SipCustomHeaders.QueueFrom(request),
            CallOutcome.Busy, now, null, now));
    }

    /// <summary>
    /// Answers an INVITE with one final response and nothing before it.
    /// </summary>
    /// <remarks>
    /// The point is what it does <b>not</b> send. <c>AcceptCall</c> sends 100
    /// Trying and 180 Ringing before handing back a server user agent, so
    /// declining through it means the caller hears ringback first and is then
    /// cut off. For a blocked caller that breaks the "no ringing" half of A-17;
    /// for a busy agent it wastes the caller's time before the PBX can move
    /// them on. Sending the final response straight into a new transaction
    /// skips the provisional responses entirely.
    ///
    /// <b>603 Decline, not 486 Busy, for a block.</b> 6xx is a global failure:
    /// RFC 3261 has a proxy stop trying other branches and pass it upstream,
    /// which is the closest a phone can get to "hang up on this caller".
    /// 486 means "this one line is busy" and invites the PBX to try elsewhere,
    /// which is right for a second call and wrong for a blocked one.
    /// </remarks>
    private void Decline(SIPRequest request, SIPResponseStatusCodesEnum status, string why)
    {
        try
        {
            var response = SIPResponse.GetResponse(request, status, null);
            new UASInviteTransaction(transport.Transport, request, null).SendFinalResponse(response);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A {Why} call could not be turned away cleanly", why);
        }
    }

    /// <summary>
    /// 200 OK to an OPTIONS or NOTIFY keep-alive.
    /// </summary>
    /// <remarks>
    /// <c>Allow</c> is advertised rather than left out: some switches will not
    /// offer a call to an extension that has not said what it can do.
    /// </remarks>
    private async Task AnswerKeepAliveAsync(SIPEndPoint remoteEndPoint, SIPRequest request)
    {
        try
        {
            var ok = SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Ok, null);
            ok.Header.Allow = "INVITE, ACK, CANCEL, BYE, OPTIONS, NOTIFY, INFO";
            await transport.Transport.SendResponseAsync(ok);
        }
        catch (Exception ex)
        {
            // A missed keep-alive reply is worth a line and must never take the
            // phone down.
            logger.LogWarning(ex, "Could not answer {Method} from {Remote}", request.Method, remoteEndPoint);
        }
    }

    /// <summary>
    /// An incoming INVITE. The block check is the first thing that happens here
    /// and the reason this method is not async before it: A-17 requires no
    /// pop-up and no ringing, so nothing may be shown or played until the
    /// caller has been cleared.
    /// </summary>
    private void OnIncomingCall(SIPUserAgent agent, SIPRequest request)
    {
        var identity = CallerId.FromInvite(request);
        var caller = identity.Number;
        var queue = SipCustomHeaders.QueueFrom(request);

        // A-17: the answer comes from the local cache, so it is immediate and
        // works with the server down.
        if (blockList.IsBlocked(caller))
        {
            logger.LogInformation(
                "Call from {Caller} rejected: the number is blocked (A-17), queue {Queue}",
                caller, queue ?? "none");

            // 603 Decline, and nothing before it. NOT AcceptCall().Reject():
            // AcceptCall sends 100 Trying and 180 Ringing first, so the caller
            // would hear ringback before being turned away — which is the
            // "no ringing" half of A-17 broken, and what the caller experiences
            // as being left hanging.
            Decline(request, SIPResponseStatusCodesEnum.Decline, "blocked");

            // No state change: nothing is shown and the agent never learns this
            // happened. It is still reported, because A-17 requires a blocked
            // call to appear in the supervisor's reports.
            var now = DateTimeOffset.Now;
            Report(new FinishedCall(
                request.Header?.CallId ?? Guid.NewGuid().ToString(),
                caller, identity.DisplayName, queue, CallOutcome.Blocked, now, null, now));

            return;
        }

        // Busy is handled in OnTransportRequest: the user agent never raises
        // this event for a second call.

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

        logger.LogInformation(
            "Incoming call from {Caller} ({Name}) via queue {Queue}, Call-ID {CallId}",
            caller ?? "withheld", identity.DisplayName ?? "no name", queue ?? "none",
            request.Header.CallId);

        // When no queue came through, say what the INVITE actually carried.
        // Otherwise a dialplan that never set the header and a header this app
        // failed to read look identical in the log, and they need opposite fixes.
        if (queue is null)
        {
            var extra = request.Header?.UnknownHeaders;
            logger.LogInformation(
                "No {Header} on this INVITE. Custom headers present: {Headers}",
                SipCustomHeaders.QueueName,
                extra is null || extra.Count == 0 ? "(none)" : string.Join(" | ", extra));
        }

        lock (_gate)
        {
            // Null-conditional to match the header read above: an INVITE with no
            // header at all would not get this far, but the two should not
            // disagree about whether that is possible.
            _callId = request.Header?.CallId;
        }

        Set(new CallState(
            CallStatus.Ringing, caller, identity.DisplayName, queue, DateTimeOffset.Now, null,
            _callId));
    }

    /// <summary>
    /// The call ended. Raised for a BYE arriving <b>and</b> from inside our own
    /// <c>Hangup()</c>, so the flag decides which it was.
    /// </summary>
    private void OnCallHungup(SIPDialogue? dialogue)
    {
        lock (_gate)
        {
            if (_hangingUpLocally)
            {
                // Our own Hangup() raised this. HangUp() finishes the call
                // itself, once the BYE is away.
                return;
            }
        }

        Finish("the caller hung up");
    }

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
        Finish(why, CallOutcome.RejectedByAgent);
    }

    /// <summary>
    /// The microphone and speaker, as a media session. Created per call rather
    /// than held open, so the app does not sit on the microphone between calls —
    /// on a shared laptop that is both rude and a privacy question.
    /// </summary>
    private (VoIPMediaSession Media, WindowsAudioEndPoint Audio) CreateMedia()
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

        return (media, audio);
    }

    private void CloseMedia()
    {
        VoIPMediaSession? media;

        lock (_gate)
        {
            media = _media;
            _media = null;
            // Closing the session closes the endpoint with it; the reference
            // only needs dropping so a stale mute cannot outlive the call.
            _audio = null;
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

    /// <summary>
    /// Ends the call in progress and reports it (A-14).
    /// </summary>
    /// <param name="outcome">
    /// Null means "work it out from the state": a call that was connected was
    /// answered, one that was only ringing was missed. Callers that know better
    /// — the agent pressing Reject — say so.
    /// </param>
    private void Finish(string why, CallOutcome? outcome = null)
    {
        CallState state;
        string? callId;

        lock (_gate)
        {
            state = _state;
            callId = _callId;
            _pending = null;
            _callId = null;
            _hangingUpLocally = false;
        }

        CloseMedia();

        if (state.Status is CallStatus.Idle)
        {
            // Nothing was in progress - a second Finish for the same call, which
            // happens when both ends hang up at once. Reporting twice would be
            // harmless, but there is nothing to report.
            Set(CallState.Idle);
            return;
        }

        logger.LogInformation("Call finished: {Why}", why);

        Report(new FinishedCall(
            callId ?? Guid.NewGuid().ToString(),
            state.Number,
            state.CallerName,
            state.Queue,
            outcome ?? (state.Status is CallStatus.Connected ? CallOutcome.Answered : CallOutcome.Missed),
            state.StartedAt ?? DateTimeOffset.Now,
            state.ConnectedAt,
            DateTimeOffset.Now));

        Set(CallState.Idle);
    }

    /// <summary>
    /// Hands a finished call to whoever is recording them. Never throws: a
    /// reporting problem must not take the phone down mid-shift.
    /// </summary>
    private void Report(FinishedCall call)
    {
        try
        {
            CallFinished?.Invoke(this, call);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A finished call could not be reported");
        }
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
