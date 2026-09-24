using System.Buffers.Binary;

namespace CallCenter.AgentApp.Audio;

/// <summary>
/// Where the audio is inside a recording the server sent back (A-51), and when
/// the call was on hold.
/// </summary>
/// <remarks>
/// The files are the ones <c>CallRecorder</c> writes: 8 kHz G.711 mu-law,
/// customer left and agent right. Parsed by hand for the same reason they are
/// written by hand — it is a few dozen bytes of documented structure.
///
/// <b>Chunks are walked, not assumed.</b> Our own header is 58 bytes, but a file
/// that has been through anything else (a supervisor's editor, a restored
/// backup) may carry a <c>LIST</c> chunk or put <c>fact</c> elsewhere. Reading
/// the data from a fixed offset would play the header as noise.
///
/// <b>The hold chunk.</b> While a call is on hold the PBX plays its music to
/// the customer and sends the laptop nothing, so that stretch of the recording
/// is silence on both sides. The recorder writes when each hold began and how
/// long it lasted in a <c>hold</c> chunk after the audio, so a player can say
/// "on hold" rather than leave a silence that looks like a dead line. Every
/// WAV reader skips a chunk it does not know, so the file still plays anywhere.
/// The layout is <see cref="HoldChunk"/>'s.
///
/// Deliberately free of WPF and of the audio library, so the test project can
/// compile this one file and check it without the app.
/// </remarks>
/// <param name="Channels">1 or 2. Ours are always 2.</param>
/// <param name="SampleRate">Samples per second per channel. Ours are 8000.</param>
/// <param name="DataOffset">Where the first audio byte is.</param>
/// <param name="DataLength">
/// How many audio bytes there are — what the header claims, or what actually
/// arrived if the file is shorter, and always a whole number of frames.
/// </param>
public sealed record RecordingWav(int Channels, int SampleRate, int DataOffset, int DataLength)
{
    /// <summary><c>WAVE_FORMAT_MULAW</c>, the format tag of every recording.</summary>
    public const ushort MuLawFormat = 7;

    /// <summary>
    /// When the call was on hold, in order, trimmed to the audio. Empty for a
    /// call never held, and for a recording made before holds were marked.
    /// </summary>
    public IReadOnlyList<HoldPeriod> Holds { get; init; } = [];

    /// <summary>How long it plays for. One byte is one sample of one channel.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)DataLength / (SampleRate * Channels));

    /// <summary>
    /// The <c>hold</c> chunk for a recording, ready to append after its audio,
    /// or nothing when the call was never held.
    /// </summary>
    /// <remarks>
    /// Chunk id <c>hold</c>, then one pair of little-endian unsigned 32-bit
    /// numbers per hold: the frame it began at and how many frames it lasted. A
    /// frame is one sample of every channel, 1/8000 of a second here, so the
    /// numbers are positions in the audio rather than clock times and cannot
    /// drift from it.
    /// </remarks>
    public static byte[] HoldChunk(IReadOnlyList<(long StartFrame, long Frames)> holds)
    {
        if (holds.Count == 0)
        {
            return [];
        }

        var chunk = new byte[8 + (holds.Count * 8)];
        "hold"u8.CopyTo(chunk);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)(holds.Count * 8));

        for (var i = 0; i < holds.Count; i++)
        {
            var at = chunk.AsSpan(8 + (i * 8));
            BinaryPrimitives.WriteUInt32LittleEndian(at, (uint)Math.Clamp(holds[i].StartFrame, 0, uint.MaxValue));
            BinaryPrimitives.WriteUInt32LittleEndian(at[4..], (uint)Math.Clamp(holds[i].Frames, 0, uint.MaxValue));
        }

        return chunk;
    }

    /// <summary>
    /// Finds the audio in a mu-law WAV, or returns null for anything this app
    /// cannot play — so the screen can say so rather than play static.
    /// </summary>
    public static RecordingWav? Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length < 12
            || !file[..4].SequenceEqual("RIFF"u8)
            || !file.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return null;
        }

        int? channels = null;
        int? sampleRate = null;
        int? dataOffset = null;
        var dataLength = 0;
        var holds = new List<(long StartFrame, long Frames)>();
        var position = 12;

        while (position + 8 <= file.Length)
        {
            var id = file.Slice(position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(position + 4, 4));
            var body = position + 8;

            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16 || body + 16 > file.Length)
                {
                    return null;
                }

                var format = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(body, 2));
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(body + 14, 2));

                if (format != MuLawFormat || bits != 8)
                {
                    return null;
                }

                channels = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(body + 2, 2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(body + 4, 4));
            }
            else if (id.SequenceEqual("data"u8))
            {
                // The format has to come first: without it the bytes mean nothing.
                if (channels is not (1 or 2) || sampleRate is not > 0)
                {
                    return null;
                }

                // A file cut short in transfer still plays up to where it stops,
                // rather than being refused for the part that is missing.
                var length = (int)Math.Min(size, (uint)(file.Length - body));
                dataOffset = body;
                dataLength = length - (length % channels.Value);
            }
            else if (id.SequenceEqual("hold"u8))
            {
                var pairs = file.Slice(body, (int)Math.Min(size, (uint)(file.Length - body)));

                for (var at = 0; at + 8 <= pairs.Length; at += 8)
                {
                    holds.Add((
                        BinaryPrimitives.ReadUInt32LittleEndian(pairs.Slice(at, 4)),
                        BinaryPrimitives.ReadUInt32LittleEndian(pairs.Slice(at + 4, 4))));
                }
            }

            // Chunks are padded to an even length. One that runs past the end
            // is where a file was cut short; whatever came before it stands.
            var next = body + (long)size + (size % 2);
            if (next > file.Length)
            {
                break;
            }

            position = (int)next;
        }

        if (dataOffset is not { } offset || dataLength <= 0)
        {
            return null;
        }

        return new RecordingWav(channels!.Value, sampleRate!.Value, offset, dataLength)
        {
            Holds = Trim(holds, frames: dataLength / channels.Value, sampleRate.Value),
        };
    }

    /// <summary>
    /// Holds as times, in order, cut to the audio there is. A call hung up
    /// while on hold has a hold that outlasts its audio, because nothing
    /// arrived to pad the file out to the end.
    /// </summary>
    private static List<HoldPeriod> Trim(List<(long StartFrame, long Frames)> holds, long frames, int sampleRate)
    {
        var trimmed = new List<HoldPeriod>();

        foreach (var (start, length) in holds.OrderBy(h => h.StartFrame))
        {
            var end = Math.Min(start + length, frames);

            if (start < frames && end > start)
            {
                trimmed.Add(new HoldPeriod(
                    TimeSpan.FromSeconds((double)start / sampleRate),
                    TimeSpan.FromSeconds((double)(end - start) / sampleRate)));
            }
        }

        return trimmed;
    }
}

/// <summary>One stretch of a recording during which the call was on hold.</summary>
public readonly record struct HoldPeriod(TimeSpan Start, TimeSpan Length)
{
    public TimeSpan End => Start + Length;
}
