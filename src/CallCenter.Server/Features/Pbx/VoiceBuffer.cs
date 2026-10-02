using System.Buffers.Binary;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// The supervisor's voice on a <c>*223</c> listen-in (S-62), between the
/// browser, which sends it in bursts of about 100 ms, and the PBX, which takes
/// one 20 ms packet every 20 ms.
/// </summary>
/// <remarks>
/// <b>A little behind, on purpose,</b> as the browser's player is for the
/// other direction. After it has run dry it waits for <see cref="Lead"/>
/// samples before giving any out, so a burst that arrives a little late leaves
/// no gap. Past <see cref="MaxSamples"/> the oldest go: the agent hears the
/// supervisor now, not a second ago.
/// </remarks>
public sealed class VoiceBuffer
{
    /// <summary>What is held back after the buffer has run dry: 120 ms at 8 kHz.</summary>
    public const int Lead = 960;

    /// <summary>The most that waits: one second at 8 kHz.</summary>
    public const int MaxSamples = 8000;

    private readonly Lock _gate = new();
    private readonly Queue<short> _samples = new();
    private bool _playing;

    /// <summary>Samples waiting.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _samples.Count;
            }
        }
    }

    /// <summary>Adds 16-bit little-endian samples. An odd last byte is left out.</summary>
    public void Write(ReadOnlySpan<byte> pcm)
    {
        lock (_gate)
        {
            for (var i = 0; i + 1 < pcm.Length; i += 2)
            {
                _samples.Enqueue(BinaryPrimitives.ReadInt16LittleEndian(pcm[i..]));
            }

            while (_samples.Count > MaxSamples)
            {
                _samples.Dequeue();
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="packet"/> with the next samples, or returns false
    /// when there is nothing to say yet and silence should go instead.
    /// </summary>
    public bool Read(Span<short> packet)
    {
        lock (_gate)
        {
            if (!_playing && _samples.Count < Lead)
            {
                return false;
            }

            if (_samples.Count < packet.Length)
            {
                // Ran dry: what is left is dropped with the gap, and the next
                // words wait for the lead again.
                _samples.Clear();
                _playing = false;
                return false;
            }

            _playing = true;
            for (var i = 0; i < packet.Length; i++)
            {
                packet[i] = _samples.Dequeue();
            }

            return true;
        }
    }
}
