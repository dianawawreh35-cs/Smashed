/**
 * The PBX blacklist (S-46). Mirrors
 * `CallCenter.Shared.Contracts.Pbx.PbxBlacklistDto` — hand-written, so a
 * change on the server has to be copied across.
 */
import { api } from './client'

export interface PbxBlacklistFailure {
  /** As keyed into the PBX: the local form, 0599123456. */
  number: string
  /** True for a *30 that failed, false for a *31. */
  adding: boolean
  error: string
  attempts: number
  at: string
}

export interface PbxBlacklist {
  extension: string
  /** Whether a password is stored. The password itself never comes back. */
  secretSet: boolean
  /** Extension, password and the PBX address are all set, so the server dials. */
  configured: boolean
  onPbx: number
  /** Numbers still to be added or removed, the failed ones included. */
  waiting: number
  failures: PbxBlacklistFailure[]
  lastSucceededAt: string | null
}

export interface UpdatePbxBlacklist {
  extension: string
  /** Blank keeps the stored password. */
  secret: string
}

const BASE = '/pbx/blacklist'

export const getPbxBlacklist = () => api.get<PbxBlacklist>(BASE)

export const savePbxBlacklist = (values: UpdatePbxBlacklist) => api.put<PbxBlacklist>(BASE, values)

/** Every failed number is tried again at the next run, without waiting out the retry delay. */
export const retryPbxBlacklist = () => api.post<PbxBlacklist>(`${BASE}/retry`)
