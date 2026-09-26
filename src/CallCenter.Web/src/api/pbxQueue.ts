/**
 * Opening and closing the queue (S-60). Mirrors
 * `CallCenter.Shared.Contracts.Pbx.QueueStatusDto` — hand-written, so a
 * change on the server has to be copied across.
 */
import { api } from './client'

export interface QueueStatus {
  /** Null until a supervisor has said which it is: *280 switches either way. */
  isOpen: boolean | null
  changedAt: string | null
  /** Null when the last change was the daily opening. */
  changedBy: string | null
  changedAutomatically: boolean
  /** The server's extension is set up, so the switch can dial. */
  configured: boolean
  /** The daily opening time, HH:mm; null when it is off. */
  autoOpenAt: string | null
  /** Why today's opening did not happen, if it did not. */
  autoOpenProblem: string | null
}

export type QueueFailure = 'not_configured' | 'unknown_state' | 'busy' | 'pbx_failed'

export interface QueueSwitchResult {
  ok: boolean
  code: QueueFailure | null
  /** What the PBX did, in the server's words. */
  error: string | null
  status: QueueStatus
}

const BASE = '/pbx/queue'

export const getQueue = () => api.get<QueueStatus>(BASE)

/** Opens or closes the queue: the server dials *280 if that changes anything. */
export const switchQueue = (open: boolean) => api.post<QueueSwitchResult>(`${BASE}/switch`, { open })

/** Says which state the queue is in right now, without calling the PBX. */
export const markQueue = (open: boolean) => api.put<QueueStatus>(`${BASE}/state`, { open })
