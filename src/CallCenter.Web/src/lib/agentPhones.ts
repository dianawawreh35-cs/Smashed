import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiError } from '../api/client'
import { LISTEN_ID_HEADER, getAgentPhones, openListen, stopListen } from '../api/pbxAgents'
import { PcmPlayer } from './pcmPlayer'

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

/** m:ss, or h:mm:ss past an hour. */
export function elapsed(fromMs: number, nowMs: number): string {
  const total = Math.max(0, Math.floor((nowMs - fromMs) / 1000))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = String(total % 60).padStart(2, '0')
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${s}` : `${m}:${s}`
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

