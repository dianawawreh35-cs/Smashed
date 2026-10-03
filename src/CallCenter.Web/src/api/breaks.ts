/**
 * The agents' breaks (A-86): who is on break now (S-66) and the break report
 * (R-22). Mirrors `CallCenter.Shared.Contracts.Breaks`, hand-written, so a
 * change on the server has to be copied across.
 */
import { api, requestBlob } from './client'
import type { Query } from './client'

/** Where an agent stands now. */
export type BreakState = 'Working' | 'OnBreak' | 'NotHeard' | 'SignedOut'

/** How a break ended: the agent's own two, then the server's conclusions for a break the app never ended. */
export type BreakEnding = 'BreakOut' | 'SignedOut' | 'SessionEnded' | 'NotHeard'

export interface BreakMonitorRow {
  agentId: string
  agentDisplayName: string
  state: BreakState
  /** Set exactly while on break. */
  breakStartedAt: string | null
  /** Every break today, the one going included, to `asOf`. */
  todaySeconds: number
  todayBreaks: number
  /** The Agent App's do-not-disturb switch (A-18); null when the app is not heard from, or too old to say. */
  doNotDisturb: boolean | null
  /** When it was last switched; null when `doNotDisturb` is. */
  doNotDisturbSince: string | null
}

export interface BreakMonitor {
  /** The server's clock when this was worked out: breaks going are counted on from here. */
  asOf: string
  dailyLimitMinutes: number
  agents: BreakMonitorRow[]
}

export interface BreakAgentTotal {
  agentId: string
  agentDisplayName: string
  breaks: number
  seconds: number
  /** Days with a break. */
  days: number
  daysOver: number
  overSeconds: number
}

export interface BreakDay {
  /** yyyy-mm-dd, the restaurant's day. */
  day: string
  agentId: string
  agentDisplayName: string
  breaks: number
  seconds: number
  overSeconds: number
}

export interface BreakReport {
  dailyLimitMinutes: number
  agents: BreakAgentTotal[]
  days: BreakDay[]
}

export interface Break {
  id: string
  agentId: string
  agentDisplayName: string
  startedAt: string
  /** Null while it is going. */
  endedAt: string | null
  endedBy: BreakEnding | null
  seconds: number
}

export interface BreakPage {
  rows: Break[]
  total: number
  page: number
  pageSize: number
}

/** The restaurant's days, both ends included, yyyy-mm-dd; and some agents, or none for all. */
export interface BreakFilters {
  from?: string
  to?: string
  agentId?: string[]
}


export const breakMonitor = () => api.get<BreakMonitor>('/breaks/monitor')

export const breakReport = (filters: BreakFilters) =>
  api.get<BreakReport>('/breaks/report', { query: { ...filters } as Query })

export const listBreaks = (filters: BreakFilters, page: number, pageSize = 50) =>
  api.get<BreakPage>('/breaks', { query: { ...filters, page, pageSize } as Query })

/** Every break of the period, as the CSV the server builds (S-05): the whole result, not the page. */
export const exportBreaks = (filters: BreakFilters, lang: string) =>
  requestBlob('/breaks/export', { query: { ...filters, lang } as Query })
