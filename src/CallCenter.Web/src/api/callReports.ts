/**
 * The call reports (R-01 to R-18) and the dashboard (S-20). Mirrors
 * `CallCenter.Shared.Contracts.Communications.CallReportsDto` — hand-written,
 * so a change on the server has to be copied across.
 *
 * Calls only, except where the point is comparing the phone with the apps
 * (R-01's calls vs app, R-12 to R-14, the dashboard's by channel; Dia, 25 Sep).
 * Every report takes S-07's common filters and comes back whole, not paged:
 * the rows are the report.
 */
import { api } from './client'
import type { OrdersReportRow, ReportFilters, TypeCount } from './applicationReports'

export type TimeGrouping = 'day' | 'week' | 'month'
export type BreakdownGrouping = TimeGrouping | 'agent' | 'branch'
export type ComplaintsGrouping = TimeGrouping | 'branch' | 'agent'

/** R-01. Inbound = answered + missed + blocked. */
export interface CallSummaryRow {
  bucket: string
  communications: number
  calls: number
  messages: number
  inbound: number
  outbound: number
  /** Inbound calls answered. */
  answered: number
  /** Inbound calls Missed or Rejected; never an outbound NoAnswer. */
  missed: number
  blocked: number
}

/** R-03: share is a percentage of the classified calls. */
export interface TypeShareRow {
  typeName: string
  labelAr: string
  labelEn: string
  count: number
  share: number
}

/** R-04. */
export interface CallBreakdownRow {
  key: string
  label: string
  calls: number
  answered: number
  missed: number
  orders: number
  orderValue: number
  byType: TypeCount[]
}

/** A customer, ranked (R-04, R-05, R-16). */
export interface CustomerRankRow {
  contactId: string
  name: string | null
  number: string | null
  calls: number
  orders: number
  orderValue: number
  complaints: number
  lastAt: string
}

/** A complaint or a cancellation (R-05, R-14). */
export interface ProblemRow {
  id: string
  startedAt: string
  channel: string
  contactId: string | null
  customer: string | null
  number: string | null
  agent: string | null
  branch: string | null
  notes: string | null
  followUp: boolean
  resolved: boolean
  resolvedAt: string | null
}

/** R-05 per branch or agent, and R-17. */
export interface ComplaintsRow {
  key: string
  label: string
  complaints: number
  followUp: number
  resolved: number
  open: number
  averageHoursToResolve: number | null
  orders: number
  perHundredOrders: number | null
}

export interface Count {
  key: string
  label: string
  count: number
}

export interface DashboardToday {
  communications: number
  calls: number
  messages: number
  byType: TypeCount[]
  byChannel: Count[]
  orders: number
  orderValue: number
  complaints: number
  missed: number
  unclassified: number
  agentsOnline: number
}

export interface DashboardPeriod {
  perDay: Count[]
  perType: TypeCount[]
  perChannel: Count[]
  perHour: Count[]
}

const BASE = '/reports/calls'
const q = (filters: ReportFilters, extra: Record<string, string | undefined> = {}) =>
  ({ query: { ...filters, ...extra } })

export const callSummary = (f: ReportFilters, groupBy: TimeGrouping) =>
  api.get<CallSummaryRow[]>(`${BASE}/summary`, q(f, { groupBy }))

export const callsByType = (f: ReportFilters) => api.get<TypeShareRow[]>(`${BASE}/by-type`, q(f))

export const callBreakdown = (f: ReportFilters, groupBy: BreakdownGrouping) =>
  api.get<CallBreakdownRow[]>(`${BASE}/breakdown`, q(f, { groupBy }))

export const recurringCustomers = (f: ReportFilters) =>
  api.get<CustomerRankRow[]>(`${BASE}/recurring-customers`, q(f))

export const complaintsList = (f: ReportFilters) => api.get<ProblemRow[]>(`${BASE}/complaints`, q(f))

export const complaintsBy = (f: ReportFilters, groupBy: ComplaintsGrouping) =>
  api.get<ComplaintsRow[]>(`${BASE}/complaints/by`, q(f, { groupBy }))

export const repeatComplainers = (f: ReportFilters) =>
  api.get<CustomerRankRow[]>(`${BASE}/repeat-complainers`, q(f))

/** R-13: phone and apps together, per channel, branch, agent or day. */
export const orderValue = (f: ReportFilters, groupBy: 'channel' | 'branch' | 'agent' | 'day') =>
  api.get<OrdersReportRow[]>(`${BASE}/orders`, q(f, { groupBy }))

export const dashboardToday = () => api.get<DashboardToday>('/reports/dashboard/today')

