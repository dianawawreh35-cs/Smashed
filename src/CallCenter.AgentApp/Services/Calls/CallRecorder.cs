using System.IO;
using CallCenter.AgentApp.Audio;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// Records one call, both voices, to a file on this laptop (A-30).
/// </summary>
/// <remarks>
/// <b>The customer on one channel and the agent on the other</b>, in a single
/// stereo file. A supervisor reviewing a complaint hears the conversation as it
/// happened, and can turn one side down when the two talked over each other —
/// which is exactly what the calls worth reviewing sound like. Mixing them to
/// one channel would have halved the size and thrown that away permanently.
///
/// <b>Stored as the phone system sends it.</b> The audio arrives as G.711, so
/// the file is written as G.711 in a WAV container rather than expanded to
/// plain PCM: same bytes, same quality, half the disk, and it still opens in
/// anything. About 0.9 MB a minute.
///
/// <b>What is recorded is what crossed the line, not what the room heard.</b>
/// The agent's side is tapped from the microphone feed the call is using, so a
/// muted or held call records honest silence rather than the office.
///
/// <b>Nothing here may ever interrupt a call (A-32).</b> Every entry point
/// catches everything. A full disk, a file that will not open, a codec nobody
/// expected — each costs the recording and nothing else, and the call carries
/// on with no recording attached to it.
///
/// Audio arrives on SIPSorcery's threads, one per direction, so the two writes
/// are serialised on a lock. The work per frame is a decode and a copy, at
/// 8 kHz, which is nothing.
/// </remarks>
public sealed class CallRecorder : IDisposable
{
    /// <summary>
    /// Everything is resampled to this before it is written. G.711 runs at
    /// 8 kHz, so in practice nothing is resampled at all; the step exists so a
    /// PBX offering wideband audio produces a correct file rather than a
    /// chipmunk.
    /// </summary>
    private const int SampleRate = 8000;

    private const int BytesPerSecondPerChannel = SampleRate;

    /// <summary>
    /// How far the two channels may drift apart before silence is inserted to
    /// straighten them. One RTP packet is 20 ms; a fifth of a second is short
    /// enough that nobody hears the join and long enough that ordinary jitter
    /// does not trigger it.
    /// </summary>
    private static readonly TimeSpan MaxDrift = TimeSpan.FromMilliseconds(200);

    /// <summary>8 kHz mu-law, which is what this PBX sends and what is stored.</summary>
    private static readonly AudioFormat MuLaw = new(SDPWellKnownMediaFormatsEnum.PCMU);

    private readonly ILogger<CallRecorder> _logger;
    private readonly AudioEncoder _codec = new();
    private readonly Lock _gate = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    /// <summary>The customer, and the agent, as raw 8 kHz G.711 while the call runs.</summary>
    private readonly string _remoteScratch;
    private readonly string _localScratch;

    private FileStream? _remote;
    private FileStream? _local;

    private long _remoteBytes;
    private long _localBytes;

    private bool _failed;
    private bool _stopped;

    /// <summary>
    /// When the agent held the call, as frames from the start of the recording
    /// (A-51). Written after the audio so the player can mark the silence as a
    /// hold rather than leave it looking like a dead line.
    /// </summary>
    private readonly List<(long StartFrame, long Frames)> _holds = [];

    /// <summary>Where the hold now in progress began, or null when there is none.</summary>
    private long? _holdStartFrame;

