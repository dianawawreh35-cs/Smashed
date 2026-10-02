import { describe, expect, it } from 'vitest'
import { CHUNK_SAMPLES, Downsampler, MAX_PENDING_CHUNKS, VoiceSender } from './micCapture'

/** The microphone's voice for a *223 listen-in (S-62), without a microphone. */
describe('Downsampler', () => {
  const samplesOf = (chunk: Uint8Array) => {
    const view = new DataView(chunk.buffer, chunk.byteOffset, chunk.length)
    return Array.from({ length: chunk.length / 2 }, (_, i) => view.getInt16(i * 2, true))
  }

  it('turns 48 kHz into 100 ms chunks of 16-bit 8 kHz', () => {
    const chunks: Uint8Array[] = []
    const down = new Downsampler(48_000, (c) => chunks.push(c))

    // 100 ms at 48 kHz, in the 2048-sample pieces the browser hands over.
    const second = new Float32Array(4800).fill(0.5)
    down.push(second.subarray(0, 2048))
    down.push(second.subarray(2048, 4096))
    expect(chunks).toHaveLength(0)
    down.push(second.subarray(4096))

    expect(chunks).toHaveLength(1)
    expect(chunks[0].length).toBe(CHUNK_SAMPLES * 2)
    expect(samplesOf(chunks[0]).every((s) => s === Math.round(0.5 * 32767))).toBe(true)
  })

  it('keeps time at 44.1 kHz, which is not a whole multiple of 8 kHz', () => {
    const chunks: Uint8Array[] = []
    const down = new Downsampler(44_100, (c) => chunks.push(c))

    // A second, and a little over, so rounding cannot leave the tenth chunk short.
    down.push(new Float32Array(44_200))

    expect(chunks).toHaveLength(10)
  })

  it('averages away what 8 kHz cannot carry, and clips what is too loud', () => {
    const chunks: Uint8Array[] = []
    const down = new Downsampler(48_000, (c) => chunks.push(c))

    // 24 kHz, the loudest the microphone can give: nothing of it is speech.
    down.push(Float32Array.from({ length: 4800 }, (_, i) => (i % 2 === 0 ? 1 : -1)))
    expect(samplesOf(chunks[0]).every((s) => s === 0)).toBe(true)

    down.push(new Float32Array(4800).fill(3))
    expect(samplesOf(chunks[1]).every((s) => s === 32767)).toBe(true)
  })

  it('starts again from nothing after a reset', () => {
    const chunks: Uint8Array[] = []
    const down = new Downsampler(48_000, (c) => chunks.push(c))

    down.push(new Float32Array(4000))
    down.reset()
    down.push(new Float32Array(4000))
    expect(chunks).toHaveLength(0)
  })
})

describe('VoiceSender', () => {
  const flush = () => new Promise((resolve) => setTimeout(resolve, 0))
  const chunk = (n: number) => new Uint8Array([n, 0])

  it('posts one at a time, in order, joining what came in meanwhile', async () => {
    const posts: number[][] = []
    let release: () => void = () => undefined
    const sender = new VoiceSender((bytes) => {
      posts.push(Array.from(bytes))
      return new Promise<void>((resolve) => (release = resolve))
    })

    sender.push(chunk(1))
    sender.push(chunk(2))
    sender.push(chunk(3))
    expect(posts).toEqual([[1, 0]])

    release()
    await flush()
    expect(posts).toEqual([[1, 0], [2, 0, 3, 0]])
  })

  it('drops the oldest past a second, and a failed post is only a gap', async () => {
    const posts: number[][] = []
    let release: () => void = () => undefined
    let fail = true
    const sender = new VoiceSender((bytes) => {
      posts.push(Array.from(bytes))
      return fail ? Promise.reject(new Error('gone')) : new Promise<void>((resolve) => (release = resolve))
    })

    sender.push(chunk(0))
    await flush()
    fail = false

    sender.push(chunk(1))
    for (let i = 2; i <= MAX_PENDING_CHUNKS + 3; i++) sender.push(chunk(i))
    release()
    await flush()

    expect(posts[1]).toEqual([1, 0])
    const kept = posts[2].filter((_, i) => i % 2 === 0)
    expect(kept).toHaveLength(MAX_PENDING_CHUNKS)
    expect(kept[kept.length - 1]).toBe(MAX_PENDING_CHUNKS + 3)
  })

  it('sends nothing more once closed', () => {
    const posts: unknown[] = []
    const sender = new VoiceSender((bytes) => (posts.push(bytes), new Promise(() => undefined)))
    sender.push(chunk(1))
    sender.close()
    sender.push(chunk(2))
    expect(posts).toHaveLength(1)
  })
})
