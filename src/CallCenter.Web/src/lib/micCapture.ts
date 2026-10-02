/**
 * The supervisor's microphone on a *223 listen-in (S-62): what they say, as
 * the server wants it, 16-bit little-endian mono at 8 kHz, 100 ms at a time.
 *
 * **Only on a secure page.** Chrome and Edge offer the microphone only to an
 * `https://` address or to the computer itself; at `http://192.168.1.100`
 * they do not even ask. The supervisor's PC is told to treat that address as
 * secure (runbook, "Supervisor PCs: the microphone"); without that,
 * `navigator.mediaDevices` is missing and this says so.
 *
 * The browser's own echo cancellation is asked for, so the call coming out of
 * the speakers is not sent back to the agent. A headset is still better.
 */
import { SAMPLE_RATE } from './pcmPlayer'

/** 100 ms at 8 kHz: one post to the server. */
export const CHUNK_SAMPLES = 800

/** Why the microphone could not be had, as `phones.errors` names it. */
export type MicProblem = 'microphone_insecure' | 'microphone_denied' | 'microphone_missing'

export class MicError extends Error {
  constructor(readonly code: MicProblem) {
    super(code)
    this.name = 'MicError'
  }
}

/**
 * Turns the microphone's rate, 48 kHz as a rule, into 8 kHz by averaging the
 * samples that fall in each output sample, which also takes off most of what
 * 8 kHz cannot carry. Hands out whole chunks of `CHUNK_SAMPLES`.
 */
export class Downsampler {
  private readonly step: number
  private readonly chunk = new DataView(new ArrayBuffer(CHUNK_SAMPLES * 2))
  private filled = 0
  private sum = 0
  private count = 0
  private position = 0

  constructor(
    inputRate: number,
    private readonly onChunk: (chunk: Uint8Array<ArrayBuffer>) => void,
  ) {
    this.step = inputRate / SAMPLE_RATE
  }

  push(input: Float32Array): void {
    for (const x of input) {
      this.sum += x
      this.count++
      this.position++
      if (this.position >= this.step) {
        this.position -= this.step
        this.emit(this.sum / this.count)
        this.sum = 0
        this.count = 0
      }
    }
  }

  /** Forgets a part-made chunk, so unmuting does not start with words from before. */
  reset(): void {
    this.filled = 0
    this.sum = 0
    this.count = 0
    this.position = 0
  }

  private emit(sample: number): void {
    const clamped = Math.max(-1, Math.min(1, sample))
    this.chunk.setInt16(this.filled * 2, Math.round(clamped * 32767), true)
    if (++this.filled === CHUNK_SAMPLES) {
      this.onChunk(new Uint8Array(this.chunk.buffer.slice(0)))
      this.filled = 0
    }
  }
}

export class MicCapture {
  private _muted = false

  private constructor(
    private readonly stream: MediaStream,
    private readonly ctx: AudioContext,
    private readonly downsampler: Downsampler,
  ) {}

  /**
   * Asks for the microphone (the browser's prompt, the first time) and starts
   * handing out chunks. Throws a `MicError` when it cannot.
   */
  static async open(onChunk: (chunk: Uint8Array<ArrayBuffer>) => void): Promise<MicCapture> {
    if (!navigator.mediaDevices?.getUserMedia) throw new MicError('microphone_insecure')

    let stream: MediaStream
    try {
      stream = await navigator.mediaDevices.getUserMedia({
        audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
      })
    } catch (e) {
      const name = e instanceof DOMException ? e.name : ''
      throw new MicError(name === 'NotAllowedError' || name === 'SecurityError' ? 'microphone_denied' : 'microphone_missing')
    }

    const ctx = new AudioContext()
    const downsampler = new Downsampler(ctx.sampleRate, onChunk)
    const capture = new MicCapture(stream, ctx, downsampler)

    // A ScriptProcessor rather than an AudioWorklet: one file, and 2048
    // samples (about 40 ms) at a time is plenty for 100 ms chunks. It only
    // runs connected to the output, so through a gain of 0: the supervisor
    // must not hear themselves.
    const source = ctx.createMediaStreamSource(stream)
    const processor = ctx.createScriptProcessor(2048, 1, 1)
    const mute = ctx.createGain()
    mute.gain.value = 0
    processor.onaudioprocess = (e) => {
      if (!capture._muted) downsampler.push(e.inputBuffer.getChannelData(0))
    }
    source.connect(processor)
    processor.connect(mute)
    mute.connect(ctx.destination)

    // Made after the browser's prompt, not in the click itself, so it may
    // start suspended; a suspended one hands out nothing.
    if (ctx.state === 'suspended') void ctx.resume().catch(() => undefined)

    return capture
  }

  get muted(): boolean {
    return this._muted
  }

  set muted(value: boolean) {
    this._muted = value
    this.downsampler.reset()
  }

  /** Lets go of the microphone, so the browser's recording light goes off. */
  close(): void {
    for (const track of this.stream.getTracks()) track.stop()
    void this.ctx.close().catch(() => undefined)
  }
}

/** The most that waits to be sent: one second, as much as the server takes in one post. */
export const MAX_PENDING_CHUNKS = 10

/**
 * Posts the chunks to the server one request at a time, in order. Whatever
 * arrives while a post is out goes in the next one together. A connection
 * that falls behind loses the oldest, past `MAX_PENDING_CHUNKS`: the agent
 * hears the supervisor now, not a second ago. A post that fails is a gap,
 * not a retry.
 */
export class VoiceSender {
  private pending: Uint8Array<ArrayBuffer>[] = []
  private busy = false
  private closed = false

  constructor(private readonly send: (bytes: Uint8Array<ArrayBuffer>) => Promise<unknown>) {}

  push(chunk: Uint8Array<ArrayBuffer>): void {
    if (this.closed) return
    this.pending.push(chunk)
    if (this.pending.length > MAX_PENDING_CHUNKS) this.pending.splice(0, this.pending.length - MAX_PENDING_CHUNKS)
    void this.pump()
  }

  close(): void {
    this.closed = true
    this.pending = []
  }

  private async pump(): Promise<void> {
    if (this.busy) return
    this.busy = true
    try {
      while (this.pending.length > 0 && !this.closed) {
        const bytes = join(this.pending.splice(0))
        await this.send(bytes).catch(() => undefined)
      }
    } finally {
      this.busy = false
    }
  }
}

function join(chunks: Uint8Array<ArrayBuffer>[]): Uint8Array<ArrayBuffer> {
  if (chunks.length === 1) return chunks[0]
  const joined = new Uint8Array(chunks.reduce((n, c) => n + c.length, 0))
  let at = 0
  for (const c of chunks) {
    joined.set(c, at)
    at += c.length
  }
  return joined
}
