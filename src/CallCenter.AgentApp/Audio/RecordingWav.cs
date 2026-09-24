using System.Buffers.Binary;

namespace CallCenter.AgentApp.Audio;

/// <summary>
/// Where the audio is inside a recording the server sent back (A-51).
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

    /// <summary>How long it plays for. One byte is one sample of one channel.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)DataLength / (SampleRate * Channels));

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
                length -= length % channels.Value;

                return length > 0
                    ? new RecordingWav(channels.Value, sampleRate.Value, body, length)
                    : null;
            }

            // Chunks are padded to an even length.
            var next = body + (long)size + (size % 2);
            if (next > file.Length)
            {
                return null;
            }

            position = (int)next;
        }

        return null;
    }
}
