/**
 * The abandoned-call import (S-55). Mirrors
 * `CallCenter.Shared.Contracts.Pbx.AbandonedImportDto` — hand-written, so a
 * change on the server has to be copied across.
 */
import { api } from './client'

export interface AbandonedImport {
  url: string
  username: string
  /** Whether a password is stored. The password itself never comes back. */
  passwordSet: boolean
  intervalMinutes: number
  /** Address, username and password are all set, so the import runs. */
  configured: boolean
  lastCheckedAt: string | null
  lastSucceededAt: string | null
  /** Why the last check failed; null when it succeeded. */
  lastError: string | null
  lastAdded: number | null
  syncedThrough: string | null
}

export interface UpdateAbandonedImport {
  url: string
  username: string
  /** Blank keeps the stored password. */
  password: string
  intervalMinutes: number
}

export interface AbandonedFetchResult {
  ok: boolean
  error: string | null
  /** Local days, yyyy-MM-dd. */
  from: string
  to: string
  calls: number
  abandoned: number
  added: number
  ringsLinked: number
  status: AbandonedImport
}

const BASE = '/pbx/abandoned-import'

export const getAbandonedImport = () => api.get<AbandonedImport>(BASE)

export const saveAbandonedImport = (values: UpdateAbandonedImport) => api.put<AbandonedImport>(BASE, values)

/** Downloads the period from the PBX now. The reports' own instants: `to` exclusive. Without them, today. */
export const fetchAbandoned = (from?: string, to?: string) =>
  api.post<AbandonedFetchResult>(`${BASE}/fetch`, undefined, { query: { from, to } })