    private CallRecorder(string folder, string name, ILogger<CallRecorder> logger)
    {
        _logger = logger;

        _remoteScratch = Path.Combine(folder, $"{name}.remote.g711");
        _localScratch = Path.Combine(folder, $"{name}.local.g711");

        _remote = new FileStream(_remoteScratch, FileMode.Create, FileAccess.Write, FileShare.None);
        _local = new FileStream(_localScratch, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    /// <summary>
    /// Where the finished file will be written. Named FilePath, not Path,
    /// because a property called Path on this class hides System.IO.Path from
    /// every line inside it.
    /// </summary>
    public string FilePath { get; private init; } = string.Empty;

    /// <summary>
    /// Starts a recording, or returns null if it cannot be started. Never
    /// throws: a call must happen whether or not it can be recorded (A-32).
    /// </summary>
    public static CallRecorder? Start(string folder, string name, ILogger<CallRecorder> logger)
    {
        try
        {
            Directory.CreateDirectory(folder);

            return new CallRecorder(folder, name, logger)
            {
                FilePath = Path.Combine(folder, $"{name}.wav"),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "This call will not be recorded: the recording could not be started");
            return null;
        }
    }

    /// <summary>The customer's voice, as it arrived from the PBX.</summary>
    public void WriteRemote(EncodedAudioFrame frame)
    {
        if (frame?.EncodedAudio is not { Length: > 0 } encoded)
        {
            return;
        }

        // On hold the PBX keeps sending the customer's voice although the
        // agent no longer hears it (A-12). The hold is marked as silence, so
        // silence is what it records; what a customer says to nobody while
        // waiting is not part of the call.
        lock (_gate)
        {
            if (_holdStartFrame is not null)
            {
                return;
            }
        }

        Write(remote: true, Transcode(encoded, frame.AudioFormat));
    }

    /// <summary>
    /// The agent's voice, as it goes out to the PBX.
    /// </summary>
    /// <remarks>
    /// Taken encoded, from the same feed the call transmits, so it is what the
    /// customer actually heard. The first attempt used the raw-sample event
    /// instead and produced a <b>completely silent agent channel on a real
    /// call</b>: <c>WindowsAudioEndPoint</c> never raises it — the compiler
    /// said so, marking it obsolete with "the audio source only generates
    /// encoded samples", and the warning was filtered out of the build output.
    ///
    /// While the call is muted or on hold the source is paused and nothing
    /// arrives, which is why those stretches record silence — the honest
    /// answer.
    /// </remarks>
    public void WriteLocal(EncodedAudioFrame frame)
    {
        if (frame?.EncodedAudio is not { Length: > 0 } encoded)
        {
            return;
        }

        Write(remote: false, Transcode(encoded, frame.AudioFormat));
    }

    /// <summary>
    /// The agent put the call on hold, or took it off (A-12). Marked so the
    /// recording can say where the silence is a hold (A-51).
    /// </summary>
    /// <remarks>
    /// Only the agent's own hold is marked. When the PBX holds the laptop — a
    /// transfer, say — it plays its music <i>to</i> the laptop, so that music is
    /// in the recording already and needs no explaining.
    ///
    /// Timed from the same clock the padding in <see cref="Write"/> uses, so a
    /// mark lands on the same place in the file as the silence it describes.
    /// </remarks>
    public void MarkHold(bool onHold)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            var frame = FramesSoFar();

            if (onHold)
            {
                _holdStartFrame ??= frame;
            }
            else if (_holdStartFrame is { } start)
            {
                _holds.Add((start, frame - start));
                _holdStartFrame = null;
            }
        }
    }

