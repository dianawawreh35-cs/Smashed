using System.Buffers.Binary;
using System.Net;
using System.Threading.Channels;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace CallCenter.Server.Features.Pbx;

/// <summary>Whether a number goes on the PBX's blacklist or comes off it.</summary>
public enum BlacklistAction
{
    /// <summary><c>*30</c>.</summary>
    Add,

    /// <summary><c>*31</c>.</summary>
    Remove,
}

/// <summary>The server's own extension on the PBX, and where the PBX is (S-46, S-60).</summary>
public sealed record PbxExtension(string Host, string Extension, string Secret);

/// <summary>Why a feature-code call failed, in words a supervisor can act on.</summary>
/// <param name="answered">
/// Whether the PBX answered before it went wrong. An answered call may already
/// have done its work, so a toggle such as <c>*280</c> must not be dialled
/// again after one: it would switch straight back.
/// </param>
public sealed class PbxFeatureException(string message, bool answered = true) : Exception(message)
{
    public bool Answered { get; } = answered;
}

/// <summary>
/// Dials the PBX's feature codes the way a person at a desk phone would. An
/// interface so the blacklist and the queue switch can be tested without a PBX.
/// </summary>
public interface IPbxFeatureDialer
{
    /// <summary>
    /// <c>*30</c> or <c>*31</c> (S-46): the number and <c>#</c>, then <c>1</c> to
    /// confirm. Returns once the PBX has confirmed.
    /// </summary>
    /// <exception cref="PbxFeatureException">The call failed, or the PBX did not behave as expected.</exception>
    Task BlacklistAsync(PbxExtension from, BlacklistAction action, string number, CancellationToken ct);

    /// <summary>
    /// A code that switches something and says so, with no keys to press, such
    /// as <c>*280</c> for the queue (S-60). Returns once the announcement has
    /// finished or the PBX has hung up.
    /// </summary>
    /// <exception cref="PbxFeatureException">The call failed, or no announcement was heard.</exception>
    Task ToggleAsync(PbxExtension from, string code, CancellationToken ct);
}

/// <summary>
/// Listens in on an agent's call through the PBX's <c>*222</c> (S-62). An
/// interface so the listening screen can be tested without a PBX.
/// </summary>
public interface IPbxCallListener
{
    /// <summary>
    /// Dials <paramref name="code"/> (<c>*222</c> and the extension) and returns
    /// once the PBX has answered. The caller disposes the call to hang up.
    /// </summary>
    /// <exception cref="PbxFeatureException">The PBX did not answer, or refused the call.</exception>
    Task<IPbxListenCall> ListenAsync(PbxExtension from, string code, CancellationToken ct);
}

/// <summary>A listen-in call in progress (S-62). Disposing it hangs up.</summary>
public interface IPbxListenCall : IAsyncDisposable
{
    /// <summary>
    /// What the PBX sends, as it arrives: 16-bit little-endian mono samples at
    /// 8 kHz, one 20 ms packet per item. Completes when the call ends.
    /// </summary>
    ChannelReader<byte[]> Audio { get; }

    /// <summary>Completes when the PBX hangs up.</summary>
    Task Ended { get; }
}

/// <summary>
/// Places feature-code calls from the server's extension and answers their
/// prompts (S-46, S-60), and places the listen-in calls (S-62).
/// </summary>
/// <remarks>
/// <b>The steps,</b> as tried by hand on 26 Sep. <c>*30</c> / <c>*31</c>: hear
/// "enter the number…", key the number and <c>#</c>, hear it read back and
/// "press 1 to confirm", key <c>1</c>, hear the result. <c>*280</c>: hear that
/// the queue is open or closed, and the PBX hangs up. A key pressed while the
/// PBX is still talking is lost, so every key waits for the prompt before it
/// to end (<see cref="PromptListener"/>).
///
/// <b>Outbound only.</b> The call is an INVITE from the server with the
/// extension's digest login, like an agent's outgoing call; nothing registers
/// and nothing listens for the PBX. That keeps within SRS 4.5: no port is
/// opened towards the PBX.
///
/// <b>The server talks first.</b> Silence is sent for the whole call. Across
/// the VPN the PBX may only learn where to send its audio from the audio it
/// receives (symmetric RTP), and a server that stayed silent would never hear
/// the prompts.
///
/// <b>Keys go as RFC 2833 events,</b> not tones in the audio: that is what the
/// PBX asks for in its SDP, and what survives G.711 unchanged.
/// </remarks>
public sealed class SipFeatureDialer(TimeProvider clock, ILogger<SipFeatureDialer> logger) : IPbxFeatureDialer, IPbxCallListener
{
    public const string AddCode = "*30";
    public const string RemoveCode = "*31";

