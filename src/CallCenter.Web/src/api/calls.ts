/**
 * The supervisor's search across every call, one call opened in full, and its
 * recording (S-02, S-03, S-04). Mirrors `CallCenter.Shared.Contracts.Communications.CallSearchDto`
 * and `...Classifications.ClassificationDto`. Hand-written, so a change on the
 * server has to be copied across.
 */
import type { Communication } from './communications'
import { api, requestBlob } from './client'
import type { Query } from './client'

export interface CallRow {
  id: string
  kind: string
  startedAt: string
  direction: string
  status: string
  agentId: string | null
  agentDisplayName: string | null
  contactId: string | null
  contactName: string | null
  remoteNumberRaw: string | null
  branchId: string | null
  branchName: string | null
  typeName: string | null
  typeLabelAr: string | null
  typeLabelEn: string | null
  orderValue: number | null
  durationSec: number | null
  /** The classification's notes, or the call's own note when it has none. */
  notes: string | null
  isClassified: boolean
  hasRecording: boolean
  /** Recorded, and the audio has since been deleted by retention (A-33). */
  recordingExpired: boolean
  /** Which app a message came on (A-70). Null for a call. */
  channelId: string | null
  channelName: string | null
}

export interface CallPage {
  rows: CallRow[]
  total: number
  page: number
  pageSize: number
}

export interface CallDetails {
  summary: CallRow
  remoteName: string | null
  answeredAt: string | null
  endedAt: string | null
  waitSec: number | null
  queueName: string | null
  extension: string | null
  /** Why a missed, rejected or unanswered call went that way (A-41). */
  callNotes: string | null
}

export interface CallClassification {
  typeId: string
  typeName: string
  typeLabelAr: string
  typeLabelEn: string
  branchId: string | null
  branchName: string | null
  orderValue: number | null
  notes: string | null
  followUp: boolean
  resolved: boolean | null
  formVersion: number
  /** Answers to the form's own questions, keyed by field. */
  customValues: Record<string, unknown>
  classifiedByName: string
  classifiedAt: string
  updatedByName: string | null
  updatedAt: string | null
}

export interface ClassificationChange {
  changedAt: string
  changedByName: string
  /** Null on the first classification. */
  before: Record<string, unknown> | null
  after: Record<string, unknown>
}

/**
 * Every filter is optional; blank ones are left off the query. A list matches
 * any of its values, and an empty one is no filter (Dia, 2 Oct 2026).
 */
export interface CallFilters {
  /**
   * Call (the default, and what the Calls page sends) or App. Two kinds share
   * the table, one screen each (A-70): the Calls page must never start
   * showing messages.
   */
  kind?: 'Call' | 'App'
  q?: string
  channelId?: string[]
  agentId?: string[]
  branchId?: string[]
  typeId?: string[]
  status?: string[]
  direction?: string
  /** Inclusive instant, ISO 8601. */
  from?: string
  /** Exclusive instant, ISO 8601. */
  to?: string
  notes?: string
  minOrder?: number
  maxOrder?: number
  hasRecording?: boolean
  classified?: boolean
  /**
   * False for the customers' calls, the ones the reports count; true for the
   * internal ones, which they leave out (S-48). The Calls page's two tabs
   * (Dia, 9 Oct 2026). Left off, both.
   */
  internal?: boolean
}

export const searchCalls = (filters: CallFilters, page: number, pageSize = 50) =>
  api.get<CallPage>('/communications/search', {
    query: { ...filters, page, pageSize } as Query,
  })

/**
 * R-02: every call matching the filters, as the CSV the server builds (S-05).
 * The whole result, not the page on screen: exporting a page would lie the
 * way filtering one does (20 Sep). Headings in the supervisor's language.
 */
export const exportCalls = (filters: CallFilters, lang: string) =>
  requestBlob('/communications/search/export', {
    query: { ...filters, lang } as Query,
  })

export const callDetails = (id: string) => api.get<CallDetails>(`/communications/${id}`)

export const callClassification = (id: string) => api.get<CallClassification>(`/classifications/${id}`)

/**
 * What a classification is saved as (A-42, S-04). The built-in answers have
 * columns of their own, because the reports group by them; every question the
 * supervisor added travels in `customValues`, keyed by field.
 */
export interface SaveClassification {
  typeId: string
  branchId: string | null
  orderValue: number | null
  notes: string | null
  followUp: boolean
  /** Only kept for a complaint; the server clears it on anything else. */
  resolved: boolean | null
  formVersion: number
  customValues: Record<string, unknown>
}

/** Classifies a call, or changes its classification. A supervisor may do either at any time (S-04). */
export const saveClassification = (id: string, request: SaveClassification) =>
  api.put<CallClassification>(`/classifications/${id}`, request)

/**
 * Moves a message to another app channel (A-71). A supervisor may on any
 * message, on any day (Dia, 2 Oct); the customer and the time are left as they are.
 */
export const changeMessageChannel = (id: string, channelId: string) =>
  api.put<Communication>(`/communications/applications/${id}/channel`, { channelId })

/** Deletes a message recorded by mistake (Dia, 2 Oct). What it said stays in the audit log. Never a call. */
export const deleteMessage = (id: string) => api.delete<void>(`/communications/applications/${id}`)

export const classificationHistory = (id: string) =>
  api.get<ClassificationChange[]>(`/classifications/${id}/history`)

/** The audio, as the recorder wrote it. Seeking needs it whole, and it is a few MB. */
export const fetchRecording = (id: string, signal?: AbortSignal) =>
  requestBlob(`/recordings/${id}`, { signal })

/** The same file, to keep (S-04). */
export const downloadRecording = (id: string) => requestBlob(`/recordings/${id}/download`)

/** The start of the day `date` (yyyy-mm-dd) names, in the browser's own time zone. */
export function startOfDay(date: string): string {
  const [y, m, d] = date.split('-').map(Number)
  return new Date(y, m - 1, d).toISOString()
}

/** The start of the day after `date`: the exclusive end of a range that includes it. */
export function startOfNextDay(date: string): string {
  const [y, m, d] = date.split('-').map(Number)
  return new Date(y, m - 1, d + 1).toISOString()
}
