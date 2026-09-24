/**
 * Reads a call recording in the browser (S-04): where the audio is, when the
 * call was on hold, and the audio itself in a form the browser can play.
 *
 * The files are the ones the Agent App's recorder writes: 8 kHz G.711 mu-law
 * WAV, customer on the left channel and agent on the right, with a `hold` chunk
 * after the audio. **Chrome and Edge do not play mu-law WAV**, so the audio is
 * decoded here to 16-bit PCM, the one WAV encoding every browser plays, and
 * handed to an ordinary `<audio>` element. That keeps the native seeking and
 * timing, and needs nothing from the server but the file.
 *
 * This is the same reading as `RecordingWav` in the Agent App, written a second
 * time because the two apps share no code. The layout it relies on is in
 * `docs/SCHEMA.md`, and the tests here and there check the same cases. Change
 * one, change the other.
 */

/** `WAVE_FORMAT_MULAW`: the format tag of every recording. */
const MU_LAW = 7

/** One stretch of the recording during which the call was on hold, in seconds. */
export interface HoldPeriod {
  start: number
  end: number
}

export interface Recording {
  channels: number
  sampleRate: number
  /** Seconds of audio. */
  duration: number
  /** In order, cut to the audio there is. Empty for a call never held. */
  holds: HoldPeriod[]
  /** The same audio as 16-bit PCM WAV, ready for an `<audio>` element. */
  playable: Blob
}

function id(view: DataView, at: number): string {
  return String.fromCharCode(
    view.getUint8(at), view.getUint8(at + 1), view.getUint8(at + 2), view.getUint8(at + 3),
  )
}

/** G.711 mu-law to 16-bit linear, all 256 values worked out once. */
const MU_LAW_TABLE = (() => {
  const table = new Int16Array(256)
  for (let i = 0; i < 256; i++) {
    const u = ~i & 0xff
    const exponent = (u >> 4) & 0x07
    const magnitude = (((u & 0x0f) << 3) + 0x84) << exponent
    table[i] = u & 0x80 ? 0x84 - magnitude : magnitude - 0x84
  }
  return table
})()

/**
 * Finds the audio and the holds in a recording, or returns null for anything
 * that is not one of ours, so the screen can say so rather than play static.
 *
 * Chunks are walked, not assumed: a file that has been through an editor or a
 * restored backup may carry a `LIST` chunk, and reading the audio from a fixed
 * offset would play the header as noise.
 */
export function readRecording(file: ArrayBuffer): Recording | null {
  const view = new DataView(file)
  if (file.byteLength < 12 || id(view, 0) !== 'RIFF' || id(view, 8) !== 'WAVE') return null

  let channels: number | null = null
  let sampleRate: number | null = null
  let dataOffset: number | null = null
  let dataLength = 0
  const rawHolds: Array<[number, number]> = []

  let position = 12
  while (position + 8 <= file.byteLength) {
    const chunk = id(view, position)
    const size = view.getUint32(position + 4, true)
    const body = position + 8

    if (chunk === 'fmt ') {
      if (size < 16 || body + 16 > file.byteLength) return null
      const format = view.getUint16(body, true)
      const bits = view.getUint16(body + 14, true)
      if (format !== MU_LAW || bits !== 8) return null
      channels = view.getUint16(body + 2, true)
      sampleRate = view.getUint32(body + 4, true)
    } else if (chunk === 'data') {
      // The format has to come first: without it the bytes mean nothing.
      if ((channels !== 1 && channels !== 2) || !sampleRate) return null
      // A file cut short in transfer still plays up to where it stops.
      const length = Math.min(size, file.byteLength - body)
      dataOffset = body
      dataLength = length - (length % channels)
    } else if (chunk === 'hold') {
      const end = body + Math.min(size, file.byteLength - body)
      for (let at = body; at + 8 <= end; at += 8) {
        rawHolds.push([view.getUint32(at, true), view.getUint32(at + 4, true)])
      }
    }

    // Chunks are padded to an even length. One that runs past the end is where
    // the file was cut short; whatever came before it stands.
    const next = body + size + (size % 2)
    if (next > file.byteLength) break
    position = next
  }

  if (dataOffset === null || dataLength <= 0 || !channels || !sampleRate) return null

  const frames = dataLength / channels

  return {
    channels,
    sampleRate,
    duration: frames / sampleRate,
    holds: trimHolds(rawHolds, frames, sampleRate),
    playable: toPcmWav(new Uint8Array(file, dataOffset, dataLength), channels, sampleRate),
  }
}

/**
 * Holds as seconds, in order, cut to the audio. A call hung up while on hold
 * has a hold that outlasts its audio, because nothing arrived to pad it out.
 */
function trimHolds(holds: Array<[number, number]>, frames: number, sampleRate: number): HoldPeriod[] {
  return [...holds]
    .sort((a, b) => a[0] - b[0])
    .map(([start, length]) => [start, Math.min(start + length, frames)] as const)
    .filter(([start, end]) => start < frames && end > start)
    .map(([start, end]) => ({ start: start / sampleRate, end: end / sampleRate }))
}

/** Mu-law samples as a 16-bit PCM WAV file: a 44-byte header, then the samples. */
function toPcmWav(mulaw: Uint8Array, channels: number, sampleRate: number): Blob {
  const pcmBytes = mulaw.length * 2
  const buffer = new ArrayBuffer(44 + pcmBytes)
  const view = new DataView(buffer)
  const ascii = (at: number, text: string) => {
    for (let i = 0; i < text.length; i++) view.setUint8(at + i, text.charCodeAt(i))
  }

  ascii(0, 'RIFF')
  view.setUint32(4, 36 + pcmBytes, true)
  ascii(8, 'WAVE')
  ascii(12, 'fmt ')
  view.setUint32(16, 16, true)
  view.setUint16(20, 1, true) // PCM
  view.setUint16(22, channels, true)
  view.setUint32(24, sampleRate, true)
  view.setUint32(28, sampleRate * channels * 2, true)
  view.setUint16(32, channels * 2, true)
  view.setUint16(34, 16, true)
  ascii(36, 'data')
  view.setUint32(40, pcmBytes, true)

  const samples = new Int16Array(buffer, 44)
  for (let i = 0; i < mulaw.length; i++) samples[i] = MU_LAW_TABLE[mulaw[i]]

  return new Blob([buffer], { type: 'audio/wav' })
}

/** m:ss, or h:mm:ss for a call over an hour. */
export function formatClock(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = String(total % 60).padStart(2, '0')
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${s}` : `${m}:${s}`
}
