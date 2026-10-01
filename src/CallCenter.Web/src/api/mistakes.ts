/**
 * The mistakes made by the branches and the agents (S-65). Mirrors
 * `CallCenter.Shared.Contracts.Mistakes`, hand-written, so a change on the
 * server has to be copied across.
 */
import { api, requestBlob } from './client'

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

/** Every filter is optional; blank ones are left off the query. */
export interface MistakeFilters {
  /** First day, yyyy-mm-dd, inclusive. */
  from?: string
  /** Last day, yyyy-mm-dd, inclusive. */
  to?: string
  branchId?: string
  responsible?: Responsible
  agentId?: string
  q?: string
}

export interface UpsertMistakeRequest {
  occurredOn: string
  branchId: string
  responsible: Responsible
  agentId: string | null
  value: number | null
  customerNumber: string | null
  notes: string
}

type Query = Record<string, string | number | boolean | undefined>

export const searchMistakes = (filters: MistakeFilters, page: number, pageSize = 50) =>
  api.get<MistakePage>('/mistakes', { query: { ...filters, page, pageSize } as Query })

/** Every mistake matching the filters, as the CSV the server builds (S-05): the whole result, not the page. */
export const exportMistakes = (filters: MistakeFilters, lang: string) =>
  requestBlob('/mistakes/export', { query: { ...filters, lang } as Query })

export const createMistake = (request: UpsertMistakeRequest) => api.post<Mistake>('/mistakes', request)

export const updateMistake = (id: string, request: UpsertMistakeRequest) =>
  api.put<Mistake>(`/mistakes/${id}`, request)

export const deleteMistake = (id: string) => api.delete<void>(`/mistakes/${id}`)
