using SIPSorcery.Media;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// Hears the PBX's voice prompts on a feature-code call and says when one has
/// finished (S-46).
/// </summary>
/// <remarks>
/// <b>Why listen at all.</b> Issabel's <c>*30</c> ignores keys pressed while it
/// is still talking: the number has to go in after "enter the number…" has
/// finished, and the <c>1</c> after the read-back and "press 1 to confirm". The
/// read-back is as long as the number, so a fixed wait would be either too
/// short for a long number or slow for every one. Instead the audio is measured
/// packet by packet, and a prompt has ended once it has been quiet for
/// <see cref="Quiet"/> after some sound.
///
/// <b>The level</b> is the mean absolute sample of a packet, decoded from G.711
/// to 16-bit. A spoken prompt sits in the thousands; the digital silence
/// Asterisk sends between prompts is 0, and line noise is well under
/// <see cref="LoudLevel"/>. A PBX that sends nothing at all while quiet is the
/// same as silence here, since quiet is measured from the last loud packet.
///
/// Thread-safe: packets arrive on the RTP thread, the dialer asks from its own.
/// </remarks>
public sealed class PromptListener(TimeProvider clock)
{
    /// <summary>Mean absolute 16-bit sample above which a packet is sound, not silence.</summary>
    public const int LoudLevel = 500;

    /// <summary>
    /// Quiet this long after sound, and the prompt is over. Longer than the gaps
    /// between the digits of the read-back and between "press" and "1", which
    /// are well under a second; short enough that the PBX is still waiting for
    /// the key when it comes.
    /// </summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    /// <summary>No sound at all this long after starting to listen: the audio is not reaching the server.</summary>
    public static readonly TimeSpan NoSoundLimit = TimeSpan.FromSeconds(10);

    /// <summary>A prompt still going after this long is not a prompt.</summary>
    public static readonly TimeSpan PromptLimit = TimeSpan.FromSeconds(45);

    public enum Heard
    {
        /// <summary>Still talking, or not started yet.</summary>
        Waiting,

        /// <summary>The prompt has finished: the PBX is waiting for a key.</summary>
        Ended,

        /// <summary>Nothing heard within <see cref="NoSoundLimit"/>.</summary>
        NoSound,

        /// <summary>Sound for longer than <see cref="PromptLimit"/>.</summary>
        TooLong,
    }

    private readonly Lock _gate = new();
    private DateTimeOffset _since = clock.GetUtcNow();
    private DateTimeOffset? _lastLoud;

    /// <summary>Starts listening for the next prompt: sound heard before now no longer counts.</summary>
    public void Listen()
    {
        lock (_gate)
        {
            _since = clock.GetUtcNow();
            _lastLoud = null;
        }
    }

    /// <summary>One packet's worth of audio, as its <see cref="Level"/>.</summary>
    public void Hear(int level)
    {
        if (level < LoudLevel)
        {
            return;
        }

        lock (_gate)
        {
            _lastLoud = clock.GetUtcNow();
        }
    }

    /// <summary>Where the current prompt is.</summary>
    public Heard State()
    {
        var now = clock.GetUtcNow();

        lock (_gate)
        {
            if (_lastLoud is not { } loud)
            {
                return now - _since >= NoSoundLimit ? Heard.NoSound : Heard.Waiting;
            }

            if (now - loud >= Quiet)
            {
                return Heard.Ended;
            }

            return now - _since >= PromptLimit ? Heard.TooLong : Heard.Waiting;
        }
    }

    /// <summary>
    /// How loud a G.711 packet is: the mean absolute sample once decoded.
    /// Null for any other payload — a DTMF event, comfort noise — which says
    /// nothing about whether the PBX is talking.
    /// </summary>
    public static int? Level(int payloadType, ReadOnlySpan<byte> payload)
    {
        if (payloadType is not (PcmuPayloadType or PcmaPayloadType) || payload.IsEmpty)
        {
            return null;
        }

        long total = 0;
        foreach (var b in payload)
        {
            var sample = payloadType == PcmuPayloadType
                ? MuLawDecoder.MuLawToLinearSample(b)
                : ALawDecoder.ALawToLinearSample(b);
            total += Math.Abs((int)sample);
        }

        return (int)(total / payload.Length);
    }

    /// <summary>G.711 μ-law, RFC 3551.</summary>
    public const int PcmuPayloadType = 0;

    /// <summary>G.711 A-law, RFC 3551.</summary>
    public const int PcmaPayloadType = 8;
}