    /// <summary>
    /// Finishes the file and returns it, or null if there is nothing usable.
    /// Never throws.
    /// </summary>
    public RecordedCall? Stop()
    {
        long hangUpFrame;

        lock (_gate)
        {
            if (_stopped)
            {
                return null;
            }

            hangUpFrame = FramesSoFar();

            // Hung up while on hold: the hold ends with the call.
            if (_holdStartFrame is { } start)
            {
                _holds.Add((start, hangUpFrame - start));
                _holdStartFrame = null;
            }

            _stopped = true;
        }

        try
        {
            if (_failed || (_remoteBytes == 0 && _localBytes == 0))
            {
                // Nobody said anything, or the writing had already given up. An
                // empty file attached to a call is worse than no file: it looks
                // like a recording until somebody plays it.
                CloseScratch();
                Discard();
                return null;
            }

            // Before the padding below makes both sides look heard.
            var remoteHeard = _remoteBytes;
            var localHeard = _localBytes;

            PadToHangUp(hangUpFrame);
            CloseScratch();

            var bytes = Interleave();
            var seconds = (int)(bytes / (double)(BytesPerSecondPerChannel * 2));

            // The working files are twice the size of what they produced, and
            // nothing reads them again.
            Discard();

            if (remoteHeard == 0 || localHeard == 0)
            {
                // One side of the conversation is missing. Said out loud,
                // because the file is otherwise the right size and the right
                // shape, and the only other way to find out is to play it.
                _logger.LogWarning(
                    "The recording has silence on one side: {Missing} was never heard. "
                    + "Customer {RemoteBytes} bytes, agent {LocalBytes} bytes.",
                    remoteHeard == 0 ? "the customer" : "the agent",
                    remoteHeard, localHeard);
            }

            _logger.LogInformation(
                "Recorded {Seconds}s of call audio to {Path} ({Size} bytes)", seconds, FilePath, bytes);

            return new RecordedCall(FilePath, bytes, seconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The recording could not be finished; the call is unaffected");

            // A half-written file would be attached to the call and play as
            // noise, which is worse than admitting there is no recording.
            DeleteQuietly(FilePath);
            Discard();
            return null;
        }
    }

    /// <summary>
    /// Runs both channels on to the moment the call ended, in silence.
    /// </summary>
    /// <remarks>
    /// A recording lasts from answer to hang-up (A-30). Padding otherwise only
    /// happens when the next sound arrives, so a call hung up while on hold or
    /// muted ended at the last thing anybody said, and the hold it ended on had
    /// no audio under it to be marked on (A-51).
    ///
    /// A failure here costs the silent tail and nothing more: the audio before
    /// it is whole, so it is logged and the recording finished without it.
    /// </remarks>
    private void PadToHangUp(long hangUpFrame)
    {
        try
        {
            // One byte per frame per channel.
            if (_remote is not null && _remoteBytes < hangUpFrame)
            {
                PadSilence(_remote, hangUpFrame - _remoteBytes);
                _remoteBytes = hangUpFrame;
            }

            if (_local is not null && _localBytes < hangUpFrame)
            {
                PadSilence(_local, hangUpFrame - _localBytes);
                _localBytes = hangUpFrame;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The recording ends at the last sound rather than at the hang-up");
        }
    }

    /// <summary>
    /// Appends to one channel, padding it with silence first if it has fallen
    /// behind the wall clock.
    /// </summary>
    /// <remarks>
    /// The padding is what keeps the two voices lined up. Audio only arrives
    /// while somebody is speaking and sending: a hold, a mute, or a customer
    /// who says nothing for ten seconds all produce a gap, and without padding
    /// the next thing they say would be heard on top of whatever the other
    /// party was saying ten seconds earlier.
    /// </remarks>
    private void Write(bool remote, byte[]? audio)
    {
        if (audio is null || audio.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_failed || _stopped)
            {
                return;
            }

            try
            {
                var stream = remote ? _remote : _local;
                if (stream is null)
                {
                    return;
                }

                var written = remote ? _remoteBytes : _localBytes;
                var elapsed = DateTimeOffset.Now - _startedAt;
                var expected = (long)(elapsed.TotalSeconds * BytesPerSecondPerChannel);
                var behind = expected - written;

                if (behind > MaxDrift.TotalSeconds * BytesPerSecondPerChannel)
                {
                    PadSilence(stream, behind);
                    written += behind;
                }

                stream.Write(audio, 0, audio.Length);
                written += audio.Length;

                if (remote)
                {
                    _remoteBytes = written;
                }
                else
                {
                    _localBytes = written;
                }
            }
            catch (Exception ex)
            {
                // Once, then never again: a disk that is full will not empty
                // itself mid-call, and a line per frame would bury the log.
                _failed = true;
                _logger.LogError(ex, "Recording stopped mid-call; the call itself is unaffected");
            }
        }
    }

    /// <summary>
    /// G.711 silence is 0xFF in mu-law, not zero. Zero is a loud buzz, which is
    /// the sort of thing that is only discovered by playing a recording of a
    /// call that was on hold.
    /// </summary>
    private static void PadSilence(FileStream stream, long bytes)
    {
        Span<byte> silence = stackalloc byte[1024];
        silence.Fill(0xFF);

        while (bytes > 0)
        {
            var chunk = (int)Math.Min(bytes, silence.Length);
            stream.Write(silence[..chunk]);
            bytes -= chunk;
        }
    }

    /// <summary>
    /// Whatever arrived, as 8 kHz mu-law. PCMU passes straight through, which
    /// is the case that actually happens on this PBX.
    /// </summary>
    private byte[]? Transcode(byte[] encoded, AudioFormat format)
    {
        try
        {
            if (format.Codec == AudioCodecsEnum.PCMU && format.ClockRate == SampleRate)
            {
                return encoded;
            }

            var pcm = _codec.DecodeAudio(encoded, format);
            return ToG711(pcm, format.ClockRate);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (!_failed)
                {
                    _failed = true;
                    _logger.LogError(
                        ex, "Recording stopped: {Codec} audio could not be read", format.Codec);
                }
            }

            return null;
        }
    }

