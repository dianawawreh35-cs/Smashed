/**
 * The POS customer lookup (A-67). Mirrors
 * `CallCenter.Shared.Contracts.Pos.PosLookupStatusDto` — hand-written, so a
 * change on the server has to be copied across.
 */
import { api } from './client'

export interface PosLookupStatus {
  /** The server has a POS token. Without one nothing is ever asked. */
  enabled: boolean
  /** How often it runs by itself (`pos.lookup.interval_minutes`). */
  intervalMinutes: number
  /** A run is going now. */
  running: boolean
  /** When the last run since the server started began; null when there has been none. */
  lastStartedAt: string | null
  lastFinishedAt: string | null
  asked: number
  notFound: number
  created: number
  filledIn: number
  callsLinked: number
  /** The POS could not be asked, so the run stopped early. */
  failed: boolean
}

const BASE = '/pos/lookup'

export const getPosLookup = () => api.get<PosLookupStatus>(BASE)

/** Asks the POS now about every number that called in the last two days and has no contact. */
export const runPosLookup = () => api.post<PosLookupStatus>(`${BASE}/run`)
