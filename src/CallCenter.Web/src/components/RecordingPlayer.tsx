import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { downloadRecording, fetchRecording } from '../api/calls'
import { formatClock, readRecording } from '../lib/recordingWav'
import type { HoldPeriod } from '../lib/recordingWav'

type State =
  | { kind: 'loading' }
  | { kind: 'ready'; url: string; duration: number; holds: HoldPeriod[] }
  | { kind: 'unreadable' }
  | { kind: 'failed' }

/**
 * Plays one call's recording for a supervisor (S-04), and shows where the call
 * was on hold, as the Agent App's player does (A-51).
 *
 * A hold records as silence. The PBX plays its music to the customer and sends
 * the laptop nothing, and the agent's microphone is paused. Silence alone looks
 * like a dead line, so the holds the recorder wrote into the file are drawn as
 * amber marks under the seek bar, listed as times, and announced while playback
 * is inside one.
 *
 * The audio is fetched whole and decoded here (`readRecording`), because Chrome
 * and Edge will not play the mu-law the recorder writes.
 */
export default function RecordingPlayer({
  communicationId,
  hasRecording,
  expired,
}: {
  communicationId: string
  hasRecording: boolean
  expired: boolean
}) {
  const { t } = useTranslation()

  if (expired) return <p className="field-hint">{t('calls.recording.expired')}</p>
  if (!hasRecording) return <p className="field-hint">{t('calls.recording.none')}</p>

  // Keyed on the call, so opening another one starts a fresh player rather
  // than carrying the last one's position and state across.
  return <Player key={communicationId} communicationId={communicationId} />
}

function Player({ communicationId }: { communicationId: string }) {
  const { t } = useTranslation()
  const audio = useRef<HTMLAudioElement>(null)
  const [state, setState] = useState<State>({ kind: 'loading' })
  const [position, setPosition] = useState(0)
  const [playing, setPlaying] = useState(false)

  useEffect(() => {
    const abort = new AbortController()
    let url: string | null = null

    void (async () => {
      try {
        const blob = await fetchRecording(communicationId, abort.signal)
        const parsed = readRecording(await readBlob(blob))
        if (abort.signal.aborted) return
        if (!parsed) {
          setState({ kind: 'unreadable' })
          return
        }
        url = URL.createObjectURL(parsed.playable)
        setState({ kind: 'ready', url, duration: parsed.duration, holds: parsed.holds })
      } catch {
        if (!abort.signal.aborted) setState({ kind: 'failed' })
      }
    })()

    // A customer's call is not held in memory once the supervisor has moved
    // on. It does not go on playing either: a media element taken out of the
    // page is paused by the browser.
    return () => {
      abort.abort()
      if (url) URL.revokeObjectURL(url)
    }
  }, [communicationId])

  // While playing, the bar follows the audio on every frame. The browser's
  // timeupdate event fires only about four times a second, so a bar driven by
  // it moves in visible jumps, and "On hold" appears up to a quarter of a
  // second late. timeupdate still covers the paused case: a seek, or the end.
  useEffect(() => {
    if (!playing) return
    let frame = 0
    const follow = () => {
      if (audio.current) setPosition(audio.current.currentTime)
      frame = requestAnimationFrame(follow)
    }
    frame = requestAnimationFrame(follow)
    return () => cancelAnimationFrame(frame)
  }, [playing])

  // The player's own height, so it takes the place of this line without a jump.
  if (state.kind === 'loading') {
    return <p className="field-hint flex min-h-[2.5rem] items-center">{t('calls.recording.loading')}</p>
  }
  if (state.kind === 'unreadable') return <p className="notice-error">{t('calls.recording.unreadable')}</p>
  if (state.kind === 'failed') return <p className="notice-error">{t('calls.recording.failed')}</p>

  const { url, duration, holds } = state
  const atHold = holds.some((h) => position >= h.start && position < h.end)

  function toggle() {
    const element = audio.current
    if (!element) return
    if (element.paused) void element.play()
    else element.pause()
  }

  function seek(seconds: number) {
    if (audio.current) audio.current.currentTime = seconds
    setPosition(seconds)
  }

  async function download() {
    const blob = await downloadRecording(communicationId)
    const link = document.createElement('a')
    link.href = URL.createObjectURL(blob)
    link.download = `call-${communicationId}.wav`
    link.click()
    // Revoked a moment later: straight away can cancel the save in some browsers.
    setTimeout(() => URL.revokeObjectURL(link.href), 10_000)
  }

  return (
    <div className="space-y-2">
      <audio
        ref={audio}
        src={url}
        preload="auto"
        onTimeUpdate={(e) => !playing && setPosition(e.currentTarget.currentTime)}
        onPlay={() => setPlaying(true)}
        onPause={() => setPlaying(false)}
        onEnded={() => setPlaying(false)}
      />

      <div className="flex items-center gap-4">
        <button type="button" className="btn-primary min-w-[90px]" onClick={toggle}>
          {playing ? t('calls.recording.pause') : t('calls.recording.play')}
        </button>

        <div className="flex-1">
          <input
            type="range"
            aria-label={t('calls.recording.position')}
            className="w-full accent-brand-500"
            min={0}
            max={duration}
            step="any"
            value={Math.min(position, duration)}
            onChange={(e) => seek(Number(e.target.value))}
          />
          {/* Where the call was on hold, under the bar. inset-inline-start, so
              in Arabic the marks run from the right, as the bar itself does. */}
          {holds.length > 0 && (
            <div className="relative mx-2 h-1" data-testid="hold-band">
              {holds.map((h) => (
                <span
                  key={h.start}
                  className="absolute top-0 h-1 rounded-sm bg-amber-400"
                  style={{
                    insetInlineStart: `${(h.start / duration) * 100}%`,
                    width: `${((h.end - h.start) / duration) * 100}%`,
                  }}
                  title={`${formatClock(h.start)}–${formatClock(h.end)}`}
                />
              ))}
            </div>
          )}
        </div>

        <div className="flex items-center gap-3">
          {/* Said while playback is inside a hold, so the silence is not
              mistaken for a dead line. */}
          {atHold && <span className="text-xs font-medium text-amber-400">{t('calls.recording.holdNow')}</span>}
          {/* Digits read left to right in both languages. */}
          <span className="tabular text-sm text-slate-400" dir="ltr">
            {formatClock(position)} / {formatClock(duration)}
          </span>
        </div>

        <button type="button" className="btn-ghost btn-sm" onClick={() => void download()}>
          {t('calls.recording.download')}
        </button>
      </div>

      {/* Every hold, as times: the marks say where, this says exactly when. */}
      {holds.length > 0 && (
        <p className="text-xs text-amber-400">
          {t('calls.recording.onHold')}{' '}
          <span dir="ltr">{holds.map((h) => `${formatClock(h.start)}–${formatClock(h.end)}`).join(', ')}</span>
        </p>
      )}
    </div>
  )
}

/** The file's bytes. FileReader rather than Blob.arrayBuffer(), which older engines lack. */
function readBlob(blob: Blob): Promise<ArrayBuffer> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as ArrayBuffer)
    reader.onerror = () => reject(reader.error)
    reader.readAsArrayBuffer(blob)
  })
}
