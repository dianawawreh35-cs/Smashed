import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import type { OrdersReportRow, ReportFilters, TypeCount } from '../api/applicationReports'
import {
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
  AgentProductivityRow,
  BreakdownGrouping,
  CallBreakdownRow,
  CallSummaryRow,
  CancellationGrouping,
  CancellationRateRow,
  ChannelOrdersRow,
  ChannelOrdersTrendPoint,
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
import ReportCard, { SERIES_COLOURS } from '../components/ReportCard'
import type { ReportColumn } from '../components/ReportCard'
import { Grouping, ReportFilterBar } from '../components/ReportFilters'
import { localDate, useReportFilters } from '../lib/reportFilters'

/** The groups the reports are shown in (Dia, 25 Sep): one page, one filter bar, a tab each. */
const TABS = ['overview', 'orders', 'customers', 'agents', 'problems', 'quality'] as const
type Tab = (typeof TABS)[number]

/** How many rows of a list report are drawn; the export holds them all. */
const LIST_LIMIT = 200

/**
 * The call reports (R-01 to R-18): calls only, except where the point is the
 * comparison with the apps (R-01's calls vs app, R-12 to R-14; Dia, 25 Sep).
 * R-19 (the daily e-mail) is not here, and neither are R-20, R-21 or the
 * abandoned half of R-11: they need the PBX's call records (S-55), and until
 * those are imported they are left off rather than shown empty (Dia, 25 Sep).
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
  const { draft, filters, set, choosePreset } = useReportFilters('week')

  return (
    <div className="space-y-6">
      <div>
        <h2 className="page-title">{t('callReports.heading')}</h2>
        <p className="page-subtitle">{t('callReports.intro')}</p>
      </div>

      <ReportFilterBar draft={draft} set={set} choosePreset={choosePreset} includePhone />

      <div role="tablist" aria-label={t('callReports.heading')} className="flex flex-wrap gap-2 border-b border-ink-700 pb-2">
        {TABS.map((name) => (
          <button
            key={name}
            type="button"
            role="tab"
            aria-selected={tab === name}
            className={tab === name ? 'btn-primary btn-sm' : 'btn-ghost btn-sm'}
            onClick={() => setParams(name === 'overview' ? {} : { tab: name }, { replace: true })}
          >
            {t(`callReports.tabs.${name}`)}
          </button>
        ))}
      </div>

      <div role="tabpanel" aria-label={t(`callReports.tabs.${tab}`)} className="space-y-6">
        {tab === 'overview' && <Overview filters={filters} />}
        {tab === 'orders' && <Orders filters={filters} />}
        {tab === 'customers' && <Customers filters={filters} />}
        {tab === 'agents' && <Agents filters={filters} />}
        {tab === 'problems' && <Problems filters={filters} />}
        {tab === 'quality' && <Quality filters={filters} />}
      </div>
    </div>
  )
}

// ---- shared pieces ------------------------------------------------------------

function useFormat() {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  return {
    t,
    arabic,
    c: (key: string) => t(`callReports.columns.${key}`),
    money: (v: number) => v.toLocaleString(i18n.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
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

/** The customer, or the number when the contact has no name. */
const customer = (r: { name?: string | null; customer?: string | null; number: string | null }) =>
  r.name ?? r.customer ?? r.number ?? ''

// ---- Overview: R-01, R-02, R-03, R-04 per day -----------------------------------

function Overview({ filters }: { filters: ReportFilters }) {
  const { t, c, typeLabel, percent } = useFormat()
  const [by, setBy] = useState<TimeGrouping>('day')

  const summary = useQuery({ queryKey: ['reports', 'calls', 'summary', filters, by], queryFn: () => callSummary(filters, by) })
  const byType = useQuery({ queryKey: ['reports', 'calls', 'by-type', filters], queryFn: () => callsByType(filters) })

  const summaryColumns: ReportColumn<CallSummaryRow>[] = [
    { key: 'bucket', label: c('period'), value: (r) => r.bucket },
    { key: 'communications', label: c('communications'), value: (r) => r.communications, numeric: true, total: true },
    { key: 'calls', label: c('calls'), value: (r) => r.calls, numeric: true, total: true },
    { key: 'messages', label: c('messages'), value: (r) => r.messages, numeric: true, total: true },
    { key: 'inbound', label: c('inbound'), value: (r) => r.inbound, numeric: true, total: true },
    { key: 'outbound', label: c('outbound'), value: (r) => r.outbound, numeric: true, total: true },
    { key: 'answered', label: c('answered'), value: (r) => r.answered, numeric: true, total: true },
    { key: 'missed', label: c('missed'), value: (r) => r.missed, numeric: true, total: true },
    { key: 'blocked', label: c('blocked'), value: (r) => r.blocked, numeric: true, total: true },
  ]

  const typeColumnsR03: ReportColumn<TypeShareRow>[] = [
    { key: 'type', label: c('type'), value: (r) => typeLabel(r) },
    { key: 'count', label: c('calls'), value: (r) => r.count, numeric: true, total: true },
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
        exportName={`communications-by-${by}`}
        chart={{
          // A line needs two points; one day is a bar.
          kind: (summary.data?.length ?? 0) > 1 ? 'line' : 'bar',
          x: (r) => r.bucket,
          series: [
            { key: 'answered', label: c('answered'), colour: SERIES_COLOURS[0] },
            { key: 'missed', label: c('missed'), colour: SERIES_COLOURS[1] },
            { key: 'messages', label: c('messages'), colour: SERIES_COLOURS[2] },
          ],
        }}
      >
        <Grouping label={t('applicationReports.groupBy')} value={by} options={['day', 'week', 'month']} onChange={setBy} />
      </ReportCard>

      <section className="card card-body" aria-label={t('callReports.sections.list.title')}>
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
        exportName="calls-by-type"
        chart={{
          kind: 'bar',
          x: (r) => typeLabel(r),
          series: [{ key: 'count', label: c('calls'), colour: SERIES_COLOURS[0] }],
        }}
      />

      <Breakdown filters={filters} initial="day" name="breakdown" />

      <PeakHours filters={filters} />
    </>
  )
}

/** R-04: per day, week, month, agent or branch, and per type within each. */
function Breakdown({ filters, initial, name }: { filters: ReportFilters; initial: BreakdownGrouping; name: 'breakdown' | 'byAgent' }) {
  const { t, c, arabic, money } = useFormat()
  const [by, setBy] = useState<BreakdownGrouping>(initial)
  const rows = useQuery({ queryKey: ['reports', 'calls', 'breakdown', filters, by], queryFn: () => callBreakdown(filters, by) })

  const columns: ReportColumn<CallBreakdownRow>[] = [
    { key: 'label', label: t(`applicationReports.groups.${by}`), value: (r) => r.label },
    { key: 'calls', label: c('calls'), value: (r) => r.calls, numeric: true, total: true },
    { key: 'answered', label: c('answered'), value: (r) => r.answered, numeric: true, total: true },
    { key: 'missed', label: c('missed'), value: (r) => r.missed, numeric: true, total: true },
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
      exportName={`calls-by-${by}`}
      chart={{
        kind: time && (rows.data?.length ?? 0) > 1 ? 'line' : 'bar',
        x: (r) => r.label,
        series: [
          { key: 'answered', label: c('answered'), colour: SERIES_COLOURS[0] },
          { key: 'missed', label: c('missed'), colour: SERIES_COLOURS[1] },
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
    { key: 'customer', label: c('customer'), value: (r) => customer(r) },
    { key: 'number', label: c('number'), value: (r) => r.number },
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

/** A complaint's or a cancellation's line (R-05, R-14). The channel shows only where apps are counted too. */
function problemColumns(f: ReturnType<typeof useFormat>, withChannel: boolean): ReportColumn<ProblemRow>[] {
  const { t, c, when, stamp } = f
  const yes = (v: boolean) => (v ? t('common.yes') : t('common.no'))
  return [
    { key: 'when', label: c('when'), value: (r) => stamp(r.startedAt), format: (r) => when(r.startedAt) },
    ...(withChannel ? [{ key: 'channel', label: c('channel'), value: (r: ProblemRow) => r.channel }] : []),
    { key: 'customer', label: c('customer'), value: (r) => customer(r) },
    { key: 'number', label: c('number'), value: (r) => r.number },
    { key: 'agent', label: c('agent'), value: (r) => r.agent },
    { key: 'branch', label: c('branch'), value: (r) => r.branch },
    { key: 'notes', label: c('notes'), value: (r) => r.notes },
    ...(withChannel
      ? []
      : [
          { key: 'followUp', label: c('followUp'), value: (r: ProblemRow) => yes(r.followUp) },
          { key: 'status', label: c('status'), value: (r: ProblemRow) => (r.resolved ? t('callReports.resolved') : t('callReports.open')) },
        ]),
  ]
}

// ---- Problems: R-05 --------------------------------------------------------------------

function Problems({ filters }: { filters: ReportFilters }) {
  const f = useFormat()
  const { t, c, decimal } = f
  const [by, setBy] = useState<ComplaintsGrouping>('branch')

  const list = useQuery({ queryKey: ['reports', 'calls', 'complaints', filters], queryFn: () => complaintsList(filters) })
  const grouped = useQuery({ queryKey: ['reports', 'calls', 'complaints-by', filters, by], queryFn: () => complaintsBy(filters, by) })
  const repeat = useQuery({ queryKey: ['reports', 'calls', 'repeat', filters], queryFn: () => repeatComplainers(filters) })

  const listColumns = problemColumns(f, false)

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

      <Missed filters={filters} />
    </>
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
        columns={problemColumns(f, true)} rows={list.data} loading={list.isFetching} error={list.isError}
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

function Agents({ filters }: { filters: ReportFilters }) {
  const { t, c, money } = useFormat()
  const rows = useQuery({ queryKey: ['reports', 'calls', 'agents', filters], queryFn: () => agentProductivity(filters) })
  const clock = (s: number | null) => (s === null ? '' : `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`)

  const columns: ReportColumn<AgentProductivityRow>[] = [
    { key: 'agent', label: c('agent'), value: (r) => r.agent },
    { key: 'handled', label: c('handled'), value: (r) => r.handled, numeric: true, total: true },
    { key: 'inbound', label: c('inboundAnswered'), value: (r) => r.inbound, numeric: true, total: true },
    { key: 'outbound', label: c('outboundMade'), value: (r) => r.outbound, numeric: true, total: true },
    { key: 'duration', label: c('averageDuration'), value: (r) => r.averageDurationSec, format: (r) => clock(r.averageDurationSec), numeric: true },
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true, total: true },
    { key: 'orderValue', label: c('orderValue'), value: (r) => r.orderValue, format: (r) => money(r.orderValue), numeric: true, total: true },
    { key: 'unclassified', label: c('unclassified'), value: (r) => r.unclassified, numeric: true, total: true },
    { key: 'missed', label: c('missed'), value: (r) => r.missed, numeric: true, total: true },
  ]

  return (
    <>
      <ReportCard title={t('callReports.sections.productivity.title')} hint={t('callReports.sections.productivity.hint')}
        columns={columns} rows={rows.data} loading={rows.isFetching} error={rows.isError}
        exportName="agent-productivity"
        chart={{
          kind: 'bar',
          x: (r) => r.agent,
          series: [
            { key: 'handled', label: c('handled'), colour: SERIES_COLOURS[0] },
            { key: 'missed', label: c('missed'), colour: SERIES_COLOURS[1] },
          ],
        }} />
      <Breakdown filters={filters} initial="agent" name="byAgent" />
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
    { key: 'total', label: c('missed'), value: (r) => r.total, numeric: true, total: true },
    { key: 'rate', label: c('missedRate'), value: (r) => r.rate, format: (r) => percent(r.rate), numeric: true },
  ]
  const listColumns: ReportColumn<MissedCallRow>[] = [
    { key: 'when', label: c('when'), value: (r) => stamp(r.startedAt), format: (r) => when(r.startedAt) },
    { key: 'status', label: c('status'), value: (r) => t(`history.statuses.${r.status}`) },
    { key: 'customer', label: c('customer'), value: (r) => customer(r) },
    { key: 'number', label: c('number'), value: (r) => r.number },
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
    { key: 'number', label: c('number'), value: (r) => r.number },
    { key: 'calls', label: c('calls'), value: (r) => r.calls, numeric: true, total: true },
    { key: 'firstAt', label: c('firstCall'), value: (r) => stamp(r.firstAt), format: (r) => when(r.firstAt) },
    { key: 'lastAt', label: c('lastCall'), value: (r) => stamp(r.lastAt), format: (r) => when(r.lastAt) },
  ]
  const nameColumns: ReportColumn<DuplicateNameRow>[] = [
    { key: 'name', label: c('name'), value: (r) => r.name },
    { key: 'contacts', label: c('contacts'), value: (r) => r.contacts, numeric: true },
    { key: 'numbers', label: c('numbers'), value: (r) => r.numbers },
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
