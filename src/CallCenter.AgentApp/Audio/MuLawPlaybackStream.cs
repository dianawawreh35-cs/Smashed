using System.Runtime.InteropServices;
using NAudio.Codecs;
using NAudio.Wave;

namespace CallCenter.AgentApp.Audio;

/// <summary>
/// A recording, decoded to 16-bit PCM as it is played (A-51).
/// </summary>
/// <remarks>
/// Decoded on the way out rather than all at once: the whole call would be
/// twice its size in memory for no gain, and seeking is exact either way — one
/// mu-law byte is always one PCM sample, so a position here is the position in
/// the file times two.
///
/// Played by the Windows output device rather than handed to WPF's
/// <c>MediaElement</c>, which cannot be given bytes, only a URL or a file (see
/// <c>ApiClient.GetRecordingAsync</c> for why neither will do).
/// </remarks>
public sealed class MuLawPlaybackStream(byte[] file, RecordingWav wav) : WaveStream
{
    /// <summary>
    /// The device reads on its own thread while the seek bar moves the position
    /// from the UI thread. Without this, a seek landing mid-read is overwritten
    /// by the read finishing, and the bar jumps back.
    /// </summary>
    private readonly Lock _gate = new();

    private long _position;

    public override WaveFormat WaveFormat { get; } = new(wav.SampleRate, 16, wav.Channels);

    /// <summary>In PCM bytes: two for every byte of the recording.</summary>
    public override long Length => (long)wav.DataLength * 2;

    public override long Position
    {
        get
        {
            lock (_gate)
            {
                return _position;
            }
        }

        // Whole frames only. Landing between the left and right sample of one
        // frame would swap the customer and the agent for the rest of the call.
        set
        {
            var clamped = Math.Clamp(value, 0, Length);

            lock (_gate)
            {
                _position = clamped - (clamped % WaveFormat.BlockAlign);
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        lock (_gate)
        {
            return ReadLocked(buffer);
        }
    }

    private int ReadLocked(Span<byte> buffer)
    {
        var samples = (int)Math.Min(buffer.Length / 2, (Length - _position) / 2);
        samples -= samples % wav.Channels;

        if (samples <= 0)
        {
            return 0;
        }

        var source = file.AsSpan(wav.DataOffset + (int)(_position / 2), samples);
        var target = MemoryMarshal.Cast<byte, short>(buffer[..(samples * 2)]);

        MuLawDecoder.Decode(source, target);

        _position += samples * 2;
        return samples * 2;
    }
}