    /// <summary>How long the PBX may take to answer the feature code.</summary>
    public const int RingTimeoutSeconds = 20;

    /// <summary>The whole call, prompts and all. The read-back of a long number is the longest step.</summary>
    public static readonly TimeSpan CallLimit = TimeSpan.FromMinutes(2);

    /// <summary>Between one key and the next, as a finger would leave.</summary>
    public static readonly TimeSpan KeyGap = TimeSpan.FromMilliseconds(250);

    /// <summary>One packet of G.711 at 8 kHz: 20 ms.</summary>
    private const int SamplesPerPacket = 160;

    public Task BlacklistAsync(PbxExtension from, BlacklistAction action, string number, CancellationToken ct)
    {
        if (number.Length == 0 || !number.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Only digits can be keyed into the PBX.", nameof(number));
        }

        var code = action == BlacklistAction.Add ? AddCode : RemoveCode;

        return CallAsync(from, code, $"for {number}", async call =>
        {
            await call.PromptAsync("the request for the number");
            await call.KeysAsync(number + "#");

            await call.PromptAsync("the read-back of the number");
            await call.KeysAsync("1");

            // The result. The PBX may hang up once it has said it, which is
            // the same as it going quiet: either way the number is in.
            await call.PromptAsync("the confirmation", hangUpEnds: true);
        }, ct);
    }

    public Task ToggleAsync(PbxExtension from, string code, CancellationToken ct) =>
        // Heard to the end, or until the PBX hangs up, so the call is never cut
        // short of whatever the PBX does after answering.
        CallAsync(from, code, string.Empty, call => call.PromptAsync("the announcement", hangUpEnds: true), ct);

