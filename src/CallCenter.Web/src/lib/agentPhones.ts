import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiError } from '../api/client'
import { LISTEN_ID_HEADER, getAgentPhones, openListen, openSpeak, sendVoice, stopListen } from '../api/pbxAgents'
import { MicCapture, MicError, VoiceSender } from './micCapture'
import { PcmPlayer } from './pcmPlayer'

/**
 * The agents' phones as the PBX reports them (S-61), and listening in on a
 * call, or listening and speaking to the agent (S-62), for the Users page.
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
  /** Through *223: the supervisor's microphone goes to the agent, not the customer. */
  speak: boolean
  /** Speaking, with the microphone off for now. */
  muted: boolean
  startedAt?: number
  /** The server's code, a `MicProblem`, and the PBX's own words for `pbx_failed`. */
  code?: string
  error?: string
}

interface Session {
  controller: AbortController
  player: PcmPlayer | null
  mic: MicCapture | null
  sender: VoiceSender | null
  id: string | null
}

/**
 * One listen-in at a time. Starting another, pressing Stop, or leaving the
 * page ends the one before: the stream is aborted, which the server takes as
 * the end, and Stop is also sent by id in case the connection is slow to
 * close. The server hangs up its *222 or *223 call either way.
 *
 * **Listen & speak** asks for the microphone before anything is dialled, so a
 * refused microphone places no call. Its chunks wait for the server to name
 * the listen-in, and are posted to it from then on.
 */
export function useListen() {
  const [current, setCurrent] = useState<Listening | null>(null)
  const session = useRef<Session | null>(null)

  const end = useCallback(() => {
    const s = session.current
    session.current = null
    if (!s) return
    s.controller.abort()
    if (s.id) void stopListen(s.id).catch(() => undefined)
    s.sender?.close()
    s.mic?.close()
    s.player?.close()
  }, [])

  const start = useCallback(
    (agent: { userId: string; displayName: string }, speak = false) => {
      end()

      const controller = new AbortController()
      // Made here, in the click: browsers only start sound in answer to the user.
      let player: PcmPlayer | null = null
      try {
        player = new PcmPlayer()
      } catch {
        // No Web Audio; the call is still placed, and the bar says it is silent.
      }

      const mine: Session = { controller, player, mic: null, sender: null, id: null }
      session.current = mine
      setCurrent({ agentId: agent.userId, name: agent.displayName, status: 'connecting', speak, muted: false })

      const stillMine = () => session.current === mine
      const finish = () => {
        session.current = null
        mine.sender?.close()
        mine.mic?.close()
        player?.close()
      }

      void (async () => {
        try {
          if (speak) {
            const mic = await MicCapture.open((chunk) => mine.sender?.push(chunk))
            if (!stillMine()) {
              mic.close()
              return
            }
            mine.mic = mic
          }

          const response = await (speak ? openSpeak : openListen)(agent.userId, controller.signal)
          const id = response.headers.get(LISTEN_ID_HEADER)
          mine.id = id
          if (!stillMine()) return
          if (speak && id) mine.sender = new VoiceSender((bytes) => sendVoice(id, bytes))
          setCurrent((c) => (c ? { ...c, status: 'listening', startedAt: Date.now() } : c))

          const reader = response.body!.getReader()
          for (;;) {
            const { done, value } = await reader.read()
            if (done) break
            if (value) player?.push(value)
          }

          // The server ended it: the call is over, or the time limit.
          if (stillMine()) {
            finish()
            setCurrent((c) => (c ? { ...c, status: 'ended' } : c))
          }
        } catch (e) {
          if (controller.signal.aborted || !stillMine()) return
          finish()
          const body = e instanceof ApiError ? (e.body as { error?: string } | null) : null
          const code = e instanceof MicError ? e.code : e instanceof ApiError ? e.code ?? 'server_error' : 'server_unreachable'
          setCurrent((c) => (c ? { ...c, status: 'failed', code, error: body?.error } : c))
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

  /** Switches the microphone off or back on, without hanging up. */
  const toggleMute = useCallback(() => {
    const mic = session.current?.mic
    if (!mic) return
    mic.muted = !mic.muted
    setCurrent((c) => (c ? { ...c, muted: mic.muted } : c))
  }, [])

  return { current, start, stop, toggleMute }
}

