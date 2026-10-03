/**
 * The mistakes made by the branches and the agents (S-65). Mirrors
 * `CallCenter.Shared.Contracts.Mistakes`, hand-written, so a change on the
 * server has to be copied across.
 */
import { api, requestBlob } from './client'
import type { Query } from './client'

/** `mistakes.responsible`: the branch as a whole, or one agent. */
export type Responsible = 'Branch' | 'Agent'

export interface Mistake {
  id: string
  /** The day it happened, yyyy-mm-dd. A day, not an instant. */
  occurredOn: string
  branchId: string
  branchName: string
  responsible: Responsible
  /** Set exactly when `responsible` is Agent. */
  agentId: string | null
  agentDisplayName: string | null
  /** Shekels. Null when the mistake had no value. */
  value: number | null
  /** The customer has been compensated for it (تم التعويض). */
  compensated: boolean
  /** The saved customer the number belonged to when it was saved. */
  contactId: string | null
  contactName: string | null
  /** As the supervisor typed it, kept even when nobody has it on file. */
  customerNumber: string | null
  notes: string
  createdByDisplayName: string | null
  createdAt: string
  updatedAt: string
}

export interface MistakePage {
  rows: Mistake[]
  total: number
  /** The value of every match, not only this page. */
  totalValue: number
  page: number
  pageSize: number
}

/** Every filter is optional; blank ones are left off the query. A list matches any of its ids. */
export interface MistakeFilters {
  /** First day, yyyy-mm-dd, inclusive. */
  from?: string
  /** Last day, yyyy-mm-dd, inclusive. */
  to?: string
  branchId?: string[]
  responsible?: Responsible
  agentId?: string[]
  q?: string
  /** Only the compensated, or only those not; left out for both. */
  compensated?: boolean
}

export interface UpsertMistakeRequest {
  occurredOn: string
  branchId: string
  responsible: Responsible
  agentId: string | null
  value: number | null
  customerNumber: string | null
  notes: string
  compensated: boolean
}


export const searchMistakes = (filters: MistakeFilters, page: number, pageSize = 50) =>
  api.get<MistakePage>('/mistakes', { query: { ...filters, page, pageSize } as Query })

/** Every mistake matching the filters, as the CSV the server builds (S-05): the whole result, not the page. */
export const exportMistakes = (filters: MistakeFilters, lang: string) =>
  requestBlob('/mistakes/export', { query: { ...filters, lang } as Query })

export const createMistake = (request: UpsertMistakeRequest) => api.post<Mistake>('/mistakes', request)

export const updateMistake = (id: string, request: UpsertMistakeRequest) =>
  api.put<Mistake>(`/mistakes/${id}`, request)

export const deleteMistake = (id: string) => api.delete<void>(`/mistakes/${id}`)

// ---- the mistakes report (R-23) ------------------------------------------

/** The report's filters: days, inclusive, yyyy-mm-dd, any of some branches and agents, and compensated or not. */
export interface MistakeReportFilters {
  from?: string
  to?: string
  branchId?: string[]
  agentId?: string[]
  compensated?: boolean
}

export interface MistakeBranchRow {
  branchId: string
  branch: string
  mistakes: number
  /** Put down to the branch as a whole. */
  branchOwn: number
  /** Put down to one of its agents. */
  byAgents: number
  value: number
  /** How many of them the customer was compensated for. */
  compensated: number
  /** The value of those compensated. */
  compensatedValue: number
}

export interface MistakeAgentRow {
  agentId: string
  agent: string
  mistakes: number
  value: number
  compensated: number
  compensatedValue: number
}

export type MistakeTrendGrouping = 'day' | 'week' | 'month'

export interface MistakeTrendPoint {
  /** yyyy-mm-dd for a day or a week's Monday, yyyy-mm for a month. */
  bucket: string
  mistakes: number
  branchOwn: number
  byAgents: number
  value: number
  /** How many of them the customer was compensated for. */
  compensated: number
  /** The value of those compensated. */
  compensatedValue: number
}

export interface MistakeCustomerRow {
  /** Null for a number nobody has on file. */
  contactId: string | null
  customer: string | null
  /** As typed on their latest mistake. */
  number: string
  mistakes: number
  value: number
  compensated: number
  /** The day of their latest mistake. */
  last: string
}

const report = <T,>(name: string, filters: MistakeReportFilters, extra: Query = {}) =>
  api.get<T[]>(`/mistakes/reports/${name}`, { query: { ...filters, ...extra } as Query })

export const mistakesByBranch = (filters: MistakeReportFilters) => report<MistakeBranchRow>('by-branch', filters)
export const mistakesByAgent = (filters: MistakeReportFilters) => report<MistakeAgentRow>('by-agent', filters)
export const mistakesTrend = (filters: MistakeReportFilters, groupBy: MistakeTrendGrouping) =>
  report<MistakeTrendPoint>('trend', filters, { groupBy })
export const repeatCustomers = (filters: MistakeReportFilters) => report<MistakeCustomerRow>('repeat-customers', filters)
