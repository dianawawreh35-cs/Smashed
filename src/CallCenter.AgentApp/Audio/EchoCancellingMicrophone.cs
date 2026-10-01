using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;

namespace CallCenter.AgentApp.Audio;

/// <summary>
/// The microphone of a call with the speaker's sound taken out of it, so the
/// customer does not hear their own voice coming back (A-87).
/// </summary>
/// <remarks>
/// <b>Why customers heard themselves.</b> The agent's microphone picks up the
/// customer's voice from the speaker or the headset and sends it straight back,
/// about a quarter of a second late. Measured in the recordings of 1 Oct 2026:
/// on one call it was there through the whole conversation.
///
/// <b>How.</b> The audio comes from <see cref="VoiceCaptureDsp"/> rather than from
/// <see cref="WindowsAudioEndPoint"/>'s own capture; everything else (the
/// encoding, the 20 ms packets, the events the media session and the recorder
/// listen to) is as the plain microphone does it, so the rest of the app cannot
/// tell the two apart.
///
/// <b>Never worse than without it.</b> If the canceller cannot start — an old
/// or unusual audio driver, a codec at a rate it cannot deliver — or stops or
/// goes quiet partway through a call, the plain microphone of the same endpoint takes
/// over, and the call carries on as it would have before A-87. That is logged,
/// and the agent is not interrupted.
/// </remarks>
public sealed class EchoCancellingMicrophone : IAudioSource
{
    /// <summary>One RTP packet's worth of audio, as the plain microphone sends.</summary>
    private const int FrameMilliseconds = 20;

    /// <summary>
    /// How long the canceller may take to deliver its first sound. The media
    /// session starts the microphone a moment before the speaker, and the
    /// canceller says nothing until the speaker plays.
    /// </summary>
    private const int FirstSoundMilliseconds = 1500;

    /// <summary>
    /// How long it may then go without delivering before the plain microphone
    /// takes over. It delivers every 10 ms when well; half a second of nothing
    /// means the agent's voice has stopped reaching the customer.
    /// </summary>
    private const int QuietMilliseconds = 500;

    private readonly WindowsAudioEndPoint _plain;
    private readonly IAudioEncoder _encoder;
    private readonly MediaFormatManager<AudioFormat> _formats;
    private readonly ILogger<EchoCancellingMicrophone> _logger;
    private readonly Lock _gate = new();

    /// <summary>Samples from the canceller that do not yet make a whole packet.</summary>
    private readonly List<short> _pending = [];

    private VoiceCaptureDsp? _dsp;
    private int _dspRate;
    private Timer? _watchdog;
    private long _dspStartedAt;
    private long _lastSoundAt;
    private bool _usingPlain;
    private bool _started;
    private bool _paused;
    private bool _closed;

    public EchoCancellingMicrophone(
        WindowsAudioEndPoint plain, IAudioEncoder encoder, ILogger<EchoCancellingMicrophone> logger)
    {
        _plain = plain;
        _encoder = encoder;
        _logger = logger;
        _formats = new MediaFormatManager<AudioFormat>(encoder.SupportedFormats);
    }

    public event EncodedSampleDelegate? OnAudioSourceEncodedSample;

    public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;

    public event SourceErrorDelegate? OnAudioSourceError;

    [Obsolete("The audio source only generates encoded samples.")]
    public event RawAudioSampleDelegate OnAudioSourceRawSample
    {
        add { }
        remove { }
    }

    public List<AudioFormat> GetAudioSourceFormats() => _formats.GetSourceFormats();

    public void RestrictFormats(Func<AudioFormat, bool> filter)
    {
        _formats.RestrictFormats(filter);
        _plain.RestrictFormats(filter);
    }

