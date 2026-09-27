/**
 * The Agent Apps' logs, as the laptops sent them (N-12). Mirrors
 * `CallCenter.Shared.Contracts.AgentLogs` — hand-written, so a change on the
 * server has to be copied across.
 */
import { api } from './client'

export interface AgentLogDay {
  /** As the laptop names it: `agent-20260927.log`. */
  file: string
  /** `yyyy-MM-dd`. */
  date: string
  bytes: number
  /** Entries at Error or Fatal. */
  errors: number
  warnings: number
  lastWriteAt: string
}

export interface AgentLogLaptop {
  /** The laptop's Windows name, as the app cleaned it for a folder. */
  laptop: string
  lastWriteAt: string
  /** Newest first. */
  days: AgentLogDay[]
}

export interface AgentLogEntry {
  /** Where the entry starts in the day's file, from 1. */
  line: number
  /** As written on the laptop: `2026-09-27 14:03:11.482 +03:00`. Empty for lines before the first entry. */
  time: string
  /** VRB, DBG, INF, WRN, ERR or FTL; empty for lines before the first entry. */
  level: string
  /** The message and every line under it: an exception, a SQL command. */
  text: string
}

export interface AgentLogPage {
  /** Newest first, at most the newest 2000 that matched. */
  entries: AgentLogEntry[]
  matched: number
  total: number
  errors: number
  warnings: number
}

/** Which entries to show. */
export type AgentLogLevels = 'all' | 'warnings' | 'errors'

export const isError = (level: string) => level === 'ERR' || level === 'FTL'
export const isWarning = (level: string) => level === 'WRN'

export const listAgentLogs = () => api.get<AgentLogLaptop[]>('/agent-logs')

export const readAgentLog = (laptop: string, file: string, levels: AgentLogLevels, search: string) =>
  api.get<AgentLogPage>(`/agent-logs/${encodeURIComponent(laptop)}/${encodeURIComponent(file)}`, {
    query: { levels, search: search.trim() || undefined },
  })
