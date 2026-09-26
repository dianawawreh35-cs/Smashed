import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { LISTEN_ID_HEADER, getAgentPhones, openListen, stopListen } from '../api/pbxAgents'
import type { AgentPhone } from '../api/pbxAgents'
import { PcmPlayer } from '../lib/pcmPlayer'

/**
 * The agents' phones as the PBX reports them (S-61), and listening in on a
 * call (S-62), for the Users page.
 *
 * The server hears from the PBX within a second of a change; the page asks the
 * server every `PHONES_REFRESH_MS`, which is what a person watching notices.
 */
export const PHONES_REFRESH_MS = 3_000

export function useAgentPhones() {
  return useQuery({ queryKey: ['agentPhones'], queryFn: getAgentPhones, refetchInterval: PHONES_REFRESH_MS })
}

/** Re-renders every second, for the timers. */
function useNow(): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])
  return now
}

/** m:ss, or h:mm:ss past an hour. */
export function elapsed(fromMs: number, nowMs: number): string {
  const total = Math.max(0, Math.floor((nowMs - fromMs) / 1000))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = String(total % 60).padStart(2, '0')
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${s}` : `${m}:${s}`
}

/** Offline, free, ringing, or in a call with how long. */
export function PhoneBadge({ phone }: { phone: AgentPhone | undefined }) {
  const { t } = useTranslation()
  const now = useNow()

  if (!phone) return <span className="text-slate-400">—</span>

  switch (phone.state) {
    case 'InCall':
      return (
        <span className="badge-call tabular">
          {t('phones.states.InCall')}
          {phone.since && ` · ${elapsed(Date.parse(phone.since), now)}`}
        </span>
      )
    case 'Ringing':
      return <span className="badge-vip">{t('phones.states.Ringing')}</span>
    case 'Free':
      return <span className="badge-ok">{t('phones.states.Free')}</span>
    case 'Offline':
      return <span className="badge-muted">{t('phones.states.Offline')}</span>
    default:
      return <span className="badge-muted">{t('phones.states.Unknown')}</span>
  }
}

export interface Listening {
  agentId: string
  name: string
  status: 'connecting' | 'listening' | 'ended' | 'failed'
  startedAt?: number
  /** The server's code, and the PBX's own words for `pbx_failed`. */
  code?: string
  error?: string
}

/**
 * One listen-in at a time. Starting another, pressing Stop, or leaving the
 * page ends the one before: the stream is aborted, which the server takes as
 * the end, and Stop is also sent by id in case the connection is slow to
 * close. The server hangs up its *222 call either way.
 */
export function useListen() {
  const [current, setCurrent] = useState<Listening | null>(null)
  const session = useRef<{ controller: AbortController; player: PcmPlayer | null; id: string | null } | null>(null)

  const end = useCallback(() => {
    const s = session.current
    session.current = null
    if (!s) return
    s.controller.abort()
    if (s.id) void stopListen(s.id).catch(() => undefined)
    s.player?.close()
  }, [])

  const start = useCallback(
    (agent: { userId: string; displayName: string }) => {
      end()

      const controller = new AbortController()
      // Made here, in the click: browsers only start sound in answer to the user.
      let player: PcmPlayer | null = null
      try {
        player = new PcmPlayer()
      } catch {
        // No Web Audio; the call is still placed, and the bar says it is silent.
      }

      const mine = { controller, player, id: null as string | null }
      session.current = mine
      setCurrent({ agentId: agent.userId, name: agent.displayName, status: 'connecting' })

      const stillMine = () => session.current === mine

      void (async () => {
        try {
          const response = await openListen(agent.userId, controller.signal)
          mine.id = response.headers.get(LISTEN_ID_HEADER)
          if (!stillMine()) return
          setCurrent((c) => (c ? { ...c, status: 'listening', startedAt: Date.now() } : c))

          const reader = response.body!.getReader()
          for (;;) {
            const { done, value } = await reader.read()
            if (done) break
            if (value) player?.push(value)
          }

          // The server ended it: the call is over, or the time limit.
          if (stillMine()) {
            session.current = null
            player?.close()
            setCurrent((c) => (c ? { ...c, status: 'ended' } : c))
          }
        } catch (e) {
          if (controller.signal.aborted || !stillMine()) return
          session.current = null
          player?.close()
          const body = e instanceof ApiError ? (e.body as { error?: string } | null) : null
          setCurrent((c) =>
            c ? { ...c, status: 'failed', code: e instanceof ApiError ? e.code ?? 'server_error' : 'server_unreachable', error: body?.error } : c,
          )
        }
      })()
    },
    [end],
  )

  // Leaving the page stops listening.
  useEffect(() => end, [end])

  const stop = useCallback(() => {
    end()
    setCurrent(null)
  }, [end])

  return { current, start, stop }
}

/** Shown while listening in, with the one button that matters: Stop. */
export function ListenBar({ listening, onStop }: { listening: Listening | null; onStop: () => void }) {
  const { t } = useTranslation()
  const now = useNow()

  if (!listening) return null

  if (listening.status === 'failed') {
    return (
      <div role="alert" className="notice-error flex items-center justify-between gap-4">
        <span>
          {t(`phones.errors.${listening.code}`, {
            name: listening.name,
            error: listening.error ?? '',
            defaultValue: t('phones.errors.server_error'),
          })}
        </span>
        <button type="button" className="btn-ghost btn-sm" onClick={onStop}>
          {t('phones.close')}
        </button>
      </div>
    )
  }

  if (listening.status === 'ended') {
    return (
      <div role="status" className="card card-body flex items-center justify-between gap-4">
        <span>{t('phones.ended', { name: listening.name })}</span>
        <button type="button" className="btn-ghost btn-sm" onClick={onStop}>
          {t('phones.close')}
        </button>
      </div>
    )
  }

  return (
    <div role="status" className="card card-body flex items-center justify-between gap-4 border-brand-500/60">
      <span className="font-medium text-slate-100">
        {listening.status === 'connecting'
          ? t('phones.connecting', { name: listening.name })
          : t('phones.listening', { name: listening.name })}
        {listening.startedAt !== undefined && (
          <span className="tabular ms-2 text-slate-400">{elapsed(listening.startedAt, now)}</span>
        )}
      </span>
      <button type="button" className="btn-danger" onClick={onStop}>
        {t('phones.stop')}
      </button>
    </div>
  )
}
