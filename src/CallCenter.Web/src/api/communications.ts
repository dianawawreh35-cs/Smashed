/**
 * The record of every call (A-14). Mirrors
 * `CallCenter.Shared.Contracts.Communications` — hand-written, so a change on
 * the server has to be copied across.
 */
import { api } from './client'

export interface Communication {
  id: string
  /** Call or App. */
  kind: string
  /** In, Out, or None for app entries. */
  direction: string
  /** Answered, Missed, Rejected, Blocked, Abandoned… */
  status: string
  contactId: string | null
  contactName: string | null
  remoteNumberRaw: string | null
  /** The caller-ID name the trunk supplied, if any. */
  remoteName: string | null
  startedAt: string
  answeredAt: string | null
  endedAt: string | null
  /** Talk time in seconds. Null when the call was never answered. */
  durationSec: number | null
  /** Which queue it came through. Null for a direct call. */
  queueName: string | null
  extension: string | null
  agentDisplayName: string | null
  isClassified: boolean
  /** Why a missed or rejected call went that way, in the agent's words. */
  notes: string | null
  /** The audio is on the server and can be played (S-04). */
  hasRecording: boolean
  /** Recorded, and retention has since deleted the audio (A-33). */
  recordingExpired: boolean
}

/**
 * One contact's history, newest first (A-62). Every agent's calls, not just the
 * viewer's — the point of the panel is the customer's whole relationship with
 * the restaurant.
 */
export const communicationsForContact = (contactId: string, limit = 100) =>
  api.get<Communication[]>(`/communications/by-contact/${contactId}`, { query: { limit } })
