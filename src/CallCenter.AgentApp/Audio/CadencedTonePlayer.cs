using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace CallCenter.AgentApp.Audio;

/// <summary>
/// A tone that repeats a pattern of on and off for as long as it is playing:
/// the ringing an agent hears for an outgoing call, and the ring of an
/// incoming one.
/// </summary>
/// <remarks>
/// Generated rather than shipped as a file. A tone is one or two sine waves
/// with a cadence, which is a few lines here and nothing to license, download
/// or lose on install. Played on the Windows default output, where the call's
/// audio goes too, so it is heard in the same headset. Never throws out of
/// <see cref="Start"/>: a laptop with no output device still gets the call.
/// </remarks>
/// <param name="frequencies">The sine waves mixed together, in Hz.</param>
/// <param name="cadence">
/// Alternating on and off lengths in seconds, starting with on, repeated for
/// ever. <c>[1, 4]</c> is one second on and four off.
/// </param>
/// <param name="warbleHz">
/// Zero mixes the frequencies into one chord, as a network tone is. Above
/// zero the tone jumps between them that many times a second instead, which
/// is what makes a bell sound like a bell rather than a dial tone.
/// </param>
public abstract class CadencedTonePlayer(
    double[] frequencies, double[] cadence, float volume, ILogger logger, double warbleHz = 0) : IDisposable
{
    private readonly Lock _gate = new();
    private WaveOut? _output;

    /// <summary>Starts the tone. Does nothing if it is already playing.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_output is not null)
            {
                return;
            }

            try
            {
                var output = new WaveOut();
                output.Init(new Pattern(frequencies, cadence, volume, warbleHz));
                output.Play();
                _output = output;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The {Tone} could not be played; the call is unaffected", GetType().Name);
            }
        }
    }

    /// <summary>Stops the tone. Safe to call when it is not playing.</summary>
    public void Stop()
    {
        WaveOut? output;

        lock (_gate)
        {
            output = _output;
            _output = null;
        }

        if (output is null)
        {
            return;
        }

        try
        {
            output.Stop();
            output.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The {Tone} did not stop cleanly", GetType().Name);
        }
    }

    public void Dispose() => Stop();

    /// <summary>The sine waves, switched on and off by the cadence, for ever.</summary>
    private sealed class Pattern(double[] frequencies, double[] cadence, float volume, double warbleHz)
        : ISampleProvider
    {
        private const int Rate = 8000;
        private readonly int[] _segments = cadence.Select(s => (int)(s * Rate)).ToArray();
        private readonly int _period = cadence.Sum(s => (int)(s * Rate));
        private readonly int _warbleStep = warbleHz > 0 ? (int)(Rate / warbleHz) : 0;
        private long _position;
        private double _phase;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);

        public int Read(float[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public int Read(Span<float> buffer)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                if (!IsOn(_position % _period))
                {
                    buffer[i] = 0f;
                    _position++;
                    continue;
                }

                if (_warbleStep > 0)
                {
                    // One frequency at a time, switching every step. The phase
                    // carries across the switch so it does not click.
                    var current = frequencies[(int)(_position / _warbleStep % frequencies.Length)];
                    _phase += 2 * Math.PI * current / Rate;
                    buffer[i] = (float)(Math.Sin(_phase) * volume);
                }
                else
                {
                    buffer[i] = (float)(frequencies.Sum(f => Math.Sin(2 * Math.PI * f * _position / Rate))
                                        / frequencies.Length * volume);
                }

                _position++;
            }

            return buffer.Length;
        }

        /// <summary>Even segments of the cadence are on, odd ones off.</summary>
        private bool IsOn(long inPeriod)
        {
            for (var i = 0; i < _segments.Length; i++)
            {
                if (inPeriod < _segments[i])
                {
                    return i % 2 == 0;
                }

                inPeriod -= _segments[i];
            }

            return false;
        }
    }
}

/// <summary>
/// The ringing an agent hears while an outgoing call waits for the customer
/// to answer (A-20).
/// </summary>
/// <remarks>
/// The PBX answers an outgoing INVITE with 180 Ringing and no early media, so
/// nothing arrives on the line until the customer picks up. Without this the
/// agent sat in silence for up to 45 seconds, unable to tell "ringing" from
/// "dead". Desk phones solve it the same way: they generate the tone locally.
/// 425 Hz, one second on and four off, is the ETSI ringback used across the
/// region.
/// </remarks>
public sealed class RingbackTone(ILogger<RingbackTone> logger)
    : CadencedTonePlayer([425], [1.0, 4.0], 0.25f, logger);

/// <summary>
/// The ring of an incoming call (A-10). Louder and faster than the ringback,
/// because it has to be noticed across a room rather than heard down a line:
/// two bursts of a warbling bell, then a pause, like a desk phone. The warble
/// (two pitches swapping twenty times a second) is what says "ringing" to the
/// ear; the same two pitches held steady sounded like a network tone.
/// </summary>
public sealed class RingTone(ILogger<RingTone> logger)
    : CadencedTonePlayer([800, 1040], [0.5, 0.25, 0.5, 2.0], 0.45f, logger, warbleHz: 20);
