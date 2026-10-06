import { useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import type { CallDirection, OrdersReportRow, ReportFilters, TypeCount } from '../api/applicationReports'
import type { CallRow } from '../api/calls'
import { fetchAbandoned, getAbandonedImport } from '../api/pbx'
import {
  abandonedCalls,
  abandonedList,
  agentProductivity,
  callBreakdown,
  callSummary,
  callsByType,
  cancellationList,
  cancellationRates,
  complaintsBy,
  complaintsList,
  customerBase,
  dataQuality,
  duplicateNames,
  inactiveCustomers,
  missedCalls,
  missedList,
  orderValue,
  ordersByChannel,
  ordersTrend,
  peakHours,
  recurringCustomers,
  repeatComplainers,
  topCustomers,
  unknownNumbers,
} from '../api/callReports'
import type {
  AbandonedCallRow,
  AbandonedGrouping,
  AbandonedRow,
  AgentProductivityRow,
  BreakdownGrouping,
  CallBreakdownRow,
  CallSummaryRow,
  CancellationGrouping,
  CancellationRateRow,
  ChannelOrdersRow,
  ChannelOrdersTrendPoint,
  ComplaintCase,
  ComplaintsGrouping,
  ComplaintsRow,
  CustomerBaseRow,
  CustomerRankRow,
  DuplicateNameRow,
  MissedCallRow,
  MissedGrouping,
  MissedRow,
  PeakHourRow,
  ProblemRow,
  TimeGrouping,
  TypeShareRow,
  UnknownNumberRow,
} from '../api/callReports'
import CallDetails from '../components/CallDetails'
import ReportCard, { SERIES_COLOURS } from '../components/ReportCard'
import type { ReportColumn } from '../components/ReportCard'
import { Grouping, PrintPageButton, ReportFilterBar, ReportPrintHeading } from '../components/ReportFilters'
import LoadError from '../components/LoadError'
import RecordingPlayer from '../components/RecordingPlayer'
import { formatClock } from '../lib/recordingWav'
import { ltr } from '../lib/bidi'
import { formatMoney } from '../lib/money'
import { printPage } from '../lib/print'
import { localDate, useReportFilters } from '../lib/reportFilters'

/**
 * The groups the reports are shown in (Dia, 25 Sep): one page, one filter bar,
 * a tab each. Complaints have a tab of their own (Dia, 6 Oct 2026); the
 * Problems tab kept the missed calls.
 */
const TABS = ['overview', 'orders', 'customers', 'agents', 'complaints', 'problems', 'abandoned', 'quality'] as const
type Tab = (typeof TABS)[number]

/** Tabs about incoming calls alone: with Outgoing chosen they say so instead of showing zeros. */
const INCOMING_ONLY: readonly Tab[] = ['problems', 'abandoned']

/** How many rows of a list report are drawn; the export holds them all. */
const LIST_LIMIT = 200

/**
 * The call reports (R-01 to R-18): calls only, except where the point is the
 * comparison with the apps (R-01's calls vs app, R-12 to R-14; Dia, 25 Sep).
 * R-19 (the daily e-mail) is not here, and neither is R-21, the service level:
 * it needs the wait time of the answered calls too, and the PBX import saves
 * only the abandoned ones (S-55). The abandoned calls have a tab of their own
 * (R-20), with a button that downloads the chosen period from the PBX now.
 *
 * One filter bar (S-07) scopes every tab, and only the open tab's reports are
 * fetched. Each report is a `ReportCard`: the server's rows as a table, a
 * chart where the figures are numeric (S-06), and Export CSV (S-05). R-02, the
 * full list, is the Calls page and its export, not a second list here.
 *
 * The words each report uses — missed, answered, an order, a customer — are
 * the server's (`CallReportsDto.cs`) and are named on the card that uses them,
 * where a supervisor would wonder.
 */
export default function CallReportsPage() {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const tab: Tab = (TABS as readonly string[]).includes(params.get('tab') ?? '') ? (params.get('tab') as Tab) : 'overview'
  const { draft, filters: chosen, set, choosePreset } = useReportFilters()
  // Incoming or outgoing, never both added together (Dia, 6 Oct 2026). The
  // complaints are the exception, and the server knows it: a complaint and
  // its call back are one complaint, whichever way each call went.
  const [direction, setDirection] = useState<CallDirection>('In')
  const filters: ReportFilters = { ...chosen, direction }
  const incomingOnly = direction === 'Out' && INCOMING_ONLY.includes(tab)

  return (
    <div className="space-y-6">
      <ReportPrintHeading title={t('callReports.heading')} section={t(`callReports.tabs.${tab}`)} draft={draft}
        extra={tab === 'complaints' ? [] : [`${t('callReports.direction.label')}: ${t(`callReports.direction.${direction}`)}`]} />
      <div className="no-print flex items-start justify-between gap-4">
        <div>
          <h1 className="page-title">{t('callReports.heading')}</h1>
          <p className="page-subtitle">{t('callReports.intro')}</p>
        </div>
        {/* Every report on the open tab (lib/print). */}
        <PrintPageButton onPrint={printPage} />
      </div>

      <ReportFilterBar draft={draft} set={set} choosePreset={choosePreset} includePhone
        direction={direction} onDirection={setDirection} />

      <ReportTabs tab={tab} onChoose={(name) => setParams(name === 'overview' ? {} : { tab: name }, { replace: true })} />

      <div role="tabpanel" id={PANEL_ID} aria-labelledby={tabId(tab)} tabIndex={0} className="space-y-6">
        {incomingOnly ? (
          <section className="card card-body">
            <p className="text-slate-300">{t('callReports.direction.incomingOnly')}</p>
            <button type="button" className="btn-primary btn-sm mt-3 self-start" onClick={() => setDirection('In')}>
              {t('callReports.direction.showIncoming')}
            </button>
          </section>
        ) : (
          <>
            {tab === 'overview' && <Overview filters={filters} direction={direction} />}
            {tab === 'orders' && <Orders filters={filters} />}
            {tab === 'customers' && <Customers filters={filters} />}
            {tab === 'agents' && <Agents filters={filters} direction={direction} />}
            {tab === 'complaints' && <Complaints filters={filters} />}
            {tab === 'problems' && <Missed filters={filters} />}
            {tab === 'abandoned' && <Abandoned filters={filters} />}
            {tab === 'quality' && <Quality filters={filters} />}
          </>
        )}
      </div>
    </div>
  )
}

// ---- shared pieces ------------------------------------------------------------

const PANEL_ID = 'call-reports-panel'
const tabId = (name: Tab) => `call-reports-tab-${name}`

/**
 * The tabs, as the ARIA tabs pattern has them: one tab in the Tab order (the
 * chosen one), the arrow keys move between them and open the one they reach,
 * Home and End go to the ends. In Arabic the tabs run right to left, so the
 * arrows follow what is on the screen: the left arrow moves to the next tab.
 */
function ReportTabs({ tab, onChoose }: { tab: Tab; onChoose: (name: Tab) => void }) {
  const { t, i18n } = useTranslation()
  const buttons = useRef<(HTMLButtonElement | null)[]>([])

  function onKeyDown(event: KeyboardEvent, index: number) {
    const forward = i18n.dir() === 'rtl' ? 'ArrowLeft' : 'ArrowRight'
    const back = i18n.dir() === 'rtl' ? 'ArrowRight' : 'ArrowLeft'
    const target =
      event.key === forward ? (index + 1) % TABS.length
        : event.key === back ? (index - 1 + TABS.length) % TABS.length
          : event.key === 'Home' ? 0
            : event.key === 'End' ? TABS.length - 1
              : null
    if (target === null) return
    event.preventDefault()
    onChoose(TABS[target])
    buttons.current[target]?.focus()
  }

  return (
    <div role="tablist" aria-label={t('callReports.heading')} className="flex flex-wrap gap-2 border-b border-ink-700 pb-2">
      {TABS.map((name, index) => (
        <button
          key={name}
          ref={(element) => {
            buttons.current[index] = element
          }}
          id={tabId(name)}
          type="button"
          role="tab"
          aria-selected={tab === name}
          aria-controls={PANEL_ID}
          tabIndex={tab === name ? 0 : -1}
          className={tab === name ? 'btn-primary btn-sm' : 'btn-ghost btn-sm'}
          onClick={() => onChoose(name)}
          onKeyDown={(event) => onKeyDown(event, index)}
        >
          {t(`callReports.tabs.${name}`)}
        </button>
      ))}
    </div>
  )
}

function useFormat() {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  return {
    t,
    arabic,
    c: (key: string) => t(`callReports.columns.${key}`),
    money: (v: number) => formatMoney(v, i18n.language),
    percent: (v: number | null) => (v === null ? '' : `${v.toLocaleString(i18n.language, { maximumFractionDigits: 1 })}%`),
    /** A figure to one decimal, or blank when there is none (an average of nothing). */
    decimal: (v: number | null) => (v === null ? '' : v.toLocaleString(i18n.language, { maximumFractionDigits: 1 })),
    when: (iso: string) => new Date(iso).toLocaleString(i18n.language, { dateStyle: 'short', timeStyle: 'short' }),
    /** For the CSV: the restaurant's date and time as Excel reads them, not a UTC instant. */
    stamp: (iso: string) => {
      const d = new Date(iso)
      return `${localDate(d)} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
    },
    typeLabel: (ty: { labelAr: string; labelEn: string }) => (arabic ? ty.labelAr : ty.labelEn),
  }
}

/** One column per type seen, after the fixed ones, so a table stays one row per heading. */
function typeColumns<T extends { byType: TypeCount[] }>(rows: T[] | undefined, arabic: boolean): ReportColumn<T>[] {
  const seen = [...new Map((rows ?? []).flatMap((r) => r.byType).map((ty) => [ty.typeName, ty])).values()]
  return seen.map((ty) => ({
    key: `type:${ty.typeName}`,
    label: arabic ? ty.labelAr : ty.labelEn,
    value: (r) => r.byType.find((x) => x.typeName === ty.typeName)?.count ?? 0,
    numeric: true,
    total: true,
  }))
}

/** Seconds as m:ss — a wait, or a talk time. Blank when there is none. */
const clock = (s: number | null) => (s === null ? '' : `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`)

/** The customer, or the number when the contact has no name. */
const customer = (r: { name?: string | null; customer?: string | null; number: string | null }) =>
  r.name ?? r.customer ?? r.number ?? ''

/** The same on screen, where a number standing in for a name is held left to right (M-W01). */
const customerShown = (r: { name?: string | null; customer?: string | null; number: string | null }) =>
  r.name ?? r.customer ?? (r.number ? ltr(r.number) : '')

// ---- Overview: R-01, R-02, R-03, R-04 per day -----------------------------------

function Overview({ filters, direction }: { filters: ReportFilters; direction: CallDirection }) {
  const { t, c, typeLabel, percent } = useFormat()
  const [by, setBy] = useState<TimeGrouping>('day')
  const incoming = direction === 'In'

  const summary = useQuery({ queryKey: ['reports', 'calls', 'summary', filters, by], queryFn: () => callSummary(filters, by) })
  const byType = useQuery({ queryKey: ['reports', 'calls', 'by-type', filters], queryFn: () => callsByType(filters) })

  // One direction's calls, then how they ended in that direction's own words,
  // then the applications, which have no direction (Dia, 6 Oct 2026).
  const summaryColumns: ReportColumn<CallSummaryRow>[] = [
    { key: 'bucket', label: c('period'), value: (r) => r.bucket },
    { key: 'communications', label: c('communications'), value: (r) => r.communications, numeric: true, total: true },
    { key: 'calls', label: c(incoming ? 'incomingCalls' : 'outgoingCalls'), value: (r) => r.calls, numeric: true, total: true },
    { key: 'answered', label: c('answered'), value: (r) => r.answered, numeric: true, total: true },
    ...(incoming
      ? [
          { key: 'missed', label: c('missed'), value: (r: CallSummaryRow) => r.missed, numeric: true, total: true },
          { key: 'abandoned', label: c('abandoned'), value: (r: CallSummaryRow) => r.abandoned, numeric: true, total: true },
          { key: 'blocked', label: c('blocked'), value: (r: CallSummaryRow) => r.blocked, numeric: true, total: true },
        ]
      : [{ key: 'notAnswered', label: c('notAnswered'), value: (r: CallSummaryRow) => r.notAnswered, numeric: true, total: true }]),
    { key: 'messages', label: c('messages'), value: (r) => r.messages, numeric: true, total: true },
  ]

  const typeColumnsR03: ReportColumn<TypeShareRow>[] = [
    { key: 'type', label: c('type'), value: (r) => typeLabel(r) },
    { key: 'count', label: c(incoming ? 'incomingCalls' : 'outgoingCalls'), value: (r) => r.count, numeric: true, total: true },
    { key: 'share', label: c('share'), value: (r) => r.share, format: (r) => percent(r.share), numeric: true },
  ]

  return (
    <>
      <ReportCard
        title={t('callReports.sections.summary.title')}
        hint={t('callReports.sections.summary.hint')}
        columns={summaryColumns}
        rows={summary.data}
        loading={summary.isFetching}
        error={summary.isError}
        exportName={`communications-${incoming ? 'incoming' : 'outgoing'}-by-${by}`}
        chart={{
          // A line needs two points; one day is a bar.
          kind: (summary.data?.length ?? 0) > 1 ? 'line' : 'bar',
          x: (r) => r.bucket,
          series: incoming
            ? [
                { key: 'answered', label: c('answered'), colour: SERIES_COLOURS[0] },
                { key: 'missed', label: c('missed'), colour: SERIES_COLOURS[1] },
                { key: 'abandoned', label: c('abandoned'), colour: SERIES_COLOURS[3] },
                { key: 'messages', label: c('messages'), colour: SERIES_COLOURS[2] },
              ]
            : [
                { key: 'answered', label: c('answered'), colour: SERIES_COLOURS[0] },
                { key: 'notAnswered', label: c('notAnswered'), colour: SERIES_COLOURS[1] },
                { key: 'messages', label: c('messages'), colour: SERIES_COLOURS[2] },
              ],
        }}
      >
        <Grouping label={t('applicationReports.groupBy')} value={by} options={['day', 'week', 'month']} onChange={setBy} />
      </ReportCard>

      {/* A link to the Calls page, which means nothing on paper. */}
      <section className="no-print card card-body" aria-label={t('callReports.sections.list.title')}>
        <h3 className="font-semibold text-slate-100">{t('callReports.sections.list.title')}</h3>
        <p className="text-sm text-slate-400">
          {t('callReports.sections.list.hint')}{' '}
          <Link to="/calls" className="text-brand-500 underline">{t('callReports.sections.list.link')}</Link>
        </p>
      </section>

      <ReportCard
        title={t('callReports.sections.byType.title')}
        hint={t('callReports.sections.byType.hint')}
        columns={typeColumnsR03}
        rows={byType.data}
        loading={byType.isFetching}
        error={byType.isError}
        exportName={`calls-${incoming ? 'incoming' : 'outgoing'}-by-type`}
        chart={{
          kind: 'bar',
          x: (r) => typeLabel(r),
          series: [{ key: 'count', label: c(incoming ? 'incomingCalls' : 'outgoingCalls'), colour: SERIES_COLOURS[0] }],
        }}
      />

      <Breakdown filters={filters} direction={direction} initial="day" name="breakdown" />

      {/* Incoming calls are the demand; the calls agents make are not (R-10). */}
      {incoming && <PeakHours filters={filters} />}
    </>
  )
}

/** R-04: per day, week, month, agent or branch, and per type within each, for one direction. */
function Breakdown({ filters, direction, initial, name }: {
  filters: ReportFilters
  direction: CallDirection
  initial: BreakdownGrouping
  name: 'breakdown' | 'byAgent'
}) {
  const { t, c, arabic, money } = useFormat()
  const [by, setBy] = useState<BreakdownGrouping>(initial)
  const rows = useQuery({ queryKey: ['reports', 'calls', 'breakdown', filters, by], queryFn: () => callBreakdown(filters, by) })
  const incoming = direction === 'In'
  const unanswered: ReportColumn<CallBreakdownRow> = incoming
    ? { key: 'missed', label: c('missed'), value: (r) => r.missed, numeric: true, total: true }
    : { key: 'notAnswered', label: c('notAnswered'), value: (r) => r.notAnswered, numeric: true, total: true }

  const columns: ReportColumn<CallBreakdownRow>[] = [
    { key: 'label', label: t(`applicationReports.groups.${by}`), value: (r) => r.label },
    { key: 'calls', label: c(incoming ? 'incomingCalls' : 'outgoingCalls'), value: (r) => r.calls, numeric: true, total: true },
    { key: 'answered', label: c('answered'), value: (r) => r.answered, numeric: true, total: true },
    unanswered,
    ...typeColumns(rows.data, arabic),
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true, total: true },
    { key: 'orderValue', label: c('orderValue'), value: (r) => r.orderValue, format: (r) => money(r.orderValue), numeric: true, total: true },
  ]
  const time = by === 'day' || by === 'week' || by === 'month'

  return (
    <ReportCard
      title={t(`callReports.sections.${name}.title`)}
      hint={t(`callReports.sections.${name}.hint`)}
      columns={columns}
      rows={rows.data}
      loading={rows.isFetching}
      error={rows.isError}
      exportName={`calls-${incoming ? 'incoming' : 'outgoing'}-by-${by}`}
      chart={{
        kind: time && (rows.data?.length ?? 0) > 1 ? 'line' : 'bar',
        x: (r) => r.label,
        series: [
          { key: 'answered', label: c('answered'), colour: SERIES_COLOURS[0] },
          incoming
            ? { key: 'missed', label: c('missed'), colour: SERIES_COLOURS[1] }
            : { key: 'notAnswered', label: c('notAnswered'), colour: SERIES_COLOURS[1] },
        ],
      }}
    >
      <Grouping
        label={t('applicationReports.groupBy')}
        value={by}
        options={['day', 'week', 'month', 'agent', 'branch']}
        onChange={setBy}
      />
    </ReportCard>
  )
}

// ---- Customers: R-04's recurring customers -----------------------------------------

function customerColumns(f: ReturnType<typeof useFormat>, which: 'calls' | 'complaints'): ReportColumn<CustomerRankRow>[] {
  const { c, money, when, stamp } = f
  return [
    { key: 'customer', label: c('customer'), value: (r) => customer(r), format: (r) => customerShown(r) },
    { key: 'number', label: c('number'), value: (r) => r.number, ltr: true },
    ...(which === 'complaints'
      ? [{ key: 'complaints', label: c('complaints'), value: (r: CustomerRankRow) => r.complaints, numeric: true }]
      : []),
    { key: 'calls', label: c('calls'), value: (r) => r.calls, numeric: true },
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true },
    { key: 'orderValue', label: c('orderValue'), value: (r) => r.orderValue, format: (r) => money(r.orderValue), numeric: true },
    { key: 'lastAt', label: c('lastCall'), value: (r) => stamp(r.lastAt), format: (r) => when(r.lastAt) },
  ]
}

function Customers({ filters }: { filters: ReportFilters }) {
  const f = useFormat()
  const recurring = useQuery({ queryKey: ['reports', 'calls', 'recurring', filters], queryFn: () => recurringCustomers(filters) })

  return (
    <>
      <ReportCard
        title={f.t('callReports.sections.recurring.title')}
        hint={f.t('callReports.sections.recurring.hint')}
        columns={customerColumns(f, 'calls')}
        rows={recurring.data}
        loading={recurring.isFetching}
        error={recurring.isError}
        exportName="recurring-customers"
        limit={LIST_LIMIT}
      />
      <CustomerBase filters={filters} />
    </>
  )
}

/** A cancellation's line (R-14), by phone or through an application. */
function problemColumns(f: ReturnType<typeof useFormat>): ReportColumn<ProblemRow>[] {
  const { c, when, stamp } = f
  return [
    { key: 'when', label: c('when'), value: (r) => stamp(r.startedAt), format: (r) => when(r.startedAt) },
    { key: 'channel', label: c('channel'), value: (r) => r.channel },
    { key: 'customer', label: c('customer'), value: (r) => customer(r), format: (r) => customerShown(r) },
    { key: 'number', label: c('number'), value: (r) => r.number, ltr: true },
    { key: 'agent', label: c('agent'), value: (r) => r.agent },
    { key: 'branch', label: c('branch'), value: (r) => r.branch },
    { key: 'notes', label: c('notes'), value: (r) => r.notes },
  ]
}

// ---- Complaints: R-05, R-17 ------------------------------------------------------------

/**
 * The complaints (Dia, 6 Oct 2026): each complaint once, however many calls
 * and messages it took — the customer's call, the agent's call back, a
 * message through an application — on one working day, 05:00 to 05:00 (the
 * server's `ComplaintCases`). Not split by the Incoming / Outgoing switch,
 * for the same reason. A complaint opens to its calls, each with its
 * recording to play and its details to open, where a supervisor marks it
 * resolved.
 */
function Complaints({ filters }: { filters: ReportFilters }) {
  const f = useFormat()
  const { t, c, decimal, when, stamp } = f
  const [by, setBy] = useState<ComplaintsGrouping>('branch')

  const list = useQuery({ queryKey: ['reports', 'calls', 'complaints', filters], queryFn: () => complaintsList(filters) })
  const grouped = useQuery({ queryKey: ['reports', 'calls', 'complaints-by', filters, by], queryFn: () => complaintsBy(filters, by) })
  const repeat = useQuery({ queryKey: ['reports', 'calls', 'repeat', filters], queryFn: () => repeatComplainers(filters) })

  const yes = (v: boolean) => (v ? t('common.yes') : t('common.no'))
  const listColumns: ReportColumn<ComplaintCase>[] = [
    { key: 'when', label: c('when'), value: (r) => stamp(r.firstAt), format: (r) => when(r.firstAt) },
    { key: 'customer', label: c('customer'), value: (r) => customer(r), format: (r) => customerShown(r) },
    { key: 'number', label: c('number'), value: (r) => r.number, ltr: true },
    { key: 'calls', label: c('calls'), value: (r) => r.calls, numeric: true },
    { key: 'applications', label: c('messages'), value: (r) => r.applications, numeric: true },
    { key: 'agent', label: c('agent'), value: (r) => r.agent },
    { key: 'branch', label: c('branch'), value: (r) => r.branch },
    { key: 'notes', label: c('notes'), value: (r) => r.notes },
    { key: 'followUp', label: c('followUp'), value: (r) => yes(r.followUp) },
    { key: 'status', label: c('status'), value: (r) => (r.resolved ? t('callReports.resolved') : t('callReports.open')) },
  ]

  const groupedColumns: ReportColumn<ComplaintsRow>[] = [
    { key: 'label', label: t(`applicationReports.groups.${by}`), value: (r) => r.label },
    { key: 'complaints', label: c('complaints'), value: (r) => r.complaints, numeric: true, total: true },
    { key: 'followUp', label: c('followUp'), value: (r) => r.followUp, numeric: true, total: true },
    { key: 'resolved', label: c('resolved'), value: (r) => r.resolved, numeric: true, total: true },
    { key: 'open', label: c('open'), value: (r) => r.open, numeric: true, total: true },
    {
      key: 'hours', label: c('hoursToResolve'), value: (r) => r.averageHoursToResolve,
      format: (r) => decimal(r.averageHoursToResolve), numeric: true,
    },
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true, total: true },
    { key: 'per100', label: c('perHundredOrders'), value: (r) => r.perHundredOrders, format: (r) => decimal(r.perHundredOrders), numeric: true },
  ]

  return (
    <>
      <ReportCard
        title={t('callReports.sections.complaints.title')}
        hint={t('callReports.sections.complaints.hint')}
        columns={listColumns}
        rows={list.data}
        loading={list.isFetching}
        error={list.isError}
        exportName="complaints"
        limit={LIST_LIMIT}
        expand={(r) => <ComplaintCalls rows={r.communications} />}
      />

      <ReportCard
        title={t('callReports.sections.complaintsBy.title')}
        hint={t('callReports.sections.complaintsBy.hint')}
        columns={groupedColumns}
        rows={grouped.data}
        loading={grouped.isFetching}
        error={grouped.isError}
        exportName={`complaints-by-${by}`}
        chart={{
          kind: 'bar',
          x: (r) => r.label,
          series: [
            { key: 'open', label: c('open'), colour: SERIES_COLOURS[1] },
            { key: 'resolved', label: c('resolved'), colour: SERIES_COLOURS[0] },
          ],
        }}
      >
        <Grouping
          label={t('applicationReports.groupBy')}
          value={by}
          options={['branch', 'agent', 'day', 'week', 'month']}
          onChange={setBy}
        />
      </ReportCard>

      <ReportCard
        title={t('callReports.sections.repeat.title')}
        hint={t('callReports.sections.repeat.hint')}
        columns={customerColumns(f, 'complaints')}
        rows={repeat.data}
        loading={repeat.isFetching}
        error={repeat.isError}
        exportName="repeat-complainers"
        limit={LIST_LIMIT}
      />
    </>
  )
}

/**
 * A complaint's calls and messages, first to last: when, which way, who, how
 * long, the recording to play, and Details for the whole call (S-03), where a
 * supervisor reads the classification and marks it resolved.
 */
function ComplaintCalls({ rows }: { rows: CallRow[] }) {
  const { t, when } = useFormat()
  const [open, setOpen] = useState<string | null>(null)
  const how = (r: CallRow) =>
    r.kind === 'App' ? r.channelName ?? '' : t(r.direction === 'Out' ? 'callReports.direction.Out' : 'callReports.direction.In')

  return (
    <ul className="space-y-3 py-2">
      {rows.map((r) => (
        <li key={r.id} className="space-y-2">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
            <span className="text-slate-200">{when(r.startedAt)}</span>
            <span className="badge">{how(r)}</span>
            <span className="text-slate-400">{r.agentDisplayName ?? ''}</span>
            {r.durationSec !== null && <span className="tabular text-slate-400" dir="ltr">{formatClock(r.durationSec)}</span>}
            {r.notes && <span className="text-slate-300">{r.notes}</span>}
            <button type="button" className="btn-ghost btn-sm" aria-expanded={open === r.id}
              onClick={() => setOpen(open === r.id ? null : r.id)}>
              {t('callReports.complaintDetails')}
            </button>
          </div>
          {r.kind !== 'App' && (
            <RecordingPlayer communicationId={r.id} hasRecording={r.hasRecording} expired={r.recordingExpired} />
          )}
          {open === r.id && <CallDetails row={r} onClose={() => setOpen(null)} />}
        </li>
      ))}
    </ul>
  )
}

// ---- Overview: R-10 ------------------------------------------------------------------

/** Monday to Sunday in the reader's language, from a week known to start on a Monday (1 Jan 2024). */
function weekdayNames(language: string): string[] {
  return Array.from({ length: 7 }, (_, i) => new Date(2024, 0, 1 + i).toLocaleDateString(language, { weekday: 'short' }))
}

/** R-10: incoming calls per hour and weekday, as a heat table (S-06), for staffing. */
function PeakHours({ filters }: { filters: ReportFilters }) {
  const { t, i18n } = useTranslation()
  const { c } = useFormat()
  const rows = useQuery({ queryKey: ['reports', 'calls', 'peak-hours', filters], queryFn: () => peakHours(filters) })
  const days = weekdayNames(i18n.language)
  // All 24 hours come back; with nothing in the period the card says so rather than drawing an empty grid.
  const data = rows.data && rows.data.some((r) => r.total > 0) ? rows.data : rows.data && []

  const columns: ReportColumn<PeakHourRow>[] = [
    { key: 'hour', label: c('hour'), value: (r) => `${String(r.hour).padStart(2, '0')}:00` },
    ...days.map((day, i): ReportColumn<PeakHourRow> => ({
      key: `d${i}`, label: day, value: (r) => r.byWeekday[i], numeric: true, total: true, heat: true,
    })),
    { key: 'total', label: c('total'), value: (r) => r.total, numeric: true, total: true },
  ]

  return (
    <ReportCard
      title={t('callReports.sections.peakHours.title')}
      hint={t('callReports.sections.peakHours.hint')}
      columns={columns}
      rows={data}
      loading={rows.isFetching}
      error={rows.isError}
      exportName="peak-hours"
    />
  )
}

// ---- Orders and channels: R-12, R-13, R-14 -------------------------------------------

function Orders({ filters }: { filters: ReportFilters }) {
  const f = useFormat()
  const { t, c, money, percent } = f
  const [trendBy, setTrendBy] = useState<TimeGrouping>('day')
  const [valueBy, setValueBy] = useState<'channel' | 'branch' | 'agent' | 'day'>('channel')
  const [cancelBy, setCancelBy] = useState<CancellationGrouping>('branch')

  const byChannel = useQuery({ queryKey: ['reports', 'calls', 'orders-by-channel', filters], queryFn: () => ordersByChannel(filters) })
  const trend = useQuery({ queryKey: ['reports', 'calls', 'orders-trend', filters, trendBy], queryFn: () => ordersTrend(filters, trendBy) })
  const value = useQuery({ queryKey: ['reports', 'calls', 'orders', filters, valueBy], queryFn: () => orderValue(filters, valueBy) })
  const rates = useQuery({ queryKey: ['reports', 'calls', 'cancellations', filters, cancelBy], queryFn: () => cancellationRates(filters, cancelBy) })
  const list = useQuery({ queryKey: ['reports', 'calls', 'cancellations-list', filters], queryFn: () => cancellationList(filters) })

  const channelColumns: ReportColumn<ChannelOrdersRow>[] = [
    { key: 'channel', label: c('channel'), value: (r) => r.channel },
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true, total: true },
    { key: 'orderValue', label: c('orderValue'), value: (r) => r.orderValue, format: (r) => money(r.orderValue), numeric: true, total: true },
    { key: 'share', label: c('share'), value: (r) => r.share, format: (r) => percent(r.share), numeric: true },
  ]
  const trendColumns: ReportColumn<ChannelOrdersTrendPoint>[] = [
    { key: 'bucket', label: c('period'), value: (r) => r.bucket },
    { key: 'phoneOrders', label: c('phoneOrders'), value: (r) => r.phoneOrders, numeric: true, total: true },
    { key: 'appOrders', label: c('appOrders'), value: (r) => r.appOrders, numeric: true, total: true },
    { key: 'phoneValue', label: c('phoneValue'), value: (r) => r.phoneValue, format: (r) => money(r.phoneValue), numeric: true, total: true },
    { key: 'appValue', label: c('appValue'), value: (r) => r.appValue, format: (r) => money(r.appValue), numeric: true, total: true },
  ]
  const valueColumns: ReportColumn<OrdersReportRow>[] = [
    { key: 'label', label: t(`applicationReports.groups.${valueBy}`), value: (r) => r.label },
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true, total: true },
    { key: 'orderValue', label: c('orderValue'), value: (r) => r.orderValue, format: (r) => money(r.orderValue), numeric: true, total: true },
    { key: 'average', label: c('average'), value: (r) => r.average, format: (r) => (r.average === null ? '' : money(r.average)), numeric: true },
  ]
  const rateColumns: ReportColumn<CancellationRateRow>[] = [
    { key: 'label', label: t(`applicationReports.groups.${cancelBy}`), value: (r) => r.label },
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true, total: true },
    { key: 'cancellations', label: c('cancellations'), value: (r) => r.cancellations, numeric: true, total: true },
    { key: 'rate', label: c('cancellationRate'), value: (r) => r.rate, format: (r) => percent(r.rate), numeric: true },
  ]

  return (
    <>
      <ReportCard title={t('callReports.sections.ordersByChannel.title')} hint={t('callReports.sections.ordersByChannel.hint')}
        columns={channelColumns} rows={byChannel.data} loading={byChannel.isFetching} error={byChannel.isError}
        exportName="orders-by-channel"
        chart={{ kind: 'bar', x: (r) => r.channel, series: [{ key: 'orders', label: c('orders'), colour: SERIES_COLOURS[0] }] }} />

      <ReportCard title={t('callReports.sections.ordersTrend.title')} hint={t('callReports.sections.ordersTrend.hint')}
        columns={trendColumns} rows={trend.data} loading={trend.isFetching} error={trend.isError}
        exportName={`orders-phone-and-apps-by-${trendBy}`}
        chart={{
          kind: (trend.data?.length ?? 0) > 1 ? 'line' : 'bar',
          x: (r) => r.bucket,
          series: [
            { key: 'phoneOrders', label: c('phoneOrders'), colour: SERIES_COLOURS[0] },
            { key: 'appOrders', label: c('appOrders'), colour: SERIES_COLOURS[1] },
          ],
        }}>
        <Grouping label={t('applicationReports.groupBy')} value={trendBy} options={['day', 'week', 'month']} onChange={setTrendBy} />
      </ReportCard>

      <ReportCard title={t('callReports.sections.orderValue.title')} hint={t('callReports.sections.orderValue.hint')}
        columns={valueColumns} rows={value.data} loading={value.isFetching} error={value.isError}
        exportName={`order-value-by-${valueBy}`}
        chart={{
          kind: valueBy === 'day' && (value.data?.length ?? 0) > 1 ? 'line' : 'bar',
          x: (r) => r.label,
          series: [{ key: 'orderValue', label: c('orderValue'), colour: SERIES_COLOURS[0] }],
        }}>
        <Grouping label={t('applicationReports.groupBy')} value={valueBy} options={['channel', 'branch', 'agent', 'day']} onChange={setValueBy} />
      </ReportCard>

      <ReportCard title={t('callReports.sections.cancellations.title')} hint={t('callReports.sections.cancellations.hint')}
        columns={rateColumns} rows={rates.data} loading={rates.isFetching} error={rates.isError}
        exportName={`cancellations-by-${cancelBy}`}
        chart={{ kind: 'bar', x: (r) => r.label, series: [{ key: 'cancellations', label: c('cancellations'), colour: SERIES_COLOURS[1] }] }}>
        <Grouping label={t('applicationReports.groupBy')} value={cancelBy} options={['branch', 'channel', 'agent']} onChange={setCancelBy} />
      </ReportCard>

      <ReportCard title={t('callReports.sections.cancellationList.title')} hint={t('callReports.sections.cancellationList.hint')}
        columns={problemColumns(f)} rows={list.data} loading={list.isFetching} error={list.isError}
        exportName="cancellations" limit={LIST_LIMIT} />
    </>
  )
}

// ---- Customers: R-16 ------------------------------------------------------------------

function CustomerBase({ filters }: { filters: ReportFilters }) {
  const f = useFormat()
  const { t, c } = f
  const [by, setBy] = useState<TimeGrouping>('month')
  const [topBy, setTopBy] = useState<'orders' | 'value'>('orders')
  const [days, setDays] = useState(30)

  const base = useQuery({ queryKey: ['reports', 'calls', 'customers', filters, by], queryFn: () => customerBase(filters, by) })
  const top = useQuery({ queryKey: ['reports', 'calls', 'top', filters, topBy], queryFn: () => topCustomers(filters, topBy) })
  const inactive = useQuery({ queryKey: ['reports', 'calls', 'inactive', filters, days], queryFn: () => inactiveCustomers(filters, days) })

  const baseColumns: ReportColumn<CustomerBaseRow>[] = [
    { key: 'bucket', label: c('period'), value: (r) => r.bucket },
    { key: 'customers', label: c('customers'), value: (r) => r.customers, numeric: true },
    { key: 'new', label: c('new'), value: (r) => r.new, numeric: true, total: true },
    { key: 'returning', label: c('returning'), value: (r) => r.returning, numeric: true },
  ]
  const inactiveColumns = customerColumns(f, 'calls').map((col) => (col.key === 'lastAt' ? { ...col, label: c('lastOrder') } : col))

  return (
    <>
      <ReportCard title={t('callReports.sections.customerBase.title')} hint={t('callReports.sections.customerBase.hint')}
        columns={baseColumns} rows={base.data} loading={base.isFetching} error={base.isError}
        exportName={`customers-by-${by}`}
        chart={{
          kind: 'bar',
          x: (r) => r.bucket,
          series: [
            { key: 'returning', label: c('returning'), colour: SERIES_COLOURS[0] },
            { key: 'new', label: c('new'), colour: SERIES_COLOURS[1] },
          ],
        }}>
        <Grouping label={t('applicationReports.groupBy')} value={by} options={['day', 'week', 'month']} onChange={setBy} />
      </ReportCard>

      <ReportCard title={t('callReports.sections.topCustomers.title')} hint={t('callReports.sections.topCustomers.hint')}
        columns={customerColumns(f, 'calls')} rows={top.data} loading={top.isFetching} error={top.isError}
        exportName={`top-customers-by-${topBy}`}>
        <Grouping label={t('callReports.rankBy')} value={topBy} options={['orders', 'value']} onChange={setTopBy}
          labelFor={(o) => t(`callReports.rank.${o}`)} />
      </ReportCard>

      <ReportCard title={t('callReports.sections.inactive.title')} hint={t('callReports.sections.inactive.hint')}
        columns={inactiveColumns} rows={inactive.data} loading={inactive.isFetching} error={inactive.isError}
        exportName={`no-order-in-${days}-days`} limit={LIST_LIMIT}>
        <label className="flex items-center gap-2 text-sm text-slate-400">
          {t('callReports.days')}
          <input type="number" min={1} max={3650} className="input w-20 py-1" value={days}
            onChange={(e) => setDays(Math.max(1, Number(e.target.value) || 30))} />
        </label>
      </ReportCard>
    </>
  )
}

// ---- Agents: R-15, and R-04 per agent -------------------------------------------------

function Agents({ filters, direction }: { filters: ReportFilters; direction: CallDirection }) {
  const { t, c, money } = useFormat()
  const rows = useQuery({ queryKey: ['reports', 'calls', 'agents', filters], queryFn: () => agentProductivity(filters) })
  const incoming = direction === 'In'

  // Incoming: what they answered and what they let ring. Outgoing: what they
  // dialled and how much of it was picked up. Never the two added together.
  const columns: ReportColumn<AgentProductivityRow>[] = [
    { key: 'agent', label: c('agent'), value: (r) => r.agent },
    ...(incoming
      ? []
      : [{ key: 'outbound', label: c('outboundMade'), value: (r: AgentProductivityRow) => r.outbound, numeric: true, total: true }]),
    { key: 'handled', label: c('answered'), value: (r) => r.handled, numeric: true, total: true },
    { key: 'duration', label: c('averageDuration'), value: (r) => r.averageDurationSec, format: (r) => clock(r.averageDurationSec), numeric: true },
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true, total: true },
    { key: 'orderValue', label: c('orderValue'), value: (r) => r.orderValue, format: (r) => money(r.orderValue), numeric: true, total: true },
    { key: 'unclassified', label: c('unclassified'), value: (r) => r.unclassified, numeric: true, total: true },
    ...(incoming
      ? [{ key: 'missed', label: c('missed'), value: (r: AgentProductivityRow) => r.missed, numeric: true, total: true }]
      : []),
  ]

  return (
    <>
      <ReportCard title={t('callReports.sections.productivity.title')}
        hint={t(incoming ? 'callReports.sections.productivity.hint' : 'callReports.sections.productivity.hintOut')}
        columns={columns} rows={rows.data} loading={rows.isFetching} error={rows.isError}
        exportName={`agent-productivity-${incoming ? 'incoming' : 'outgoing'}`}
        chart={{
          kind: 'bar',
          x: (r) => r.agent,
          series: incoming
            ? [
                { key: 'handled', label: c('answered'), colour: SERIES_COLOURS[0] },
                { key: 'missed', label: c('missed'), colour: SERIES_COLOURS[1] },
              ]
            : [
                { key: 'outbound', label: c('outboundMade'), colour: SERIES_COLOURS[1] },
                { key: 'handled', label: c('answered'), colour: SERIES_COLOURS[0] },
              ],
        }} />
      <Breakdown filters={filters} direction={direction} initial="agent" name="byAgent" />
    </>
  )
}

// ---- Problems: R-11 --------------------------------------------------------------------

function Missed({ filters }: { filters: ReportFilters }) {
  const { t, c, when, stamp, percent } = useFormat()
  const [by, setBy] = useState<MissedGrouping>('day')
  const grouped = useQuery({ queryKey: ['reports', 'calls', 'missed', filters, by], queryFn: () => missedCalls(filters, by) })
  const list = useQuery({ queryKey: ['reports', 'calls', 'missed-list', filters], queryFn: () => missedList(filters) })
  const overTime = by === 'day' || by === 'week' || by === 'month'

  const groupedColumns: ReportColumn<MissedRow>[] = [
    { key: 'label', label: t(`applicationReports.groups.${by}`), value: (r) => r.label },
    { key: 'inbound', label: c('inbound'), value: (r) => r.inbound, numeric: true, total: true },
    { key: 'missedOnly', label: c('missedOnly'), value: (r) => r.missed, numeric: true, total: true },
    { key: 'rejected', label: c('rejected'), value: (r) => r.rejected, numeric: true, total: true },
    { key: 'abandoned', label: c('abandoned'), value: (r) => r.abandoned, numeric: true, total: true },
    { key: 'total', label: c('total'), value: (r) => r.total, numeric: true, total: true },
    { key: 'rate', label: c('missedRate'), value: (r) => r.rate, format: (r) => percent(r.rate), numeric: true },
  ]
  const listColumns: ReportColumn<MissedCallRow>[] = [
    { key: 'when', label: c('when'), value: (r) => stamp(r.startedAt), format: (r) => when(r.startedAt) },
    { key: 'status', label: c('status'), value: (r) => t(`history.statuses.${r.status}`) },
    { key: 'customer', label: c('customer'), value: (r) => customer(r), format: (r) => customerShown(r) },
    { key: 'number', label: c('number'), value: (r) => r.number, ltr: true },
    { key: 'agent', label: c('agent'), value: (r) => r.agent },
    { key: 'branch', label: c('branch'), value: (r) => r.branch },
    { key: 'notes', label: c('notes'), value: (r) => r.notes },
  ]

  return (
    <>
      <ReportCard title={t('callReports.sections.missed.title')} hint={t('callReports.sections.missed.hint')}
        columns={groupedColumns} rows={grouped.data} loading={grouped.isFetching} error={grouped.isError}
        exportName={`missed-by-${by}`}
        chart={{
          kind: overTime && (grouped.data?.length ?? 0) > 1 ? 'line' : 'bar',
          x: (r) => r.label,
          series: [
            { key: 'missed', label: c('missedOnly'), colour: SERIES_COLOURS[1] },
            { key: 'rejected', label: c('rejected'), colour: SERIES_COLOURS[2] },
            { key: 'abandoned', label: c('abandoned'), colour: SERIES_COLOURS[3] },
          ],
        }}>
        <Grouping label={t('applicationReports.groupBy')} value={by} options={['day', 'week', 'month', 'hour', 'agent', 'branch']} onChange={setBy} />
      </ReportCard>
      <ReportCard title={t('callReports.sections.missedList.title')} hint={t('callReports.sections.missedList.hint')}
        columns={listColumns} rows={list.data} loading={list.isFetching} error={list.isError}
        exportName="missed-calls" limit={LIST_LIMIT} />
    </>
  )
}

// ---- Abandoned: R-20 -------------------------------------------------------------------

/**
 * R-20: the calls that gave up in the queue (S-55), for the chosen period, from
 * what the PBX import has already saved. The server checks the PBX on its own
 * every minute or so; the Fetch button downloads the chosen period now, for a
 * day the timer has not reached or a PBX that was out of reach.
 */
function Abandoned({ filters }: { filters: ReportFilters }) {
  const { t, c, when, stamp, percent, decimal } = useFormat()
  const queryClient = useQueryClient()
  const [by, setBy] = useState<AbandonedGrouping>('day')

  const grouped = useQuery({ queryKey: ['reports', 'calls', 'abandoned', filters, by], queryFn: () => abandonedCalls(filters, by) })
  const list = useQuery({ queryKey: ['reports', 'calls', 'abandoned-list', filters], queryFn: () => abandonedList(filters) })
  const status = useQuery({ queryKey: ['pbx', 'abandoned-import'], queryFn: getAbandonedImport })

  const fetchNow = useMutation({
    mutationFn: () => fetchAbandoned(filters.from, filters.to),
    onSuccess: (result) => {
      queryClient.setQueryData(['pbx', 'abandoned-import'], result.status)
      // Every report may have moved: a new abandoned call, and rings no longer counted twice.
      if (result.ok) void queryClient.invalidateQueries({ queryKey: ['reports'] })
    },
  })

  const configured = status.data?.configured ?? false
  const result = fetchNow.data
  const message = !status.data
    ? ''
    : !configured
      ? t('callReports.abandoned.notSetUp')
      : fetchNow.isError
        ? t('callReports.abandoned.failed', { error: t('dashboard.failed') })
        : result
          ? result.ok
            ? t('callReports.abandoned.fetched', { from: result.from, to: result.to, abandoned: result.abandoned, added: result.added })
            : t('callReports.abandoned.failed', { error: result.error })
          : [
              status.data.lastCheckedAt ? t('callReports.abandoned.lastChecked', { when: when(status.data.lastCheckedAt) }) : '',
              t('callReports.abandoned.fetchHint'),
            ].filter(Boolean).join(' ')
  const failed = fetchNow.isError || (result !== undefined && !result.ok)

  const groupedColumns: ReportColumn<AbandonedRow>[] = [
    { key: 'label', label: t(`applicationReports.groups.${by}`), value: (r) => r.label },
    { key: 'inbound', label: c('inbound'), value: (r) => r.inbound, numeric: true, total: true },
    { key: 'abandoned', label: c('abandoned'), value: (r) => r.abandoned, numeric: true, total: true },
    { key: 'rate', label: c('abandonedRate'), value: (r) => r.rate, format: (r) => percent(r.rate), numeric: true },
    { key: 'averageWait', label: c('averageWait'), value: (r) => r.averageWaitSec, format: (r) => clock(r.averageWaitSec), numeric: true },
    { key: 'maxWait', label: c('maxWait'), value: (r) => r.maxWaitSec, format: (r) => clock(r.maxWaitSec), numeric: true },
    { key: 'calledBack', label: c('calledBack'), value: (r) => r.calledBack, numeric: true, total: true },
    {
      key: 'toCallBack', label: c('averageMinutesToCallBack'), value: (r) => r.averageMinutesToCallBack,
      format: (r) => decimal(r.averageMinutesToCallBack), numeric: true,
    },
  ]
  const listColumns: ReportColumn<AbandonedCallRow>[] = [
    { key: 'hungUp', label: c('hungUp'), value: (r) => stamp(r.endedAt ?? r.startedAt), format: (r) => when(r.endedAt ?? r.startedAt) },
    { key: 'wait', label: c('wait'), value: (r) => r.waitSec, format: (r) => clock(r.waitSec), numeric: true },
    { key: 'customer', label: c('customer'), value: (r) => customer(r), format: (r) => customerShown(r) },
    { key: 'number', label: c('number'), value: (r) => r.number, ltr: true },
    { key: 'queue', label: c('queue'), value: (r) => r.queue },
    { key: 'rings', label: c('rings'), value: (r) => r.rings, numeric: true, total: true },
    {
      key: 'calledBackAt', label: c('calledBackAt'),
      value: (r) => (r.calledBackAt ? stamp(r.calledBackAt) : ''),
      format: (r) => (r.calledBackAt ? when(r.calledBackAt) : t('callReports.abandoned.notYet')),
    },
    { key: 'calledBackBy', label: c('calledBackBy'), value: (r) => r.calledBackBy },
    { key: 'minutesToCallBack', label: c('minutesToCallBack'), value: (r) => r.minutesToCallBack, numeric: true },
  ]

  return (
    <>
      <section className="card card-body flex flex-wrap items-center gap-3" aria-label={t('callReports.abandoned.fetch')}>
        <button
          type="button"
          className="btn-primary btn-sm"
          disabled={!configured || fetchNow.isPending}
          onClick={() => fetchNow.mutate()}
        >
          {fetchNow.isPending ? t('callReports.abandoned.fetching') : t('callReports.abandoned.fetch')}
        </button>
        {/* Without the status the button cannot know whether it may fetch,
            and the line would be blank: say why (M-W03). */}
        {status.isError && !status.data ? (
          <LoadError inline message={t('callReports.abandoned.statusFailed')} onRetry={() => void status.refetch()} />
        ) : (
          <p role="status" className={`text-sm ${failed ? 'text-red-400' : 'text-slate-400'}`}>{message}</p>
        )}
      </section>

      <ReportCard title={t('callReports.sections.abandoned.title')} hint={t('callReports.sections.abandoned.hint')}
        columns={groupedColumns} rows={grouped.data} loading={grouped.isFetching} error={grouped.isError}
        exportName={`abandoned-by-${by}`}
        chart={{
          kind: by !== 'hour' && (grouped.data?.length ?? 0) > 1 ? 'line' : 'bar',
          x: (r) => r.label,
          series: [
            { key: 'abandoned', label: c('abandoned'), colour: SERIES_COLOURS[3] },
            { key: 'calledBack', label: c('calledBack'), colour: SERIES_COLOURS[0] },
          ],
        }}>
        <Grouping label={t('applicationReports.groupBy')} value={by} options={['day', 'week', 'month', 'hour']} onChange={setBy} />
      </ReportCard>

      <ReportCard title={t('callReports.sections.abandonedList.title')} hint={t('callReports.sections.abandonedList.hint')}
        columns={listColumns} rows={list.data} loading={list.isFetching} error={list.isError}
        exportName="abandoned-calls" limit={LIST_LIMIT} />
    </>
  )
}

// ---- Data quality: R-18 ----------------------------------------------------------------

interface Figure {
  measure: string
  count: number
}

function Quality({ filters }: { filters: ReportFilters }) {
  const { t, c, when, stamp } = useFormat()
  const figures = useQuery({ queryKey: ['reports', 'calls', 'data-quality', filters], queryFn: () => dataQuality(filters) })
  const numbers = useQuery({ queryKey: ['reports', 'calls', 'unknown-numbers', filters], queryFn: () => unknownNumbers(filters) })
  const names = useQuery({ queryKey: ['reports', 'calls', 'duplicate-names'], queryFn: duplicateNames })

  const d = figures.data
  const figureRows: Figure[] | undefined = d && [
    { measure: t('callReports.quality.unclassified'), count: d.unclassified },
    { measure: t('callReports.quality.unknownCalls'), count: d.unknownCalls },
    { measure: t('callReports.quality.unknownNumbers'), count: d.unknownNumbers },
    { measure: t('callReports.quality.duplicateNames'), count: d.duplicateNames },
  ]
  const figureColumns: ReportColumn<Figure>[] = [
    { key: 'measure', label: c('measure'), value: (r) => r.measure },
    { key: 'count', label: c('count'), value: (r) => r.count, numeric: true },
  ]
  const numberColumns: ReportColumn<UnknownNumberRow>[] = [
    { key: 'number', label: c('number'), value: (r) => r.number, ltr: true },
    { key: 'calls', label: c('calls'), value: (r) => r.calls, numeric: true, total: true },
    { key: 'firstAt', label: c('firstCall'), value: (r) => stamp(r.firstAt), format: (r) => when(r.firstAt) },
    { key: 'lastAt', label: c('lastCall'), value: (r) => stamp(r.lastAt), format: (r) => when(r.lastAt) },
  ]
  const nameColumns: ReportColumn<DuplicateNameRow>[] = [
    { key: 'name', label: c('name'), value: (r) => r.name },
    { key: 'contacts', label: c('contacts'), value: (r) => r.contacts, numeric: true },
    { key: 'numbers', label: c('numbers'), value: (r) => r.numbers, ltr: true },
  ]

  return (
    <>
      <ReportCard title={t('callReports.sections.quality.title')} hint={t('callReports.sections.quality.hint')}
        columns={figureColumns} rows={figureRows} loading={figures.isFetching} error={figures.isError} exportName="data-quality" />
      <ReportCard title={t('callReports.sections.unknownNumbers.title')} hint={t('callReports.sections.unknownNumbers.hint')}
        columns={numberColumns} rows={numbers.data} loading={numbers.isFetching} error={numbers.isError}
        exportName="unknown-numbers" limit={LIST_LIMIT} />
      <ReportCard title={t('callReports.sections.duplicateNames.title')} hint={t('callReports.sections.duplicateNames.hint')}
        columns={nameColumns} rows={names.data} loading={names.isFetching} error={names.isError}
        exportName="shared-names" limit={LIST_LIMIT} />
    </>
  )
}
