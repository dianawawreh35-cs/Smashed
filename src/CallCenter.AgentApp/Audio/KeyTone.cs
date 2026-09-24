using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace CallCenter.AgentApp.Audio;

/// <summary>
/// The short beep a phone makes when a key is pressed (A-20).
/// </summary>
/// <remarks>
/// Each key is the standard pair of frequencies a real keypad sends, so the
/// pad sounds like the phone it replaces. Generated like <see cref="RingbackTone"/>
/// rather than shipped as twelve files.
///
/// <b>One output, kept open.</b> The first version opened a new Windows output
/// for every press, on the UI thread, and the digit only appeared on screen
/// once the device had opened: a visible lag between the click and the number.
/// Now the output is opened once, on the first press, and plays silence; a
/// press just hands the generator a burst to mix in. The click is instant and
/// the beep follows within the output's buffer. A laptop with no output device
/// stays silent and the key still works.
/// </remarks>
public sealed class KeyTone(ILogger<KeyTone> logger) : IDisposable
{
    private const int Rate = 8000;
    private const double Seconds = 0.12;
    private const float Volume = 0.2f;

    /// <summary>The two tones for each key, as every keypad has them.</summary>
    private static readonly Dictionary<char, (double Low, double High)> Pairs = new()
    {
        ['1'] = (697, 1209), ['2'] = (697, 1336), ['3'] = (697, 1477),
        ['4'] = (770, 1209), ['5'] = (770, 1336), ['6'] = (770, 1477),
        ['7'] = (852, 1209), ['8'] = (852, 1336), ['9'] = (852, 1477),
        ['*'] = (941, 1209), ['0'] = (941, 1336), ['#'] = (941, 1477),
    };

    private readonly Lock _gate = new();
    private readonly Generator _generator = new();
    private WaveOut? _output;
    private bool _failed;

    /// <summary>Plays the tone for one key. Unknown keys are silent. Never blocks the caller for long.</summary>
    public void Play(char key)
    {
        if (!Pairs.TryGetValue(key, out var pair))
        {
            return;
        }

        // The burst is queued first, so even the very first press (which opens
        // the device) is heard once the device is ready.
        _generator.Trigger(pair.Low, pair.High);

        lock (_gate)
        {
            if (_output is not null || _failed)
            {
                return;
            }

            _failed = true;
        }

        // Opening the output takes a noticeable moment; off the UI thread so
        // the digit is drawn while it happens.
        _ = Task.Run(() =>
        {
            try
            {
                var output = new WaveOut();
                output.Init(_generator);
                output.Play();

                lock (_gate)
                {
                    _output = output;
                    _failed = false;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The key tones could not be played; the keys still work");
            }
        });
    }

    public void Dispose()
    {
        WaveOut? output;

        lock (_gate)
        {
            output = _output;
            _output = null;
        }

        output?.Dispose();
    }

    /// <summary>
    /// Silence, with a burst of two sine waves mixed in whenever a key asks
    /// for one. Read on the audio thread; triggered from the UI thread.
    /// </summary>
    private sealed class Generator : ISampleProvider
    {
        private readonly Lock _gate = new();
        private readonly int _length = (int)(Seconds * Rate);
        private double _low;
        private double _high;
        private int _position = int.MaxValue;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);

        public void Trigger(double low, double high)
        {
            lock (_gate)
            {
                _low = low;
                _high = high;
                _position = 0;
            }
        }

        public int Read(float[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public int Read(Span<float> buffer)
        {
            lock (_gate)
            {
                for (var i = 0; i < buffer.Length; i++)
                {
                    if (_position >= _length)
                    {
                        buffer[i] = 0f;
                        continue;
                    }

                    var t = (double)_position / Rate;

                    // A short fade at both ends so the burst does not click.
                    var edge = Math.Min(_position, _length - _position) / (0.005 * Rate);
                    var envelope = Math.Min(1.0, edge);

                    buffer[i] = (float)((Math.Sin(2 * Math.PI * _low * t) + Math.Sin(2 * Math.PI * _high * t))
                                        * 0.5 * Volume * envelope);
                    _position++;
                }
            }

            // Always a full buffer: silence keeps the output open and ready.
            return buffer.Length;
        }
    }
}