    /// <summary>
    /// The codec agreed with the PBX. Passed to the plain microphone as well,
    /// so it is ready at the right rate if it has to take over.
    /// </summary>
    public void SetAudioSourceFormat(AudioFormat audioFormat)
    {
        bool restart;

        lock (_gate)
        {
            _formats.SetSelectedFormat(audioFormat);
            restart = _dsp is not null && _dspRate != audioFormat.ClockRate;
        }

        _plain.SetAudioSourceFormat(audioFormat);

        if (restart)
        {
            // Agreed again at a new rate mid-call (a re-INVITE): the canceller
            // only delivers the rate it was opened at.
            StopDsp();
            StartCapture();
        }
    }

    public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample is not null;

    public bool IsAudioSourcePaused() => _paused;

    public void ExternalAudioSourceRawSample(
        AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample) =>
        throw new NotImplementedException();

    public Task StartAudio()
    {
        lock (_gate)
        {
            if (_started || _closed)
            {
                return Task.CompletedTask;
            }

            _started = true;
        }

        StartCapture();
        return Task.CompletedTask;
    }

    /// <summary>Mute and hold (A-12): nothing is sent or recorded until it resumes.</summary>
    public Task PauseAudio()
    {
        bool plain;

        lock (_gate)
        {
            _paused = true;
            _pending.Clear();
            plain = _usingPlain;
        }

        // The canceller keeps listening through a pause, so it does not have to
        // learn the room again afterwards; what it hears is thrown away.
        return plain ? _plain.PauseAudio() : Task.CompletedTask;
    }

    public Task ResumeAudio()
    {
        bool plain;

        lock (_gate)
        {
            _paused = false;
            plain = _usingPlain;
        }

        return plain ? _plain.ResumeAudio() : Task.CompletedTask;
    }

    public Task CloseAudio()
    {
        bool plain;

        lock (_gate)
        {
            if (_closed)
            {
                return Task.CompletedTask;
            }

            _closed = true;
            plain = _usingPlain;
        }

        StopDsp();

        if (plain)
        {
            _plain.OnAudioSourceEncodedSample -= ForwardSample;
            _plain.OnAudioSourceEncodedFrameReady -= ForwardFrame;
            _plain.OnAudioSourceError -= ForwardError;
        }

        // Only the microphone half: the speaker half is the sink's, and the
        // media session closes it separately.
        return _plain.CloseAudio();
    }

