using System.Net;
using SIPSorceryMedia.Abstractions;

namespace CallCenter.AgentApp.Audio;

/// <summary>
/// The speaker of a call, with a way to stop the customer's voice reaching it
/// while the call is on hold (A-12).
/// </summary>
/// <remarks>
/// Hold asks the PBX to stop sending us the customer (an <c>a=sendonly</c>
/// re-INVITE), and Asterisk plays them its music — but it keeps sending their
/// voice all the same, so the agent went on hearing the customer through the
/// whole hold. Reported 26 Sep. A-12 says neither side hears the other, so the
/// frames are dropped here, on the laptop, whatever the PBX does.
///
/// Dropped rather than paused: <c>PauseAudioSink</c> stops the playback but the
/// frames keep queueing behind it, and Resume would open with the last few
/// seconds of whatever the customer said on hold.
/// </remarks>
public sealed class HoldableAudioSink(IAudioSink inner) : IAudioSink
{
    private volatile bool _silenced;

    /// <summary>True while on hold: the customer's audio is thrown away.</summary>
    public bool IsSilenced
    {
        get => _silenced;
        set => _silenced = value;
    }

    public event SourceErrorDelegate OnAudioSinkError
    {
        add => inner.OnAudioSinkError += value;
        remove => inner.OnAudioSinkError -= value;
    }

    public List<AudioFormat> GetAudioSinkFormats() => inner.GetAudioSinkFormats();

    public void SetAudioSinkFormat(AudioFormat audioFormat) => inner.SetAudioSinkFormat(audioFormat);

    public void RestrictFormats(Func<AudioFormat, bool> filter) => inner.RestrictFormats(filter);

    public void GotAudioRtp(
        IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
    {
        if (!_silenced)
        {
            // Obsolete, but still on the interface, so it has to be passed on.
#pragma warning disable CS0618
            inner.GotAudioRtp(remoteEndPoint, ssrc, seqnum, timestamp, payloadID, marker, payload);
#pragma warning restore CS0618
        }
    }

    public void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame)
    {
        if (!_silenced)
        {
            inner.GotEncodedMediaFrame(encodedMediaFrame);
        }
    }

    public Task StartAudioSink() => inner.StartAudioSink();

    public Task PauseAudioSink() => inner.PauseAudioSink();

    public Task ResumeAudioSink() => inner.ResumeAudioSink();

    public Task CloseAudioSink() => inner.CloseAudioSink();
}