    /// <summary>Dials <paramref name="code"/>, runs <paramref name="script"/> once answered, and hangs up.</summary>
    /// <remarks>
    /// <paramref name="ct"/> can stop the call only before it is dialled (F-14,
    /// 27 Sep review). Once the PBX has answered, what the code does has
    /// started there, so the call is finished on its own time limit: a
    /// supervisor closing the tab mid-way used to cancel it after the PBX had
    /// toggled the queue and before the server wrote that down, and the next
    /// press flipped it the wrong way. The same holds for the blacklist codes.
    /// </remarks>
    private async Task CallAsync(
        PbxExtension from, string code, string about, Func<FeatureCall, Task> script, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var limit = new CancellationTokenSource(CallLimit);

        var destination = $"sip:{code}@{from.Host}";
        var listener = new PromptListener(clock);

        var transport = new SIPTransport();
        transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0)));

        var rtp = CreateMedia();
        var agent = new SIPUserAgent(transport, null);

        string? failure = null;
        var hungUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        agent.ClientCallFailed += (_, error, response) =>
            failure = response is null ? error : $"{(int)response.Status} {response.ReasonPhrase}";
        agent.OnCallHungup += _ => hungUp.TrySetResult();

        rtp.OnRtpPacketReceived += (_, media, packet) =>
        {
            if (media == SDPMediaTypesEnum.audio
                && PromptListener.Level(packet.Header.PayloadType, packet.Payload) is { } level)
            {
                listener.Hear(level);
            }
        };

        try
        {
            logger.LogInformation("PBX feature code: dialling {Code} {About} from extension {Extension}",
                code, about, from.Extension);

            var answered = await agent.Call(destination, from.Extension, from.Secret, rtp, RingTimeoutSeconds);
            if (!answered)
            {
                throw new PbxFeatureException(failure switch
                {
                    null => $"The PBX did not answer {code}.",
                    var f when f.StartsWith("401") || f.StartsWith("403") || f.StartsWith("407") =>
                        $"The PBX refused extension {from.Extension} ({f}). Check its number and password in Settings.",
                    var f when f.StartsWith("404") =>
                        $"The PBX does not know {code} ({f}). Check that the feature code is on in Issabel.",
                    var f => $"The PBX refused {code} ({f}).",
                }, answered: false);
            }

            using var silence = SendSilence(rtp, limit.Token);
            await script(new FeatureCall(rtp, listener, hungUp.Task, limit.Token));

            logger.LogInformation("PBX feature code: {Code} {About} done", code, about);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            throw new PbxFeatureException($"The call to {code} took longer than {CallLimit.TotalMinutes:0} minutes and was ended.");
        }
        finally
        {
            try
            {
                if (agent.IsCallActive)
                {
                    agent.Hangup();
                }

                rtp.Close("done");
                transport.Shutdown();
            }
            catch (Exception ex)
            {
                // The call did what it did; tidying it up must not change the answer.
                logger.LogDebug(ex, "PBX feature code: the call did not close cleanly");
            }
        }
    }

    public async Task<IPbxListenCall> ListenAsync(PbxExtension from, string code, CancellationToken ct)
    {
        var transport = new SIPTransport();
        transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0)));

        var rtp = CreateMedia();
        var agent = new SIPUserAgent(transport, null);

        // A second of audio at most waits for the browser; beyond that the
        // oldest goes, so a slow connection hears the call late by a second,
        // not by more and more.
        var audio = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        var hungUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? failure = null;

        agent.ClientCallFailed += (_, error, response) =>
            failure = response is null ? error : $"{(int)response.Status} {response.ReasonPhrase}";
        agent.OnCallHungup += _ =>
        {
            hungUp.TrySetResult();
            audio.Writer.TryComplete();
        };

        var heard = new SoundTally();
        rtp.OnRtpPacketReceived += (_, media, packet) =>
        {
            heard.Packet(packet.Header.PayloadType, packet.Payload);
            if (media == SDPMediaTypesEnum.audio && Pcm(packet.Header.PayloadType, packet.Payload) is { } pcm)
            {
                audio.Writer.TryWrite(pcm);
            }
        };

        try
        {
            logger.LogInformation("PBX listen: dialling {Code} from extension {Extension}", code, from.Extension);

            var answered = await agent.Call($"sip:{code}@{from.Host}", from.Extension, from.Secret, rtp, RingTimeoutSeconds);
            if (!answered)
            {
                throw new PbxFeatureException(failure switch
                {
                    null => $"The PBX did not answer {code}.",
                    var f when f.StartsWith("401") || f.StartsWith("403") || f.StartsWith("407") =>
                        $"The PBX refused extension {from.Extension} ({f}). Check its number and password in Settings.",
                    var f when f.StartsWith("404") =>
                        $"The PBX does not know {code} ({f}). Check that the listen-in code is set up in Issabel.",
                    var f => $"The PBX refused {code} ({f}).",
                }, answered: false);
            }

            ct.ThrowIfCancellationRequested();
            var silence = SendSilence(rtp, CancellationToken.None);

            logger.LogInformation("PBX listen: answered; the PBX sends its audio to {Destination}, codec {Codec}",
                rtp.AudioDestinationEndPoint, rtp.AudioStream?.GetSendingFormat().Name());

            return new ListenCall(agent, rtp, transport, silence, audio, hungUp.Task, heard, logger);
        }
        catch
        {
            Close(agent, rtp, transport, logger);
            throw;
        }
    }

    /// <summary>A G.711 packet as 16-bit little-endian samples, or null for anything else (such as a key event).</summary>
    public static byte[]? Pcm(int payloadType, byte[] payload)
    {
        if (payloadType is not (PromptListener.PcmuPayloadType or PromptListener.PcmaPayloadType) || payload.Length == 0)
        {
            return null;
        }

        var pcm = new byte[payload.Length * 2];
        for (var i = 0; i < payload.Length; i++)
        {
            var sample = payloadType == PromptListener.PcmuPayloadType
                ? MuLawDecoder.MuLawToLinearSample(payload[i])
                : ALawDecoder.ALawToLinearSample(payload[i]);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), sample);
        }

        return pcm;
    }

    private static void Close(SIPUserAgent agent, RTPSession rtp, SIPTransport transport, ILogger logger)
    {
        try
        {
            if (agent.IsCallActive)
            {
                agent.Hangup();
            }

            rtp.Close("done");
            transport.Shutdown();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "PBX listen: the call did not close cleanly");
        }
    }

    /// <summary>
    /// What came in on a listen-in, for the log when it ends: without it, "no
    /// sound" cannot be told apart between the PBX sending nothing and the
    /// browser not playing it (26 Sep, the first listen-in on the server).
    /// </summary>
    private sealed class SoundTally
    {
        private readonly Lock _gate = new();
        private readonly SortedSet<int> _payloadTypes = [];
        private int _packets;
        private int _loud;
        private int _loudest;

        public void Packet(int payloadType, byte[] payload)
        {
            var level = PromptListener.Level(payloadType, payload);
            lock (_gate)
            {
                _packets++;
                _payloadTypes.Add(payloadType);
                if (level is { } l)
                {
                    _loudest = Math.Max(_loudest, l);
                    if (l >= PromptListener.LoudLevel)
                    {
                        _loud++;
                    }
                }
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _packets == 0
                    ? "no audio at all reached the server"
                    : $"{_packets} packets ({_packets / 50} s) of payload type {string.Join(",", _payloadTypes)}, "
                      + $"{_loud} of them sound, loudest level {_loudest} (speech is in the thousands, silence 0)";
            }
        }
    }

    private sealed class ListenCall(
        SIPUserAgent agent,
        RTPSession rtp,
        SIPTransport transport,
        IDisposable silence,
        Channel<byte[]> audio,
        Task hungUp,
        SoundTally heard,
        ILogger logger) : IPbxListenCall
    {
        private int _closed;

        public ChannelReader<byte[]> Audio => audio.Reader;

        public Task Ended => hungUp;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1)
            {
                return;
            }

            silence.Dispose();
            audio.Writer.TryComplete();
            Close(agent, rtp, transport, logger);

            // Long enough for the BYE to leave before the port closes under it.
            await Task.Delay(200);
            logger.LogInformation("PBX listen: hung up; heard {Heard}", heard);
        }
    }

    /// <summary>An answered call, and the two things a script does on it: listen, and press keys.</summary>
    private sealed class FeatureCall(RTPSession rtp, PromptListener listener, Task hungUp, CancellationToken ct)
    {
        /// <summary>Waits for the PBX to finish talking. Sound heard before the call does not count.</summary>
        public async Task PromptAsync(string what, bool hangUpEnds = false)
        {
            listener.Listen();

            while (true)
            {
                if (hungUp.IsCompleted)
                {
                    if (hangUpEnds)
                    {
                        return;
                    }

                    throw new PbxFeatureException($"The PBX hung up before {what} had finished.");
                }

                switch (listener.State())
                {
                    case PromptListener.Heard.Ended:
                        return;

                    case PromptListener.Heard.NoSound:
                        throw new PbxFeatureException(
                            $"The PBX answered, but no sound reached the server while waiting for {what}. "
                            + "The call audio between the server and the PBX is blocked.");

                    case PromptListener.Heard.TooLong:
                        throw new PbxFeatureException($"The PBX kept talking during {what} and never stopped.");
                }

                await Task.Delay(50, ct);
            }
        }

        public async Task KeysAsync(string keys)
        {
            foreach (var key in keys)
            {
                await rtp.SendDtmf(Tone(key), ct);
                await Task.Delay(KeyGap, ct);
            }
        }
    }

    /// <summary>
    /// An RTP session with no microphone and no speaker: G.711 both ways, which
    /// every Issabel extension allows, and RFC 2833 events for the keys.
    /// </summary>
    public static RTPSession CreateMedia()
    {
        var rtp = new RTPSession(false, false, false);
        rtp.addTrack(new MediaStreamTrack(
            [new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU), new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA)]));

        // Across the VPN the PBX's audio may come from an address other than
        // the one in its SDP, as it does for the Agent App.
        rtp.AcceptRtpFromAny = true;

        return rtp;
    }

    /// <summary>The RFC 4733 event code of a key.</summary>
    public static byte Tone(char key) => key switch
    {
        >= '0' and <= '9' => (byte)(key - '0'),
        '*' => 10,
        '#' => 11,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "not a phone key"),
    };

    /// <summary>Silence, every 20 ms, until disposed or the call ends.</summary>
    private IDisposable SendSilence(RTPSession rtp, CancellationToken ct)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = stop.Token;

        // The codec the PBX chose. Silence is 0xFF in μ-law and 0xD5 in A-law;
        // the wrong one is a loud click every 20 ms.
        var alaw = rtp.AudioStream?.GetSendingFormat().ID == PromptListener.PcmaPayloadType;
        var packet = new byte[SamplesPerPacket];
        Array.Fill(packet, alaw ? (byte)0xD5 : (byte)0xFF);

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    if (!rtp.IsClosed)
                    {
                        rtp.SendAudio(SamplesPerPacket, packet);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The call is over.
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "PBX feature code: sending silence stopped");
            }
        }, CancellationToken.None);

        return new Stopper(stop);
    }

    private sealed class Stopper(CancellationTokenSource stop) : IDisposable
    {
        public void Dispose()
        {
            stop.Cancel();
            stop.Dispose();
        }
    }
}
