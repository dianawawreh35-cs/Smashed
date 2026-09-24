import { describe, expect, it } from 'vitest'
import { formatClock, readRecording } from './recordingWav'

/**
 * The same cases as the Agent App's `RecordingWavTests`, because the two read
 * the same files (docs/SCHEMA.md).
 */

/** A recording as the Agent App's recorder writes one: 58-byte header, audio, then holds. */
function recording(seconds: number, holds: Array<[number, number]> = [], extra?: Uint8Array): ArrayBuffer {
  const channels = 2
  const rate = 8000
  const audio = rate * channels * seconds
  const holdBytes = holds.length ? 8 + holds.length * 8 : 0
  const extraBytes = extra ? extra.length : 0
  const buffer = new ArrayBuffer(58 + extraBytes + audio + holdBytes)
  const view = new DataView(buffer)
  const ascii = (at: number, text: string) => {
    for (let i = 0; i < text.length; i++) view.setUint8(at + i, text.charCodeAt(i))
  }

  ascii(0, 'RIFF')
  view.setUint32(4, buffer.byteLength - 8, true)
  ascii(8, 'WAVE')
  ascii(12, 'fmt ')
  view.setUint32(16, 18, true)
  view.setUint16(20, 7, true) // mu-law
  view.setUint16(22, channels, true)
  view.setUint32(24, rate, true)
  view.setUint32(28, rate * channels, true)
  view.setUint16(32, channels, true)
  view.setUint16(34, 8, true)
  view.setUint16(36, 0, true)
  ascii(38, 'fact')
  view.setUint32(42, 4, true)
  view.setUint32(46, rate * seconds, true)

  let at = 50
  if (extra) {
    new Uint8Array(buffer, at, extra.length).set(extra)
    at += extra.length
  }

  ascii(at, 'data')
  view.setUint32(at + 4, audio, true)
  new Uint8Array(buffer, at + 8, audio).fill(0xff) // mu-law silence
  at += 8 + audio

  if (holds.length) {
    ascii(at, 'hold')
    view.setUint32(at + 4, holds.length * 8, true)
    holds.forEach(([start, frames], i) => {
      view.setUint32(at + 8 + i * 8, start, true)
      view.setUint32(at + 12 + i * 8, frames, true)
    })
  }

  return buffer
}

/** jsdom's Blob has no arrayBuffer(); FileReader it does have. */
function readBlob(blob: Blob): Promise<ArrayBuffer> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as ArrayBuffer)
    reader.onerror = () => reject(reader.error)
    reader.readAsArrayBuffer(blob)
  })
}

describe('readRecording', () => {
  it('finds the audio and says how long it is', () => {
    const parsed = readRecording(recording(3))!

    expect(parsed.channels).toBe(2)
    expect(parsed.sampleRate).toBe(8000)
    expect(parsed.duration).toBe(3)
    expect(parsed.holds).toEqual([])
  })

  it('reads the holds, in order, as seconds', () => {
    // Written out of order on purpose: the reader sorts.
    const parsed = readRecording(recording(10, [[48000, 8000], [8000, 12000]]))!

    expect(parsed.holds).toEqual([
      { start: 1, end: 2.5 },
      { start: 6, end: 7 },
    ])
  })

  it('cuts a hold that outlasts the audio, as when a call is hung up on hold', () => {
    const parsed = readRecording(recording(4, [[24000, 80000]]))!

    expect(parsed.holds).toEqual([{ start: 3, end: 4 }])
  })

  it('reads a file with a chunk it does not know before the audio', () => {
    const list = new Uint8Array([0x4c, 0x49, 0x53, 0x54, 4, 0, 0, 0, 1, 2, 3, 4]) // LIST, 4 bytes
    const parsed = readRecording(recording(2, [[8000, 4000]], list))!

    expect(parsed.duration).toBe(2)
    expect(parsed.holds).toEqual([{ start: 1, end: 1.5 }])
  })

  it('turns mu-law into 16-bit PCM the browser can play, silence staying silent', async () => {
    const parsed = readRecording(recording(1))!
    const bytes = new DataView(await readBlob(parsed.playable))

    expect(parsed.playable.type).toBe('audio/wav')
    expect(bytes.getUint16(20, true)).toBe(1) // PCM
    expect(bytes.getUint16(34, true)).toBe(16)
    expect(bytes.getUint32(40, true)).toBe(8000 * 2 * 2) // samples x channels x 2 bytes
    // 0xFF is mu-law silence; it must decode to zero, not to a buzz.
    expect(bytes.getInt16(44, true)).toBe(0)
  })

  it('decodes mu-law to the standard G.711 values', async () => {
    const file = recording(1)
    const audio = new Uint8Array(file, 58, 4) // data starts at 58, as the recorder writes it
    audio.set([0x00, 0x80, 0x7f, 0x8f])
    const bytes = new DataView(await readBlob(readRecording(file)!.playable))

    expect(bytes.getInt16(44, true)).toBe(-32124) // the loudest negative
    expect(bytes.getInt16(46, true)).toBe(32124) // the loudest positive
    expect(bytes.getInt16(48, true)).toBe(0) // 0x7F is the other silence
    expect(bytes.getInt16(50, true)).toBe(16764) // exponent 7, mantissa 0
  })

  it('refuses what is not a mu-law WAV rather than playing static', () => {
    expect(readRecording(new ArrayBuffer(4))).toBeNull()

    const pcm = recording(1)
    new DataView(pcm).setUint16(20, 1, true) // PCM, not mu-law
    expect(readRecording(pcm)).toBeNull()
  })
})

describe('formatClock', () => {
  it('reads as a call length', () => {
    expect(formatClock(0)).toBe('0:00')
    expect(formatClock(70)).toBe('1:10')
    expect(formatClock(3725)).toBe('1:02:05')
  })
})
