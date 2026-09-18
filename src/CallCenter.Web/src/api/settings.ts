/**
 * System settings (S-47). Mirrors `CallCenter.Shared.Contracts.Settings`.
 */
import { ApiError, api } from './client'

export interface Setting {
  key: string
  value: string
  /** Text, Integer, Boolean or Choice - how the field is rendered. */
  kind: string
  options: string[] | null
  updatedAt: string
  updatedByDisplayName: string | null
}

export const listSettings = () => api.get<Setting[]>('/settings')

export const updateSettings = (values: Record<string, string>) =>
  api.put<Setting[]>('/settings', { values })

/**
 * The per-setting complaints from a rejected save, so the screen can mark the
 * field that caused it rather than showing one vague message.
 */
export function settingProblems(error: unknown): Record<string, string> {
  if (error instanceof ApiError && error.body && typeof error.body === 'object') {
    const problems = (error.body as { problems?: unknown }).problems
    if (problems && typeof problems === 'object') {
      return problems as Record<string, string>
    }
  }
  return {}
}
