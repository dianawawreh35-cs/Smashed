using System.Diagnostics;
using System.IO;
using CallCenter.AgentApp.Audio;
using CallCenter.AgentApp.Services.Sip;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Phone;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
/// <b>The one exception is a call the agent places while another is on hold
/// (A-24).</b> The held call is parked — its user agent, audio and recorder
/// set aside untouched — and the new call gets a user agent of its own. When
/// the new call ends, the held one comes back to the front, still on hold, for
/// the agent to resume. Each <see cref="SIPUserAgent"/> answers only for its
/// own dialogue, so the two share the transport without seeing each other's
/// traffic. Only one call is ever parked.
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
    PhonePreferences preferences,
    IOptions<DialingOptions> dialing,
    IOptions<RecordingOptions> recording,
    IOptions<AudioOptions> audioOptions,
    RingbackTone ringback,
    RingTone ring,
    ILoggerFactory loggerFactory,
    ILogger<CallService> logger) : IDisposable
{
    private readonly Lock _gate = new();

    /// <summary>
    /// The user agent that listens for incoming calls. It is the one every
    /// ordinary call uses, in and out.
    /// </summary>
    private SIPUserAgent? _listener;

    /// <summary>
    /// The user agent of the call at the front: <see cref="_listener"/>, or a
    /// user agent made for a call placed while another is on hold (A-24).
    /// </summary>
    private SIPUserAgent? _agent;

    /// <summary>
    /// The call parked on hold while the agent makes another (A-24), or null.
    /// </summary>
    private HeldLine? _held;

    private SIPServerUserAgent? _pending;
    private VoIPMediaSession? _media;

    /// <summary>
    /// The microphone and speaker of the call in progress. Kept beside the
    /// media session because Mute (A-12) is done here — pausing the source —
    /// and not at the SIP level, which has no notion of mute at all.
    /// </summary>
    private IAudioSource? _audio;
    private CallState _state = CallState.Idle;

    /// <summary>
    /// Where the PBX is and who this extension says it is, for placing calls
    /// (A-20). Taking a call needs none of this — the PBX has already
    /// authenticated us by then — but making one does: Asterisk challenges an
    /// outgoing INVITE exactly as it challenges a REGISTER.
    /// </summary>
    private AgentExtensionsDto? _extensions;

    /// <summary>
    /// Why the last outgoing call attempt failed, as the PBX put it. Read once
    /// the call returns, to tell "nobody picked up" from "that number does not
    /// work" — two things an agent does completely different things about.
    /// </summary>
    private SIPResponseStatusCodesEnum? _lastDialFailure;

    /// <summary>
    /// The recorder for the call in progress (A-30), or null when the call is
    /// not being recorded — which includes every case where recording failed,
    /// because A-32 says a recording problem must never reach the call.
    /// </summary>
    private CallRecorder? _recorder;

    /// <summary>
    /// The Call-ID of the call in progress, so a re-INVITE for it can be told
    /// apart from a genuine second call.
    /// </summary>
    private string? _callId;

    /// <summary>
    /// Which call is at the front, as a number that only ever goes up (F-09).
    /// </summary>
    /// <remarks>
    /// A new number for every call that rings or is dialled, and for the front
    /// becoming empty again. Everything that waits — answering, dialling,
    /// building the media — takes the number when it starts and changes the
    /// state only if it is still the same when it finishes. Without it a BYE
    /// landing during the answer had already ended the call, and the answer
    /// then built "Connected" out of the idle state: the pop-up went, the
    /// service stayed Connected, and every later call was answered 486 Busy.
    /// The Call-ID cannot do this job: a call being dialled has none until it
    /// is answered, and the listener's user agent is the same object for every
    /// call. A call parked on hold (A-24) keeps its number and gets it back.
    /// </remarks>
    private long _generation;

    /// <summary>
    /// The last number handed out. Separate from <see cref="_generation"/>,
    /// which goes back to a parked call's number when it returns (A-24): one
    /// counter would then hand out a number some finished call still holds.
    /// </summary>
    private long _lastGeneration;

    /// <summary>
    /// The call this app is hanging up, by <see cref="_generation"/>, or 0.
    /// </summary>
    /// <remarks>
    /// <see cref="SIPUserAgent.OnCallHungup"/> fires for <b>both</b> ends: it is
    /// raised from inside <c>Hangup()</c> as well as when a BYE arrives. Without
    /// this every agent-initiated hang-up was logged as "the other party hung
    /// up", which is exactly backwards and is the sort of line somebody spends
    /// an hour believing while debugging something else.
    ///
    /// A call number rather than a flag (F-09): the flag was set by Cancel
    /// during dialling and never cleared, so if the customer answered as the
    /// agent gave up, their later BYE was ignored and the phone stayed busy.
    /// A number stops matching the moment the call it names is over.
    /// </remarks>
    private long _hangingUp;

    /// <summary>
    /// The call the agent gave up on while it was still being set up, by
    /// <see cref="_generation"/>, or 0. Read before the INVITE goes, so a
    /// Cancel pressed while the microphone was opening rings nobody.
    /// </summary>
    private long _cancelledDial;

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    public event EventHandler<CallState>? StateChanged;

    /// <summary>
    /// Raised once per call, when it is over, for the record the supervisor's
    /// reports are built from (A-14). Raised for blocked and busy calls too,
    /// which never appear on screen.
    /// </summary>
    public event EventHandler<FinishedCall>? CallFinished;

    /// <summary>
    /// Raised when a call that was recorded has a finished file waiting to go
    /// to the server (A-31).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CallFinished"/> and raised after it, because a
    /// recording can only be attached to a call the server already knows about.
    /// A call with no recording raises nothing, which is what "flagged: no
    /// recording" (A-32) amounts to in practice.
    /// </remarks>
    public event EventHandler<CallRecording>? RecordingReady;

    /// <summary>
    /// Raised when the microphone or speaker of the call in progress fails
    /// (M-A02). Until 27 Sep nothing listened: a call with no headset
    /// connected with no sound, and not a line in the log.
    /// </summary>
    public event EventHandler<AudioFailure>? AudioFailed;

    public CallState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>
    /// Starts listening for calls. Called once the extension is registered —
    /// before that there is nothing to listen for.
    /// </summary>
    public void Start(AgentExtensionsDto? extensions = null)
    {
        Stop();

        lock (_gate)
        {
            _extensions = extensions;

            // Subscribed to the transport before OnTransportRequest below, so
            // an INVITE reaches OnIncomingCall first. The busy check there and
            // the one in OnTransportRequest rely on that order.
            var listener = new SIPUserAgent(transport.Transport, null);
            listener.OnIncomingCall += OnIncomingCall;
            listener.ServerCallCancelled += (_, _) => Finish(listener, "the caller gave up");
            WireCallEvents(listener);

            _listener = listener;
            _agent = listener;

            transport.Transport.SIPTransportRequestReceived += OnTransportRequest;
        }

        logger.LogInformation("Listening for calls");
    }

    /// <summary>
    /// What every user agent needs, whether it listens or only dials out
    /// (A-24): the hang-up, the far end's hold, and the ringing and failure of
    /// a call being placed.
    /// </summary>
    private void WireCallEvents(SIPUserAgent agent)
    {
        agent.OnCallHungup += _ => OnCallHungup(agent);

        // The far end can hold us too - a PBX does it during a transfer.
        // Logged only: A-12 is about the agent's controls, and the pop-up
        // has nothing to offer the agent about a hold they did not choose.
        agent.RemotePutOnHold += () => logger.LogInformation("Put on hold by the other side");
        agent.RemoteTookOffHold += () => logger.LogInformation("Taken off hold by the other side");

        // Outgoing calls (A-20). Ringing is worth a line because it is the
        // proof the PBX accepted the number at all; the failure is kept so
        // the outcome can say which kind of failure it was.
        //
        // It is also when the agent should hear ringing. The PBX sends no
        // early media, so without a local tone the line is silent until
        // the customer answers. A 183 that does carry audio (a body with
        // it) is left alone: the network is already playing something.
        agent.ClientCallRinging += (_, response) =>
        {
            logger.LogInformation("The far end is ringing: {Status}", response?.Status);

            if (string.IsNullOrEmpty(response?.Body))
            {
                ringback.Start();
            }
        };

        agent.ClientCallFailed += (_, error, response) =>
        {
            ringback.Stop();

            lock (_gate)
            {
                _lastDialFailure = response?.Status;
            }

            logger.LogInformation(
                "The outgoing call did not connect: {Status} {Error}", response?.Status, error);
        };
    }

    /// <summary>
    /// Lets go of a user agent made for a call placed while another was on
    /// hold (A-24), once that call is over.
    /// </summary>
    /// <remarks>
    /// Closed at once, so it answers nothing new; disposed — which is what
    /// takes it off the transport — only after half a minute, because a closed
    /// user agent still answers a retransmitted BYE and the PBX may send one.
    /// </remarks>
    private void Retire(SIPUserAgent agent)
    {
        try
        {
            agent.Close();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "A finished call's user agent did not close cleanly");
        }

        _ = Task.Delay(TimeSpan.FromSeconds(32)).ContinueWith(_ =>
        {
            try
            {
                agent.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "A finished call's user agent did not dispose cleanly");
            }
        }, TaskScheduler.Default);
    }

    /// <summary>Stops listening and ends anything in progress.</summary>
    public void Stop()
    {
        SIPUserAgent? listener;

        lock (_gate)
        {
            listener = _listener;
        }

        if (listener is null)
        {
            return;
        }

        transport.Transport.SIPTransportRequestReceived -= OnTransportRequest;
        listener.OnIncomingCall -= OnIncomingCall;

        // A connected call is hung up while everything is still in place, so
        // it is finished and reported like any other hang-up. Twice, because
        // finishing the call at the front brings a parked one forward (A-24).
        for (var i = 0; i < 2; i++)
        {
            SIPUserAgent? current;

            lock (_gate)
            {
                current = _agent;
            }

            try
            {
                current?.Hangup();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A call did not end cleanly while stopping");
            }
        }

        SIPUserAgent? front;
        HeldLine? held;

        lock (_gate)
        {
            front = _agent;
            held = _held;
            _listener = null;
            _agent = null;
            _held = null;
        }

        // The call at the front, a call parked on hold behind it, and the
        // listener, each once.
        foreach (var agent in new[] { front, held?.Agent, listener }.OfType<SIPUserAgent>().Distinct())
        {
            try
            {
                agent.Hangup();
                agent.Close();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A call did not end cleanly while stopping");
            }
        }

        ringback.Stop();
        ring.Stop();
        StopRecording();
        CloseMedia();

        if (held is not null)
        {
            StopRecording(held.Recorder, held.Media, held.Audio);
            CloseMedia(held.Media);
        }

        CallState idle;

        lock (_gate)
        {
            // Anything still waiting on the old line is over too (F-09).
            _generation = ++_lastGeneration;
            _state = CallState.Idle;
            idle = _state;
        }

        StateChanged?.Invoke(this, idle);
    }

    /// <summary>
    /// Places a call (A-20). Returns once it has been answered or has failed.
    /// </summary>
    /// <remarks>
    /// <b>The number is sent as it is held, with only a configured prefix in
    /// front.</b> What the PBX wants dialled is a dialplan question that cannot
    /// be answered from here — see <see cref="DialingOptions"/> — so nothing is
    /// rewritten into international form on the way out. Only the digits are
    /// kept, because a number typed or displayed as <c>059-949-8581</c> is not
    /// something a switch will route.
    ///
    /// <b>One call at a time (SRS 2.3)</b>, except that a call on hold may be
    /// parked to make another (A-24). The slot is claimed before anything slow
    /// happens, so two clicks on a call button cannot both get through.
    ///
    /// The classification form needs the call's SIP Call-ID, and for an outgoing
    /// call that does not exist until the dialogue does — so it is read off the
    /// dialogue at the moment of answer, not before (A-21, A-40).
    /// </remarks>
    /// <param name="isInternal">
    /// An internal call — another agent or a branch (A-23). Dialled without the
    /// prefix, which is the PBX's way to an outside line and would send an
    /// extension number out to the phone network.
    /// </param>
    public async Task DialAsync(string? number, bool isInternal = false)
    {
        var digits = PhoneNormalizer.DigitsOnly(number);

        if (string.IsNullOrEmpty(digits))
        {
            logger.LogInformation("Nothing to dial");
            return;
        }

        SIPUserAgent agent;
        AgentExtensionsDto? extensions;
        CallState? held = null;
        long generation;

        lock (_gate)
        {
            extensions = _extensions;

            if (_listener is null || _agent is null || extensions is null)
            {
                logger.LogWarning("Cannot dial: the phone is not ready");
                return;
            }

            if (!_state.AllowsDialling)
            {
                // One extension, one call, unless that call is on hold. The
                // button is disabled for this, so reaching here means two
                // clicks landed together.
                logger.LogInformation("Cannot dial {Number}: another call is in progress", digits);
                return;
            }

            if (_state.Status is CallStatus.Connected)
            {
                // A-24: the call on hold is parked exactly as it is — still on
                // hold, still recording its hold — and the new call gets a user
                // agent of its own, since the parked one still has its dialogue.
                held = _state;
                _held = new HeldLine(_agent, _media, _audio, _recorder, _callId, _state, _generation);
                _agent = new SIPUserAgent(transport.Transport, null);
                WireCallEvents(_agent);
                _media = null;
                _audio = null;
                _recorder = null;
                _callId = null;

                logger.LogInformation("The call on hold is parked while another call is placed");
            }

            agent = _agent;
            _lastDialFailure = null;
            generation = _generation = ++_lastGeneration;

            // The slot is claimed here, inside the lock, rather than after the
            // media is built: everything below takes long enough for a second
            // click to arrive.
            _state = new CallState(
                CallStatus.Dialling, digits, null, null, DateTimeOffset.Now, null,
                SipCallId: null, IsMuted: false, IsOnHold: false, IsOutbound: true,
                IsInternal: isInternal, IsSecondLine: held is not null, Held: held);
        }

        StateChanged?.Invoke(this, State);

        var dialled = (isInternal ? string.Empty : dialing.Value.Prefix) + digits;
        var destination = $"sip:{dialled}@{extensions.SipServer}";

        logger.LogInformation(
            "Dialling {Destination} ({Kind} call)", destination, isInternal ? "internal" : "outside");

        try
        {
            var (media, audio) = CreateMedia();
            bool current;
            bool cancelled;

            lock (_gate)
            {
                current = generation == _generation;
                cancelled = _cancelledDial == generation;

                if (current && !cancelled)
                {
                    _media = media;
                    _audio = audio;
                }
            }

            if (!current || cancelled)
            {
                // F-09: the agent gave up, or the call was ended, while the
                // microphone was opening. The media is this method's own and
                // nobody else will close it; no INVITE has gone.
                CloseMedia(media);
                Finish(agent, "the agent gave up on the outgoing call", CallOutcome.NoAnswer, generation);
                return;
            }

            var answered = await agent.Call(
                destination,
                extensions.Extension,
                extensions.Secret,
                media,
                dialing.Value.RingTimeoutSeconds);

            if (!answered)
            {
                SIPResponseStatusCodesEnum? failure;

                lock (_gate)
                {
                    failure = _lastDialFailure;
                }

                Finish(agent, Explain(failure), OutcomeFor(failure), generation);
                return;
            }

            // Before anything else: the customer is on the line, and the
            // ringing must not talk over their first word.
            ringback.Stop();

            CallState connected;
            bool ended;

            lock (_gate)
            {
                // F-09: only the call this was dialled for, and only if it is
                // still being dialled. A BYE that landed while Call() was
                // returning has finished and reported it already, and building
                // Connected now would leave the phone busy for good.
                ended = generation != _generation || _state.Status is not CallStatus.Dialling;
                connected = _state;

                if (!ended)
                {
                    // The dialogue exists now, and with it the call's own
                    // reference. The classification form is keyed on this
                    // (A-40), so without it the call would work and be
                    // unclassifiable.
                    _callId = agent.Dialogue?.CallId;

                    _state = _state with
                    {
                        Status = CallStatus.Connected,
                        ConnectedAt = DateTimeOffset.Now,
                        SipCallId = _callId,
                        Held = _held?.State,
                    };

                    connected = _state;
                }
            }

            if (ended)
            {
                logger.LogInformation("The outgoing call ended as it was answered; it stays ended");
                return;
            }

            StartRecording(generation, media, audio);
            StateChanged?.Invoke(this, connected);

            logger.LogInformation("Outgoing call answered");
        }
        catch (Exception ex)
        {
            // A missing microphone is the usual cause, and it must not take the
            // app down.
            logger.LogError(ex, "The call to {Number} could not be placed", digits);
            Finish(agent, "the call could not be placed", CallOutcome.Failed, generation);
        }
    }

    /// <summary>
    /// What a failed dial attempt should be recorded as. The question it answers
    /// is whether trying again is worth anything.
    /// </summary>
    private static CallOutcome OutcomeFor(SIPResponseStatusCodesEnum? status) => status switch
    {
        // The far end was reached and did not take the call. Ringing out, busy,
        // declined, or the agent giving up - all worth another try later.
        SIPResponseStatusCodesEnum.BusyHere => CallOutcome.NoAnswer,
        SIPResponseStatusCodesEnum.BusyEverywhere => CallOutcome.NoAnswer,
        SIPResponseStatusCodesEnum.TemporarilyUnavailable => CallOutcome.NoAnswer,
        SIPResponseStatusCodesEnum.RequestTimeout => CallOutcome.NoAnswer,
        SIPResponseStatusCodesEnum.RequestTerminated => CallOutcome.NoAnswer,
        SIPResponseStatusCodesEnum.Decline => CallOutcome.NoAnswer,

        // Nothing came back at all: the ring timeout ran out. Nobody picked up.
        null => CallOutcome.NoAnswer,

        // Anything else is the number or the PBX, and repeating it will repeat
        // the result until somebody looks at it.
        _ => CallOutcome.Failed,
    };

    private static string Explain(SIPResponseStatusCodesEnum? status) => status switch
    {
        null => "nobody answered",
        SIPResponseStatusCodesEnum.BusyHere => "the line was busy",
        SIPResponseStatusCodesEnum.BusyEverywhere => "the line was busy",
        SIPResponseStatusCodesEnum.TemporarilyUnavailable => "the number was unavailable",
        SIPResponseStatusCodesEnum.NotFound => "the PBX does not know that number",
        _ => $"the PBX answered {status}",
    };

    /// <summary>Answers the ringing call (A-12).</summary>
    public async Task AnswerAsync()
    {
        SIPUserAgent? agent;
        SIPServerUserAgent? pending;
        long generation;
        bool ringing;

        lock (_gate)
        {
            agent = _agent;
            pending = _pending;
            generation = _generation;
            ringing = _state.Status is CallStatus.Ringing;
        }

        if (agent is null || pending is null || !ringing)
        {
            return;
        }

        var clock = Stopwatch.StartNew();

        // On the click, not when the line opens: the agent has answered, and
        // a ring that carried on while the audio started sounded like a
        // second call.
        ring.Stop();

        try
        {
            // The media was prepared while the call rang (see PrepareMedia),
            // so the click has nothing to build. Built here only if the
            // preparation has not finished yet.
            VoIPMediaSession? media;
            IAudioSource? audio;

            lock (_gate)
            {
                media = _media;
                audio = _audio;
            }

            var prepared = media is not null && audio is not null;

            if (!prepared)
            {
                var (built, builtAudio) = CreateMedia();
                bool keep;
                bool gone;

                lock (_gate)
                {
                    // F-09: with auto answer, this and PrepareMedia build at
                    // the same moment. Whichever gets here first is used; the
                    // other is closed, rather than overwritten and left holding
                    // an audio device until the process ends.
                    gone = generation != _generation;
                    keep = !gone && _media is null;

                    if (keep)
                    {
                        _media = built;
                        _audio = builtAudio;
                    }

                    media = _media;
                    audio = _audio;
                }

                if (!keep)
                {
                    CloseMedia(built);
                }

                if (gone)
                {
                    // The caller gave up while the microphone was opening. What
                    // is in _media now, if anything, is the next call's.
                    return;
                }
            }

            if (media is null || audio is null)
            {
                // The caller gave up while the microphone was opening, and the
                // call's media went with them.
                return;
            }

            var mediaReadyAt = clock.ElapsedMilliseconds;

            var answered = await agent.Answer(pending, media);

            var answeredAt = clock.ElapsedMilliseconds;

            if (!answered)
            {
                logger.LogWarning("The call could not be answered");
                Finish(agent, "the call could not be answered", generation: generation);
                return;
            }

            CallState connected;
            bool ended;

            lock (_gate)
            {
                // F-09: the same rule as for a dialled call. A caller who hung
                // up while the answer was on its way has been finished and
                // reported as missed; Connected must not be built on top.
                ended = generation != _generation || _state.Status is not CallStatus.Ringing;

                if (!ended)
                {
                    _pending = null;
                    _state = _state with { Status = CallStatus.Connected, ConnectedAt = DateTimeOffset.Now };
                }

                connected = _state;
            }

            if (ended)
            {
                logger.LogInformation("The caller hung up as the call was answered; it stays ended");
                return;
            }

            StartRecording(generation, media, audio);
            StateChanged?.Invoke(this, connected);

            // The timings are the point of this line: "Answer takes a moment"
            // can only be fixed once it says which moment.
            logger.LogInformation(
                "Call answered in {Total} ms (media {Media} ms, prepared while ringing: {Prepared}; SIP answer {Sip} ms; recording {Rec} ms)",
                clock.ElapsedMilliseconds, mediaReadyAt, prepared, answeredAt - mediaReadyAt,
                clock.ElapsedMilliseconds - answeredAt);
        }
        catch (Exception ex)
        {
            // An audio device that has vanished is the usual cause, and it must
            // not take the app down mid-call.
            logger.LogError(ex, "Answering the call failed");
            Finish(agent, "answering failed", generation: generation);
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
        CallState state;
        long generation;

        lock (_gate)
        {
            agent = _agent;
            state = _state;
            generation = _generation;

            if (state.Status is CallStatus.Connected)
            {
                _hangingUp = generation;
            }
            else if (state.Status is CallStatus.Dialling)
            {
                // F-09: no "hanging up" mark for a call being dialled. There
                // is no BYE of ours to tell apart, and the mark used to outlive
                // the call: a customer answering as the agent gave up had
                // their BYE ignored. If the answer crosses the Cancel, the
                // call connects and the agent has Hang up in front of them.
                _cancelledDial = generation;
            }
        }

        // Nothing to hang up on: no call, or one still ringing, which is
        // Reject's to end. A keyboard shortcut can arrive in either (M-A05).
        if (state.Status is CallStatus.Idle or CallStatus.Ringing)
        {
            return;
        }

        // A call that is still being placed has no dialogue to end, so there is
        // no BYE to send: it is cancelled instead. Hangup() is documented as
        // ending an *established* call, and calling it here would leave the PBX
        // ringing a customer nobody is waiting for.
        if (state.Status is CallStatus.Dialling)
        {
            try
            {
                agent?.Cancel();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The outgoing call did not cancel cleanly");
            }

            // DialAsync is still waiting on Call(), and finishes the call when
            // it returns. Finishing here as well would report it twice.
            logger.LogInformation("The agent gave up on the outgoing call");
            return;
        }

        try
        {
            agent?.Hangup();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hanging up did not complete cleanly");
        }

        Finish(agent, "hung up by the agent", CallOutcome.Answered, generation);
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
        IAudioSource? audio;
        CallState state;
        long generation;

        lock (_gate)
        {
            audio = _audio;
            state = _state;
            generation = _generation;
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
        Change(generation, s => s with { IsMuted = mute });
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
        CallRecorder? recorder;
        long generation;

        lock (_gate)
        {
            agent = _agent;
            state = _state;
            recorder = _recorder;
            generation = _generation;
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

        // And the speaker: Asterisk keeps sending the customer's voice through
        // a hold, and A-12 says the agent does not hear them.
        SetSpeakerSilenced(hold);

        // A-51: the recording notes where the hold is. The PBX plays its music
        // to the customer, not to us, so without this the stretch is just
        // silence on both sides.
        recorder?.MarkHold(hold);

        logger.LogInformation("Call {Action}", hold ? "put on hold" : "taken off hold");
        Change(generation, s => s with { IsOnHold = hold });
    }

    /// <summary>
    /// Stops or restarts the customer's voice reaching the speaker, for a hold
    /// (A-12). Only the call at the front is touched: a call parked on hold
    /// (A-24) stays silenced until it comes back and is resumed.
    /// </summary>
    private void SetSpeakerSilenced(bool silenced)
    {
        VoIPMediaSession? media;

        lock (_gate)
        {
            media = _media;
        }

        if (media?.Media?.AudioSink is HoldableAudioSink speaker)
        {
            speaker.IsSilenced = silenced;
        }
        else
        {
            logger.LogWarning("The speaker could not be {Action}", silenced ? "silenced" : "restored");
        }
    }

    /// <summary>
    /// Pauses or resumes the microphone. False if it could not be done, and the
    /// reason is already in the log.
    /// </summary>
    private bool SetMicrophone(bool paused)
    {
        IAudioSource? audio;

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

        string? frontCallId;
        string? heldCallId;

        lock (_gate)
        {
            frontCallId = _callId;
            heldCallId = _held?.CallId;
        }

        if (string.Equals(request.Header.CallId, frontCallId, StringComparison.Ordinal)
            || string.Equals(request.Header.CallId, heldCallId, StringComparison.Ordinal))
        {
            // Same dialogue - a re-INVITE, typically hold or a codec change,
            // for the call at the front or the one parked behind it (A-24).
            return;
        }

        // N-05, guard 4: not this agent's call, so not reported as a busy one.
        if (RefusedAsForAnotherExtension(request))
        {
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
    /// Turns away an INVITE addressed to an extension this app is not signed
    /// in as (N-05, guard 4; see <see cref="CallAddressee"/>). True when it did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing shown, nothing played, and <b>nothing reported</b>: it is not
    /// this agent's call, and filing it under this extension is exactly the
    /// mistake being stopped. A warning in the log, with both extensions, is
    /// the only trace, and it is the one that says the PBX still holds an old
    /// address for this laptop.
    /// </para>
    /// <para>
    /// <b>480 Temporarily Unavailable.</b> Not a 6xx, which tells the PBX to
    /// try nowhere else and would take the call from the whole queue. Not 486
    /// Busy, which is what Do Not Disturb and a second call say, and would show
    /// the other agent as busy on a phone they are not using. Not 404 Not
    /// Found: the extension does exist, only not here, and Asterisk turns a
    /// 404 into "unallocated number", which on a direct dial the caller hears
    /// as a number that does not work. 480 means "reached, but not available
    /// now", which is the truth, and the queue moves on to the next member.
    /// </para>
    /// </remarks>
    private bool RefusedAsForAnotherExtension(SIPRequest request)
    {
        string? mine;

        lock (_gate)
        {
            mine = _extensions?.Extension;
        }

        var requestUriUser = request.URI?.User;
        var toUser = request.Header?.To?.ToURI?.User;

        if (CallAddressee.IsFor(mine, requestUriUser, toUser))
        {
            return false;
        }

        logger.LogWarning(
            "A call for extension {Addressed} arrived here, signed in as {Extension}: refused 480 and not reported (N-05). "
            + "The PBX still has this laptop's address for the other extension. Request-URI {Uri}, To {To}",
            CallAddressee.Named(requestUriUser, toUser), mine, request.URI, toUser);

        Decline(request, SIPResponseStatusCodesEnum.TemporarilyUnavailable, "for another extension");
        return true;
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
        // The listener raises this for any INVITE while it has no dialogue of
        // its own: while it is dialling or ringing, and while a call placed
        // over a parked one (A-24) is up after the parked caller hung up. None
        // of those is free to take a call. OnTransportRequest, which runs
        // after this, turns it away as busy and reports it.
        if (State.Status is not CallStatus.Idle)
        {
            return;
        }

        // N-05, guard 4: before the block list and Do Not Disturb, because a
        // call for another extension is not this agent's to block, refuse or
        // report.
        if (RefusedAsForAnotherExtension(request))
        {
            return;
        }

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

        // A-18: do not disturb, checked here for the same reason as the block
        // list — nothing may ring or appear on screen before the answer is
        // known.
        if (preferences.DoNotDisturb)
        {
            logger.LogInformation(
                "Call from {Caller} turned away: do not disturb is on (A-18), queue {Queue}",
                caller ?? "withheld", queue ?? "none");

            // 486 Busy here, not 603 Decline as a block gets: a 6xx tells the
            // PBX to stop trying anywhere, and this agent stepping away must
            // not take the call away from the rest of the queue. 486 means
            // "not this line", which is precisely what is being said.
            Decline(request, SIPResponseStatusCodesEnum.BusyHere, "do not disturb");

            // Not reported (Dia, 1 Oct). The server has no Busy, so a report
            // landed as Missed against an agent who had stepped away, once for
            // every pass of the queue during a break (A-86). The queue moves
            // on: another agent's row counts the customer if they answer, the
            // abandoned-call import (S-55) if nobody does. The log line above
            // is the only trace.
            return;
        }

        // Busy is handled in OnTransportRequest: the user agent never raises
        // this event for a second call.

        var callId = request.Header?.CallId;
        long generation;

        lock (_gate)
        {
            // F-09: the line is claimed here, under the lock, not by the check
            // at the top. An outgoing call started in the same instant would
            // otherwise have its Dialling written over with this Ringing.
            if (_state.Status is not CallStatus.Idle)
            {
                // OnTransportRequest, which runs next, turns it away as busy.
                return;
            }

            generation = _generation = ++_lastGeneration;

            // Set with the claim: OnTransportRequest tells this INVITE from a
            // second call by it. Null-conditional to match the header read
            // above: an INVITE with no header at all would not get this far,
            // but the two should not disagree about whether that is possible.
            _callId = callId;
            _state = new CallState(
                CallStatus.Ringing, caller, identity.DisplayName, queue, DateTimeOffset.Now, null, callId);
        }

        var uas = agent.AcceptCall(request);

        lock (_gate)
        {
            if (generation == _generation)
            {
                _pending = uas;
            }
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
            callId);

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

        StateChanged?.Invoke(this, State);

        // The ring (A-10). The pop-up alone is silent, and an agent looking at
        // another screen or another window never saw it. Not for auto answer,
        // which opens the line in the same instant.
        if (!preferences.AutoAnswer)
        {
            ring.Start();
        }

        // The microphone, speaker and RTP session are built now, while the
        // agent is reading the pop-up, rather than when they press Answer.
        // Off this thread for the same reason as auto answer below.
        _ = Task.Run(() => PrepareMedia(generation));

        // A-18: auto answer. The state goes out first, so the pop-up is already
        // on screen showing who this is by the time the line opens — an agent
        // whose headset simply starts talking still has to see the number.
        //
        // Off this thread: answering opens the microphone and speaker, and this
        // is a SIPSorcery callback that the transport is waiting on.
        if (preferences.AutoAnswer)
        {
            logger.LogInformation("Answering automatically: auto answer is on (A-18)");
            _ = Task.Run(AnswerAsync);
        }
    }

    /// <summary>
    /// The call ended. Raised for a BYE arriving <b>and</b> from inside our own
    /// <c>Hangup()</c>, so the flag decides which it was.
    /// </summary>
    /// <remarks>
    /// Keyed on the user agent it came from, because with a call parked (A-24)
    /// there are two, and a customer who gives up while on hold ends the
    /// parked call, not the one the agent is on.
    /// </remarks>
    private void OnCallHungup(SIPUserAgent agent)
    {
        if (FinishHeld(agent, "the caller on hold hung up"))
        {
            return;
        }

        lock (_gate)
        {
            if (_hangingUp == _generation)
            {
                // Our own Hangup() raised this. HangUp() finishes the call
                // itself, once the BYE is away.
                return;
            }
        }

        Finish(agent, "the caller hung up");
    }

    private void RejectWith(SIPResponseStatusCodesEnum status, string why)
    {
        SIPServerUserAgent? pending;
        SIPUserAgent? agent;
        long generation;

        lock (_gate)
        {
            // Only a ringing call can be rejected. Without this, Reject pressed
            // on a connected call (a shortcut, M-A05) finished it with no BYE,
            // leaving the customer on a line nobody was on.
            if (_state.Status is not CallStatus.Ringing)
            {
                return;
            }

            pending = _pending;
            agent = _agent;
            generation = _generation;
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
        Finish(agent, why, CallOutcome.RejectedByAgent, generation);
    }

    /// <summary>
    /// Starts recording a call that has just been answered (A-30), and taps the
    /// audio in both directions.
    /// </summary>
    /// <remarks>
    /// <b>Answered calls only.</b> A missed, rejected or blocked call has no
    /// conversation to record.
    /// </remarks>
    /// <param name="media">
    /// The customer's voice, as encoded frames off the network. Taken from the
    /// session rather than the speaker so it is what arrived, not what the
    /// laptop managed to play.
    /// </param>
    /// <param name="audio">
    /// The agent's voice. Taken as raw microphone samples, which state their
    /// own rate; the encoded event does not.
    /// </param>
    /// <remarks>
    /// Wrapped end to end. A-32 is the strictest rule in this file: if
    /// recording cannot start, the call still happens and simply has no
    /// recording. The events are only subscribed once a recorder exists, so a
    /// failure here costs nothing per frame afterwards.
    /// </remarks>
    private void StartRecording(long generation, VoIPMediaSession media, IAudioSource audio)
    {
        if (!recording.Value.Enabled)
        {
            return;
        }

        try
        {
            string? callId;

            lock (_gate)
            {
                callId = _callId;
            }

            // Named for the call, not the clock: this is the name the server
            // will attach to the call record, and two calls in the same second
            // would otherwise collide.
            var name = (callId ?? Guid.NewGuid().ToString()).Replace(':', '_');

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            var recorder = CallRecorder.Start(
                recording.Value.Folder, name, loggerFactory.CreateLogger<CallRecorder>());

            if (recorder is null)
            {
                return;
            }

            bool keep;

            lock (_gate)
            {
                // F-09: only onto the call it was started for. A call finished
                // between the answer and here has already stopped whatever
                // recorder it had, and one attached now would never be stopped.
                keep = generation == _generation && _state.Status is CallStatus.Connected && _recorder is null;

                if (keep)
                {
                    _recorder = recorder;
                    media.OnAudioFrameReceived += recorder.WriteRemote;
                    audio.OnAudioSourceEncodedFrameReady += recorder.WriteLocal;
                }
            }

            if (!keep)
            {
                // Nothing was written to it, so stopping it leaves no file.
                recorder.Stop();
                recorder.Dispose();
                return;
            }

            logger.LogInformation("Recording this call to {Path}", recorder.FilePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "This call will not be recorded; the call itself is unaffected");
        }
    }

    /// <summary>
    /// Finishes the recording and hands the file on (A-31). Returns whatever is
    /// worth uploading, or null.
    /// </summary>
    private RecordedCall? StopRecording()
    {
        CallRecorder? recorder;
        IAudioSource? audio;
        VoIPMediaSession? media;

        lock (_gate)
        {
            recorder = _recorder;
            audio = _audio;
            media = _media;
            _recorder = null;
        }

        return StopRecording(recorder, media, audio);
    }

    /// <summary>
    /// Finishes one call's recording, whichever line it was on (A-24).
    /// </summary>
    private RecordedCall? StopRecording(
        CallRecorder? recorder, VoIPMediaSession? media, IAudioSource? audio)
    {
        if (recorder is null)
        {
            return null;
        }

        try
        {
            // Unsubscribed before the file is closed, so a frame arriving
            // during the tear-down cannot be written to a stream that has gone.
            if (media is not null)
            {
                media.OnAudioFrameReceived -= recorder.WriteRemote;
            }

            if (audio is not null)
            {
                audio.OnAudioSourceEncodedFrameReady -= recorder.WriteLocal;
            }

            var recorded = recorder.Stop();
            recorder.Dispose();
            return recorded;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The recording could not be closed cleanly");
            return null;
        }
    }

    /// <summary>
    /// The microphone and speaker, as a media session. Created per call rather
    /// than held open, so the app does not sit on the microphone between calls —
    /// on a shared laptop that is both rude and a privacy question.
    /// </summary>
    /// <summary>
    /// Builds the media for a ringing call ahead of the answer, so pressing
    /// Answer has nothing left to construct.
    /// </summary>
    /// <remarks>
    /// Thrown away untouched if the call has already been answered, rejected
    /// or missed by the time it is ready: <see cref="AnswerAsync"/> builds
    /// its own when it gets there first, and a rejected call's media is closed
    /// by <see cref="Finish"/> like any other.
    /// </remarks>
    private void PrepareMedia(long generation)
    {
        VoIPMediaSession media;
        IAudioSource audio;

        try
        {
            (media, audio) = CreateMedia();
        }
        catch (Exception ex)
        {
            // Not fatal: Answer will try again and report its own failure.
            logger.LogWarning(ex, "The media could not be prepared while ringing");
            return;
        }

        var keep = false;

        lock (_gate)
        {
            if (_state.Status is CallStatus.Ringing && _generation == generation && _media is null)
            {
                _media = media;
                _audio = audio;
                keep = true;
            }
        }

        if (!keep)
        {
            try
            {
                media.Close("not needed");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Prepared media did not close cleanly");
            }
        }
    }

    private (VoIPMediaSession Media, IAudioSource Audio) CreateMedia()
    {
        var endpoint = new WindowsAudioEndPoint(new AudioEncoder());

        // A-87: the microphone goes through the echo canceller, so the customer
        // does not hear their own voice back from the agent's speaker. It falls
        // back to the endpoint's own microphone by itself if it cannot run.
        IAudioSource audio = audioOptions.Value.EchoCancellation
            ? new EchoCancellingMicrophone(
                endpoint, new AudioEncoder(), loggerFactory.CreateLogger<EchoCancellingMicrophone>())
            : endpoint;

        // The speaker goes through a wrapper that can drop the customer's
        // voice for a hold (A-12); see HoldableAudioSink for why the PBX's own
        // hold is not enough.
        var media = new VoIPMediaSession(new MediaEndPoints
        {
            AudioSource = audio,
            AudioSink = new HoldableAudioSink(endpoint),
        });

        // The PBX and the media may come from different addresses across the
        // VPN, and refusing the audio because it did not arrive from exactly
        // where the SDP said would be a call with no sound.
        media.AcceptRtpFromAny = true;

        // M-A02: a headset unplugged, or no device at all. Until 27 Sep this
        // went nowhere, and the call connected in silence.
        audio.OnAudioSourceError += error => OnAudioError(audio, AudioFailure.Microphone, error);
        endpoint.OnAudioSinkError += error => OnAudioError(audio, AudioFailure.Speaker, error);

        return (media, audio);
    }

    /// <summary>
    /// A microphone or speaker failure, logged and passed on, but only for the
    /// call in progress: media thrown away after losing a race (F-09) can still
    /// report a device it never used.
    /// </summary>
    private void OnAudioError(IAudioSource audio, AudioFailure which, string? error)
    {
        bool current;

        lock (_gate)
        {
            current = ReferenceEquals(audio, _audio) || ReferenceEquals(audio, _held?.Audio);
        }

        logger.LogWarning(
            "The {Device} failed{Current}: {Error}",
            which is AudioFailure.Microphone ? "microphone" : "speaker",
            current ? string.Empty : " on media no longer in use",
            error);

        if (!current)
        {
            return;
        }

        try
        {
            AudioFailed?.Invoke(this, which);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An audio failure could not be shown to the agent");
        }
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

        CloseMedia(media);
    }

    private void CloseMedia(VoIPMediaSession? media)
    {
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
    /// <param name="agent">
    /// The user agent the call was on. With a call parked (A-24) the front one
    /// changes when a call ends, so an event arriving late from a call already
    /// finished must not finish whatever has come to the front since.
    /// </param>
    /// <param name="generation">
    /// The call this is meant to finish, when the caller knows it (F-09). The
    /// listener's user agent carries every ordinary call, so the agent alone
    /// cannot tell a late failure from the last call apart from the call
    /// ringing now.
    /// </param>
    private void Finish(SIPUserAgent? agent, string why, CallOutcome? outcome = null, long? generation = null)
    {
        CallState state;
        CallState next;
        string? callId;
        CallRecorder? recorder;
        VoIPMediaSession? media;
        IAudioSource? audio;
        SIPUserAgent? retired = null;

        lock (_gate)
        {
            if (!ReferenceEquals(agent, _agent))
            {
                // A call that is already over, and whose line has gone.
                return;
            }

            if (generation is { } forCall && forCall != _generation)
            {
                // Over already, on this same line; what is at the front now is
                // another call, and not this caller's to end.
                return;
            }

            state = _state;
            callId = _callId;
            recorder = _recorder;
            media = _media;
            audio = _audio;
            _pending = null;

            if (agent is not null && !ReferenceEquals(agent, _listener))
            {
                retired = agent;
            }

            // The call is marked finished HERE, inside the lock that read the
            // state, and not at the end of this method.
            //
            // Two things finish the same call within the same millisecond. An
            // outgoing call that the PBX refuses raises the failure this method
            // was called for *and* a hang-up from the library, and the check
            // below used to let both through: each read "not idle" before
            // either had written "idle". The call was then reported twice, two
            // flushes of the upload queue raced, and the server refused the
            // second copy with a duplicate key - visible as a 500 in the log on
            // 22 September, from a call that had otherwise gone fine.
            //
            // A call parked on hold (A-24) comes back to the front in the same
            // step, still on hold: the agent resumes it when they are ready.
            if (_held is { } held)
            {
                _agent = held.Agent;
                _media = held.Media;
                _audio = held.Audio;
                _recorder = held.Recorder;
                _callId = held.CallId;
                _state = held.State;
                _generation = held.Generation;
                _held = null;
            }
            else
            {
                _agent = _listener;
                _media = null;
                _audio = null;
                _recorder = null;
                _callId = null;
                _state = CallState.Idle;

                // Whatever was still waiting on the call just ended now finds
                // it gone (F-09).
                _generation = ++_lastGeneration;
            }

            next = _state;
        }

        // Whatever ended the call — cancel, failure, a refusal — the ringing
        // ends with it.
        ringback.Stop();
        ring.Stop();

        // Before the media is closed: the tap hangs off the session, and
        // closing that first would take the last frames with it.
        var recorded = StopRecording(recorder, media, audio);

        CloseMedia(media);

        if (retired is not null)
        {
            Retire(retired);
        }

        if (state.Status is CallStatus.Idle)
        {
            // Whoever got here first has already reported this call.
            return;
        }

        if (next.IsActive)
        {
            logger.LogInformation("Call finished: {Why}; the call on hold is back", why);
        }
        else
        {
            logger.LogInformation("Call finished: {Why}", why);
        }

        Report(new FinishedCall(
            callId ?? Guid.NewGuid().ToString(),
            state.Number,
            state.CallerName,
            state.Queue,
            outcome ?? (state.Status is CallStatus.Connected
                ? CallOutcome.Answered
                // An unanswered call this agent placed is a customer who was
                // out, not a call this call centre missed (A-21).
                : state.IsOutbound ? CallOutcome.NoAnswer : CallOutcome.Missed),
            state.StartedAt ?? DateTimeOffset.Now,
            state.ConnectedAt,
            DateTimeOffset.Now,
            state.IsOutbound,
            state.IsInternal,
            state.IsSecondLine));

        // After the call has been reported, never before: a recording can only
        // be attached to a call the server has already been told about (A-31).
        if (recorded is not null && callId is not null)
        {
            Announce(new CallRecording(callId, recorded));
        }

        // The state was set inside the lock above; this is only the event.
        StateChanged?.Invoke(this, next);
    }

    /// <summary>
    /// Ends the call parked on hold (A-24), when the customer gives up waiting
    /// while the agent is on another call. False if <paramref name="agent"/> is
    /// not the parked call's.
    /// </summary>
    /// <remarks>
    /// Reported as answered, which it was. The call at the front carries on
    /// untouched; only the "on hold" line above it goes.
    /// </remarks>
    private bool FinishHeld(SIPUserAgent agent, string why)
    {
        HeldLine held;
        CallState front;
        bool retire;

        lock (_gate)
        {
            if (_held is not { } parked || !ReferenceEquals(parked.Agent, agent))
            {
                return false;
            }

            held = parked;
            retire = !ReferenceEquals(agent, _listener);
            _held = null;
            _state = _state with { Held = null };
            front = _state;
        }

        var recorded = StopRecording(held.Recorder, held.Media, held.Audio);
        CloseMedia(held.Media);

        if (retire)
        {
            Retire(agent);
        }

        logger.LogInformation("Call finished: {Why}", why);

        var state = held.State;

        Report(new FinishedCall(
            held.CallId ?? Guid.NewGuid().ToString(),
            state.Number,
            state.CallerName,
            state.Queue,
            CallOutcome.Answered,
            state.StartedAt ?? DateTimeOffset.Now,
            state.ConnectedAt,
            DateTimeOffset.Now,
            state.IsOutbound,
            state.IsInternal,
            state.IsSecondLine));

        if (recorded is not null && held.CallId is not null)
        {
            Announce(new CallRecording(held.CallId, recorded));
        }

        StateChanged?.Invoke(this, front);
        return true;
    }

    /// <summary>
    /// Hands a finished call to whoever is recording them. Never throws: a
    /// reporting problem must not take the phone down mid-shift.
    /// </summary>
    /// <summary>
    /// Hands a finished recording to whoever uploads it. Never throws, for the
    /// same reason as <see cref="Report"/>.
    /// </summary>
    private void Announce(CallRecording recording)
    {
        try
        {
            RecordingReady?.Invoke(this, recording);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A recording could not be handed on for upload");
        }
    }

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

    /// <summary>
    /// Changes the connected call at the front, for mute and hold (F-09).
    /// </summary>
    /// <remarks>
    /// Applied to the state as it is now, inside the lock, and only if it is
    /// still the call the change was asked for and still connected. The old
    /// way read the state, let go of the lock, and wrote back the copy: a
    /// hang-up in between had its Idle overwritten by a stale Connected.
    /// </remarks>
    private void Change(long generation, Func<CallState, CallState> change)
    {
        CallState state;

        lock (_gate)
        {
            if (generation != _generation || _state.Status is not CallStatus.Connected)
            {
                return;
            }

            // The parked call (A-24) is whatever is parked now: the customer
            // on hold may have hung up since the change was asked for.
            _state = change(_state) with { Held = _held?.State };
            state = _state;
        }

        StateChanged?.Invoke(this, state);
    }

    public void Dispose() => Stop();

    /// <summary>
    /// A call set aside on hold while the agent places another (A-24):
    /// everything that belongs to it, parked untouched until it comes back to
    /// the front.
    /// </summary>
    private sealed record HeldLine(
        SIPUserAgent Agent,
        VoIPMediaSession? Media,
        IAudioSource? Audio,
        CallRecorder? Recorder,
        string? CallId,
        CallState State,
        long Generation);
}

/// <summary>Which half of the headset failed (M-A02).</summary>
public enum AudioFailure
{
    Microphone,
    Speaker,
}
