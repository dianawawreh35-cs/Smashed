using System.Text;
using CallCenter.AgentApp.Audio;
using FluentAssertions;
using Xunit;

namespace CallCenter.Shared.Tests;

/// <summary>
/// The Agent App's reader for recordings coming back from the server (A-51).
/// </summary>
/// <remarks>
/// The failure these guard against is quiet: a reader that gets the offset
/// wrong does not crash, it plays the header as a burst of noise, or plays a
/// PCM file as static, and the agent concludes the recording is broken.
/// </remarks>
public class RecordingWavTests
{
    [Fact]
    public void Reads_the_file_the_recorder_writes()
    {
        // 2 seconds of 8 kHz stereo: 32,000 bytes.
        var file = Wav(audioBytes: 32_000);

        var wav = RecordingWav.Parse(file);

        wav.Should().NotBeNull();
        wav!.Channels.Should().Be(2);
        wav.SampleRate.Should().Be(8000);
        wav.DataOffset.Should().Be(58, "that is the size of the header CallRecorder writes");
        wav.DataLength.Should().Be(32_000);
        wav.Duration.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Skips_chunks_it_does_not_need()
    {
        // An editor or a restored backup may add a LIST chunk ahead of the
        // audio. Reading from a fixed offset would play it.
        var file = Wav(audioBytes: 16_000, extraChunk: ("LIST", 11));

        var wav = RecordingWav.Parse(file);

        wav.Should().NotBeNull();
        wav!.DataOffset.Should().Be(58 + 8 + 12, "the odd-sized chunk is padded to an even length");
        wav.DataLength.Should().Be(16_000);
    }

    [Fact]
    public void Plays_what_arrived_of_a_file_cut_short()
    {
        var file = Wav(audioBytes: 16_000)[..^1001];

        var wav = RecordingWav.Parse(file);

        wav.Should().NotBeNull();

        // What is there, trimmed to whole frames, so the customer and the
        // agent cannot swap sides at the end.
        wav!.DataLength.Should().Be(14_998);
    }

    [Fact]
    public void Refuses_a_file_that_is_not_mu_law()
    {
        var file = Wav(audioBytes: 1000, formatTag: 1, bitsPerSample: 16);

        RecordingWav.Parse(file).Should().BeNull("a PCM file played as mu-law is static");
    }

    [Fact]
    public void Refuses_something_that_is_not_a_wav()
    {
        RecordingWav.Parse("<html>Server error</html>"u8).Should().BeNull();
        RecordingWav.Parse([]).Should().BeNull();
    }

    [Fact]
    public void Refuses_a_file_with_no_audio()
    {
        RecordingWav.Parse(Wav(audioBytes: 0)).Should().BeNull();
    }

    [Fact]
    public void Reads_back_the_holds_written_after_the_audio()
    {
        // 10 s of audio, held from 2 s to 5 s and from 7 s to 7.5 s. Written in
        // the wrong order on purpose: the reader puts them in order.
        var holds = RecordingWav.HoldChunk([(56_000, 4_000), (16_000, 24_000)]);
        var file = Wav(audioBytes: 160_000, trailing: holds);

        var wav = RecordingWav.Parse(file);

        wav.Should().NotBeNull();
        wav!.DataOffset.Should().Be(58, "the marks go after the audio, so the audio has not moved");
        wav.DataLength.Should().Be(160_000);
        wav.Holds.Should().Equal(
            new HoldPeriod(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)),
            new HoldPeriod(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(0.5)));
    }

    [Fact]
    public void A_hold_that_outlasts_the_audio_is_cut_to_it()
    {
        // Hung up while on hold: the hold runs to the hang-up, the audio stops
        // at the last frame that arrived. And one hold starts after the audio
        // has ended altogether.
        var holds = RecordingWav.HoldChunk([(64_000, 40_000), (90_000, 1_000)]);
        var file = Wav(audioBytes: 160_000, trailing: holds);

        RecordingWav.Parse(file)!.Holds.Should().Equal(
            new HoldPeriod(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void A_recording_from_before_holds_were_marked_has_none()
    {
        RecordingWav.Parse(Wav(audioBytes: 16_000))!.Holds.Should().BeEmpty();
    }

    [Fact]
    public void A_call_never_held_writes_no_hold_chunk()
    {
        RecordingWav.HoldChunk([]).Should().BeEmpty("a file without holds is byte for byte what it was before");
    }

    /// <summary>
    /// A mu-law WAV laid out exactly as <c>CallRecorder.WriteWavHeader</c>
    /// writes one, with the audio filled with mu-law silence.
    /// </summary>
    private static byte[] Wav(
        int audioBytes,
        ushort formatTag = RecordingWav.MuLawFormat,
        ushort bitsPerSample = 8,
        (string Id, int Size)? extraChunk = null,
        byte[]? trailing = null)
    {
        const ushort channels = 2;
        const uint sampleRate = 8000;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII);

        writer.Write("RIFF"u8);
        writer.Write(0u);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(18u);
        writer.Write(formatTag);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((ushort)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write((ushort)0);

        writer.Write("fact"u8);
        writer.Write(4u);
        writer.Write((uint)(audioBytes / channels));

        if (extraChunk is { } extra)
        {
            writer.Write(Encoding.ASCII.GetBytes(extra.Id));
            writer.Write((uint)extra.Size);
            writer.Write(new byte[extra.Size + (extra.Size % 2)]);
        }

        writer.Write("data"u8);
        writer.Write((uint)audioBytes);
        writer.Write(Enumerable.Repeat((byte)0xFF, audioBytes).ToArray());
        writer.Write(trailing ?? []);

        writer.Flush();
        return stream.ToArray();
    }
}