export const dashboardPeriod = (from?: string, to?: string) =>
  api.get<DashboardPeriod>('/reports/dashboard/period', { query: { from, to } })

// ---- Phase 2 (R-10 to R-18) -----------------------------------------------------

/** R-10: incoming calls in one hour of the day, per weekday, Monday first. */
export interface PeakHourRow {
  hour: number
  byWeekday: number[]
  total: number
}

/** R-11: rate is missed ÷ incoming, as a percentage; null when nothing came in. */
export interface MissedRow {
  key: string
  label: string
  inbound: number
  missed: number
  rejected: number
  total: number
  rate: number | null
}

export interface MissedCallRow {
  id: string
  startedAt: string
  status: string
  number: string | null
  contactId: string | null
  customer: string | null
  agent: string | null
  branch: string | null
  notes: string | null
}

/** R-12. */
export interface ChannelOrdersRow {
  channelId: string
  channel: string
  orders: number
  orderValue: number
  share: number
}

export interface ChannelOrdersTrendPoint {
  bucket: string
  phoneOrders: number
  appOrders: number
  phoneValue: number
  appValue: number
}

/** R-14: rate is cancellations ÷ orders, as a percentage; null with no orders. */
export interface CancellationRateRow {
  key: string
  label: string
  orders: number
  cancellations: number
  rate: number | null
}

/** R-15. */
export interface AgentProductivityRow {
  agentId: string
  agent: string
  /** Answered, in and out. */
  handled: number
  /** Incoming calls answered. */
  inbound: number
  /** Outgoing calls made. */
  outbound: number
  averageDurationSec: number | null
  orders: number
  orderValue: number
  unclassified: number
  missed: number
}

/** R-16. */
export interface CustomerBaseRow {
  bucket: string
  customers: number
  new: number
  returning: number
}

/** R-18. */
export interface DataQuality {
  unclassified: number
  unknownCalls: number
  unknownNumbers: number
  duplicateNames: number
}

export interface UnknownNumberRow {
  number: string
  calls: number
  firstAt: string
  lastAt: string
}

export interface DuplicateNameRow {
  name: string
  contacts: number
  numbers: string
}

export type MissedGrouping = TimeGrouping | 'hour' | 'agent' | 'branch'
export type CancellationGrouping = 'branch' | 'channel' | 'agent'

export const peakHours = (f: ReportFilters) => api.get<PeakHourRow[]>(`${BASE}/peak-hours`, q(f))

export const missedCalls = (f: ReportFilters, groupBy: MissedGrouping) =>
  api.get<MissedRow[]>(`${BASE}/missed`, q(f, { groupBy }))

export const missedList = (f: ReportFilters) => api.get<MissedCallRow[]>(`${BASE}/missed/list`, q(f))

export const ordersByChannel = (f: ReportFilters) => api.get<ChannelOrdersRow[]>(`${BASE}/orders-by-channel`, q(f))

export const ordersTrend = (f: ReportFilters, groupBy: TimeGrouping) =>
  api.get<ChannelOrdersTrendPoint[]>(`${BASE}/orders-trend`, q(f, { groupBy }))

export const cancellationRates = (f: ReportFilters, groupBy: CancellationGrouping) =>
  api.get<CancellationRateRow[]>(`${BASE}/cancellations`, q(f, { groupBy }))

export const cancellationList = (f: ReportFilters) => api.get<ProblemRow[]>(`${BASE}/cancellations/list`, q(f))

export const agentProductivity = (f: ReportFilters) => api.get<AgentProductivityRow[]>(`${BASE}/agents`, q(f))

export const customerBase = (f: ReportFilters, groupBy: TimeGrouping) =>
  api.get<CustomerBaseRow[]>(`${BASE}/customers`, q(f, { groupBy }))

export const topCustomers = (f: ReportFilters, by: 'orders' | 'value') =>
  api.get<CustomerRankRow[]>(`${BASE}/top-customers`, q(f, { by }))

/** Measured back from today over every order, whatever the period (the agent and branch filters still apply). */
export const inactiveCustomers = (f: ReportFilters, days: number) =>
  api.get<CustomerRankRow[]>(`${BASE}/inactive-customers`, q(f, { days: String(days) }))

export const dataQuality = (f: ReportFilters) => api.get<DataQuality>(`${BASE}/data-quality`, q(f))

export const unknownNumbers = (f: ReportFilters) => api.get<UnknownNumberRow[]>(`${BASE}/unknown-numbers`, q(f))

export const duplicateNames = () => api.get<DuplicateNameRow[]>(`${BASE}/duplicate-names`)