    private byte[]? ToG711(short[] pcm, int rate)
    {
        try
        {
            var atRate = rate == SampleRate ? pcm : PcmResampler.Resample(pcm, rate, SampleRate);
            return _codec.EncodeAudio(atRate, MuLaw);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (!_failed)
                {
                    _failed = true;
                    _logger.LogError(ex, "Recording stopped: the audio could not be converted");
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Weaves the two channels into one stereo WAV: customer left, agent right,
    /// the shorter one padded so both end together.
    /// </summary>
    private long Interleave()
    {
        var length = Math.Max(_remoteBytes, _localBytes);

        // After the audio, not before it, so the audio still starts at byte 58
        // and anything that assumed so keeps working. Holds past the end of
        // the audio are trimmed by the reader.
        var holdChunk = RecordingWav.HoldChunk(_holds);

        using var remote = File.OpenRead(_remoteScratch);
        using var local = File.OpenRead(_localScratch);
        using var output = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);

        WriteWavHeader(output, length * 2, holdChunk.Length);

        var remoteBuffer = new byte[4096];
        var localBuffer = new byte[4096];
        var interleaved = new byte[8192];

        long done = 0;

        while (done < length)
        {
            var chunk = (int)Math.Min(remoteBuffer.Length, length - done);

            var fromRemote = ReadFully(remote, remoteBuffer, chunk);
            var fromLocal = ReadFully(local, localBuffer, chunk);

            // Past the end of a channel is silence, so a caller who hung up
            // early does not leave the other voice paired with rubbish.
            remoteBuffer.AsSpan(fromRemote, chunk - fromRemote).Fill(0xFF);
            localBuffer.AsSpan(fromLocal, chunk - fromLocal).Fill(0xFF);

            for (var i = 0; i < chunk; i++)
            {
                interleaved[i * 2] = remoteBuffer[i];
                interleaved[(i * 2) + 1] = localBuffer[i];
            }

            output.Write(interleaved, 0, chunk * 2);
            done += chunk;
        }

        // Two channels of one byte each, so the data is always even and needs
        // no pad byte before the next chunk.
        output.Write(holdChunk);

        output.Flush();
        return output.Length;
    }

    /// <summary>How far into the call it is now, in frames of the recording.</summary>
    private long FramesSoFar() =>
        (long)((DateTimeOffset.Now - _startedAt).TotalSeconds * SampleRate);

    private static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        var total = 0;

        while (total < count)
        {
            var read = stream.Read(buffer, total, count - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>
    /// A WAV header for two-channel 8 kHz mu-law. Written by hand rather than
    /// with a library: it is 58 bytes of well-documented structure, and the
    /// alternative is a dependency for one file format.
    /// </summary>
    /// <param name="trailingBytes">Chunks written after the audio: the hold marks, if any.</param>
    private static void WriteWavHeader(Stream stream, long dataBytes, int trailingBytes)
    {
        const short muLaw = 7;
        const short channels = 2;
        const short bitsPerSample = 8;

        var byteRate = SampleRate * channels * (bitsPerSample / 8);
        var blockAlign = (short)(channels * (bitsPerSample / 8));

        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        writer.Write("RIFF"u8);
        writer.Write((uint)(50 + dataBytes + trailingBytes));
        writer.Write("WAVE"u8);

        // 18-byte fmt chunk, not 16: anything other than plain PCM needs the
        // extension size field, and players are entitled to refuse a file
        // without it.
        writer.Write("fmt "u8);
        writer.Write(18u);
        writer.Write(muLaw);
        writer.Write(channels);
        writer.Write((uint)SampleRate);
        writer.Write((uint)byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write((short)0);

        // "fact" is required for every non-PCM WAV and carries the sample count.
        writer.Write("fact"u8);
        writer.Write(4u);
        writer.Write((uint)(dataBytes / channels));

        writer.Write("data"u8);
        writer.Write((uint)dataBytes);
    }

    private void CloseScratch()
    {
        try
        {
            _remote?.Flush();
            _local?.Flush();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A recording scratch file did not flush cleanly");
        }

        _remote?.Dispose();
        _local?.Dispose();
        _remote = null;
        _local = null;
    }

    /// <summary>
    /// Removes the working files. Called whether the recording succeeded or
    /// not, because they are twice the size of what they produce.
    /// </summary>
    private void Discard()
    {
        DeleteQuietly(_remoteScratch);
        DeleteQuietly(_localScratch);
    }

    private void DeleteQuietly(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A recording file could not be removed: {Path}", path);
        }
    }

    public void Dispose()
    {
        CloseScratch();
        Discard();
        _codec.Dispose();
    }
}

/// <summary>A finished recording, waiting to be uploaded (A-31).</summary>
public record RecordedCall(string Path, long SizeBytes, int DurationSec);
