/**
 * The application reports (A-72, R-12, R-13): figures about messages, never
 * calls. Mirrors `CallCenter.Shared.Contracts.Communications.ApplicationReportsDto`
 * — hand-written, so a change on the server has to be copied across.
 *
 * Every report takes S-07's common filters. The rows come back whole, not
 * paged: they *are* the report, one row per channel, agent or day, so the
 * table, the chart and the CSV export all draw from the same fetched rows.
 */
import { api } from './client'

/** S-07's common filters. Blank ones are left off the query. */
export interface ReportFilters {
  /** Inclusive instant, ISO 8601. */
  from?: string
  /** Exclusive instant, ISO 8601. */
  to?: string
  agentId?: string
  branchId?: string
  channelId?: string
  typeId?: string
}

export type OrdersGrouping = 'channel' | 'branch' | 'agent' | 'day'
export type TrendGrouping = 'day' | 'week' | 'month' | 'hour'

export interface TypeCount {
  typeName: string
  labelAr: string
  labelEn: string
  count: number
}

export interface ChannelReportRow {
  channelId: string
  channel: string
  messages: number
  byType: TypeCount[]
}

export interface OrdersReportRow {
  /** The id, or the day as yyyy-MM-dd: stable for sorting and the chart. */
  key: string
  label: string
  orders: number
  orderValue: number
  /** Value per order. Null when there were none. */
  average: number | null
}

export interface TrendPoint {
  /** yyyy-MM-dd for a day or a week's Monday, yyyy-MM for a month, HH for an hour. */
  bucket: string
  messages: number
  orders: number
  orderValue: number
}

export interface AgentReportRow {
  agentId: string
  agent: string
  messages: number
  orders: number
  orderValue: number
}

export interface IssuesReportRow {
  channelId: string
  channel: string
  messages: number
  cancellations: number
  complaints: number
}

const BASE = '/reports/applications'

export const messagesByChannel = (filters: ReportFilters) =>
  api.get<ChannelReportRow[]>(`${BASE}/by-channel`, { query: { ...filters } })

export const ordersReport = (filters: ReportFilters, groupBy: OrdersGrouping) =>
  api.get<OrdersReportRow[]>(`${BASE}/orders`, { query: { ...filters, groupBy } })

export const messagesTrend = (filters: ReportFilters, groupBy: TrendGrouping) =>
  api.get<TrendPoint[]>(`${BASE}/trend`, { query: { ...filters, groupBy } })

export const messagesByAgent = (filters: ReportFilters) =>
  api.get<AgentReportRow[]>(`${BASE}/by-agent`, { query: { ...filters } })

export const issuesByChannel = (filters: ReportFilters) =>
  api.get<IssuesReportRow[]>(`${BASE}/issues`, { query: { ...filters } })
