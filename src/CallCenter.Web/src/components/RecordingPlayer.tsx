import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { downloadRecording, fetchRecording } from '../api/calls'
import { downloadBlob } from '../lib/csv'
import { formatClock, readRecording } from '../lib/recordingWav'
import type { Listen, Recording } from '../lib/recordingWav'

type State =
  | { kind: 'loading' }
  | { kind: 'ready'; recording: Recording; url: string }
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
 *
 * Both voices are heard in both ears, though the file keeps them on separate
 * channels. "Customer" or "Agent" plays that side alone, for when the two
 * talked over each other; the switch carries on from the same moment.
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
  const [download, setDownload] = useState<'idle' | 'busy' | 'failed'>('idle')
  const [listen, setListen] = useState<Listen>('both')
  // Where to carry on from once the audio for another choice of side has loaded.
  const resume = useRef<{ time: number; play: boolean } | null>(null)

  useEffect(() => {
    const abort = new AbortController()

    void (async () => {
      try {
        const blob = await fetchRecording(communicationId, abort.signal)
        const parsed = readRecording(await readBlob(blob))
        if (abort.signal.aborted) return
        if (!parsed) {
          setState({ kind: 'unreadable' })
          return
        }
        setState({ kind: 'ready', recording: parsed, url: URL.createObjectURL(parsed.playable('both')) })
      } catch {
        if (!abort.signal.aborted) setState({ kind: 'failed' })
      }
    })()

    return () => abort.abort()
  }, [communicationId])

  // A customer's call is not held in memory once the supervisor has moved on,
  // or once another side is chosen. Let go after the player has the new audio,
  // never before, so it is not left reading from a revoked address. It does
  // not go on playing either: a media element taken out of the page is paused
  // by the browser.
  const url = state.kind === 'ready' ? state.url : null
  useEffect(() => (url ? () => URL.revokeObjectURL(url) : undefined), [url])

  // While playing, the bar follows the audio on every frame. The browser's
  // timeupdate event fires only about four times a second, so a bar driven by
  // it moves in visible jumps, and "On hold" appears up to a quarter of a
  // second late. timeupdate still covers the paused case: a seek, or the end.
  useEffect(() => {
    if (!playing) return
    let frame = 0
    const follow = () => {
      // Not while another side's audio loads: it starts at nought until resumed.
      if (audio.current && !resume.current) setPosition(audio.current.currentTime)
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

  const { recording } = state
  const { duration, holds } = recording
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

  function choose(next: Listen) {
    const element = audio.current
    if (next === listen || !element) return
    resume.current = { time: element.currentTime, play: !element.paused }
    setListen(next)
    setState({ kind: 'ready', recording, url: URL.createObjectURL(recording.playable(next)) })
  }

  function onLoaded(element: HTMLAudioElement) {
    const at = resume.current
    if (!at) return
    resume.current = null
    element.currentTime = at.time
    if (at.play) element.play().catch(() => setPlaying(false))
  }

  // A download that fails says so, beside the button, rather than doing
  // nothing at all (it used to be an unhandled rejection).
  async function onDownload() {
    setDownload('busy')
    try {
      downloadBlob(`call-${communicationId}.wav`, await downloadRecording(communicationId))
      setDownload('idle')
    } catch {
      setDownload('failed')
    }
  }

  return (
    <div className="space-y-2">
      <audio
        ref={audio}
        src={state.url}
        preload="auto"
        onLoadedMetadata={(e) => onLoaded(e.currentTarget)}
        onTimeUpdate={(e) => !playing && !resume.current && setPosition(e.currentTarget.currentTime)}
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

        <button type="button" className="btn-ghost btn-sm" disabled={download === 'busy'} onClick={() => void onDownload()}>
          {t('calls.recording.download')}
        </button>
      </div>

      {/* Who is heard. Only for two-sided recordings, which all of ours are. */}
      {recording.channels === 2 && (
        <div className="flex items-center gap-3">
          <span className="text-xs text-slate-400">{t('calls.recording.listen')}</span>
          <div role="group" aria-label={t('calls.recording.listen')} className="inline-flex rounded-md border border-ink-700">
            {(['both', 'customer', 'agent'] as const).map((value) => (
              <button
                key={value}
                type="button"
                onClick={() => choose(value)}
                aria-pressed={listen === value}
                className={`px-3 py-1 text-xs ${
                  listen === value ? 'bg-ink-800 text-slate-100' : 'text-slate-400 hover:text-slate-200'
                }`}
              >
                {t(`calls.recording.sides.${value}`)}
              </button>
            ))}
          </div>
        </div>
      )}

      {download === 'failed' && <p role="alert" className="notice-error">{t('calls.recording.failed')}</p>}

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
