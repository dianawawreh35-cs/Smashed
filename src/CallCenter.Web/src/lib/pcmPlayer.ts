/**
 * Plays a call's sound as it arrives from the server (S-62): 16-bit
 * little-endian mono samples at 8 kHz, in chunks of any size.
 *
 * Each chunk becomes a short AudioBuffer, queued to start where the last one
 * ends. The browser resamples 8 kHz to the speaker's rate itself.
 *
 * **A little behind, on purpose.** Playback starts `LEAD` seconds after the
 * first sound, so a packet arriving a little late does not leave a gap. If the
 * network stalls and the queue runs dry, it starts again that far behind. If
 * it falls more than `MAX_BEHIND` behind, chunks are dropped until it catches
 * up: a supervisor wants to hear the call now, not a minute ago.
 *
 * Create it from a click: browsers only let a page start sound in answer to
 * the user.
 */
export const SAMPLE_RATE = 8000
const LEAD = 0.25
const MAX_BEHIND = 1.5

export class PcmPlayer {
  private readonly ctx: AudioContext
  private next = 0
  private odd: number | null = null

  constructor() {
    this.ctx = new AudioContext()
  }

  /** Queues a chunk. A chunk may split a sample; the odd byte waits for the next. */
  push(chunk: Uint8Array): void {
    const bytes = this.odd === null ? chunk : prepend(this.odd, chunk)
    this.odd = bytes.length % 2 === 1 ? bytes[bytes.length - 1] : null

    const samples = toSamples(bytes)
    if (samples.length === 0) return

    const now = this.ctx.currentTime
    if (this.next < now) this.next = now + LEAD
    if (this.next - now > MAX_BEHIND) return

    const buffer = this.ctx.createBuffer(1, samples.length, SAMPLE_RATE)
    buffer.getChannelData(0).set(samples)

    const source = this.ctx.createBufferSource()
    source.buffer = buffer
    source.connect(this.ctx.destination)
    source.start(this.next)
    this.next += buffer.duration
  }

  close(): void {
    void this.ctx.close().catch(() => undefined)
  }
}

/** 16-bit little-endian samples as floats from -1 to 1. An odd last byte is left out. */
export function toSamples(bytes: Uint8Array): Float32Array {
  const count = Math.floor(bytes.length / 2)
  const view = new DataView(bytes.buffer, bytes.byteOffset, count * 2)
  const samples = new Float32Array(count)
  for (let i = 0; i < count; i++) samples[i] = view.getInt16(i * 2, true) / 32768
  return samples
}

function prepend(first: number, rest: Uint8Array): Uint8Array {
  const joined = new Uint8Array(rest.length + 1)
  joined[0] = first
  joined.set(rest, 1)
  return joined
}