    private void StartCapture()
    {
        AudioFormat format;

        lock (_gate)
        {
            if (_closed || _usingPlain)
            {
                return;
            }

            format = _formats.SelectedFormat;
        }

        if (format.ChannelCount != 1 || Array.IndexOf(VoiceCaptureDsp.SupportedRates, format.ClockRate) < 0)
        {
            _logger.LogWarning(
                "Echo cancellation is off for this call: {Codec} at {Rate} Hz is not a rate it can deliver",
                format.Codec, format.ClockRate);
            StartPlain();
            return;
        }

        try
        {
            var dsp = VoiceCaptureDsp.Start(format.ClockRate, OnDspSamples, OnDspFailed);
            var keep = false;

            lock (_gate)
            {
                if (!_closed)
                {
                    _dsp = dsp;
                    _dspRate = format.ClockRate;
                    _dspStartedAt = _lastSoundAt = Environment.TickCount64;
                    _watchdog = new Timer(_ => CheckStillDelivering(), null, 250, 250);
                    keep = true;
                }
            }

            if (!keep)
            {
                dsp.Dispose();
                return;
            }

            _logger.LogInformation(
                "Echo cancellation on: {Codec} {Rate} Hz, microphone #{Microphone}, speaker #{Speaker}",
                format.Codec, format.ClockRate, dsp.MicrophoneIndex, dsp.SpeakerIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Echo cancellation could not start; this call uses the plain microphone");
            StartPlain();
        }
    }

    private void StopDsp()
    {
        VoiceCaptureDsp? dsp;
        Timer? watchdog;

        lock (_gate)
        {
            dsp = _dsp;
            watchdog = _watchdog;
            _dsp = null;
            _watchdog = null;
            _pending.Clear();
        }

        watchdog?.Dispose();
        dsp?.Dispose();
    }

    /// <summary>
    /// The canceller delivers nothing while the speaker is not playing (see
    /// <see cref="VoiceCaptureDsp"/>), and does not say so: the agent would go
    /// silent mid-call. Checked four times a second.
    /// </summary>
    private void CheckStillDelivering()
    {
        long silentFor;

        lock (_gate)
        {
            if (_dsp is null || _closed)
            {
                return;
            }

            var now = Environment.TickCount64;
            var deadline = Math.Max(_dspStartedAt + FirstSoundMilliseconds, _lastSoundAt + QuietMilliseconds);

            if (now <= deadline)
            {
                return;
            }

            silentFor = now - _lastSoundAt;
        }

        _logger.LogWarning(
            "Echo cancellation delivered nothing for {Milliseconds} ms; the plain microphone takes over", silentFor);
        StopDsp();
        StartPlain();
    }

    /// <summary>The fallback: the endpoint's own microphone, its frames passed straight on.</summary>
    private void StartPlain()
    {
        bool paused;

        lock (_gate)
        {
            if (_closed || _usingPlain)
            {
                return;
            }

            _usingPlain = true;
            paused = _paused;
        }

        _plain.OnAudioSourceEncodedSample += ForwardSample;
        _plain.OnAudioSourceEncodedFrameReady += ForwardFrame;
        _plain.OnAudioSourceError += ForwardError;
        _plain.StartAudio();

        if (paused)
        {
            _plain.PauseAudio();
        }
    }

    private void OnDspFailed(Exception ex)
    {
        _logger.LogWarning(ex, "Echo cancellation stopped mid-call; the plain microphone takes over");

        // On the canceller's own thread, after it has let go of the microphone:
        // its Dispose must not wait for itself, and it does not.
        StopDsp();
        StartPlain();
    }

    private void OnDspSamples(short[] samples)
    {
        AudioFormat format;
        List<short[]> frames;

        lock (_gate)
        {
            // Before the pause check: a muted call is still a canceller that works.
            _lastSoundAt = Environment.TickCount64;

            if (_closed || _paused || _usingPlain)
            {
                return;
            }

            format = _formats.SelectedFormat;
            var frameSamples = format.ClockRate * FrameMilliseconds / 1000;

            _pending.AddRange(samples);
            frames = [];

            while (_pending.Count >= frameSamples)
            {
                frames.Add(_pending.GetRange(0, frameSamples).ToArray());
                _pending.RemoveRange(0, frameSamples);
            }
        }

        foreach (var frame in frames)
        {
            Send(frame, format);
        }
    }

    /// <summary>Encodes one packet and raises it exactly as the plain microphone would.</summary>
    private void Send(short[] pcm, AudioFormat format)
    {
        byte[] encoded;

        try
        {
            encoded = _encoder.EncodeAudio(pcm, format);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A packet of the agent's voice could not be encoded and was skipped");
            return;
        }

        // In RTP clock units, which for G.722 run at half the sample rate.
        var rtpDuration = (uint)((long)pcm.Length * format.RtpClockRate / format.ClockRate);
        OnAudioSourceEncodedSample?.Invoke(rtpDuration, encoded);

        if (OnAudioSourceEncodedFrameReady is { } frameReady)
        {
            var milliseconds = (uint)Math.Round(pcm.Length * 1000.0 / format.ClockRate);
            frameReady(new EncodedAudioFrame(0, format, milliseconds, encoded));
        }
    }

    private void ForwardSample(uint durationRtpUnits, byte[] sample) =>
        OnAudioSourceEncodedSample?.Invoke(durationRtpUnits, sample);

    private void ForwardFrame(EncodedAudioFrame frame) => OnAudioSourceEncodedFrameReady?.Invoke(frame);

    private void ForwardError(string error) => OnAudioSourceError?.Invoke(error);
}
