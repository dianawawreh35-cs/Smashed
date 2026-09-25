import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  issuesByChannel,
  messagesByAgent,
  messagesByChannel,
  messagesTrend,
  ordersReport,
} from '../api/applicationReports'
import type {
  AgentReportRow,
  ChannelReportRow,
  IssuesReportRow,
  OrdersGrouping,
  OrdersReportRow,
  ReportFilters,
  TrendGrouping,
  TrendPoint,
} from '../api/applicationReports'
import { startOfDay, startOfNextDay } from '../api/calls'
import { listChannels } from '../api/channels'
import { listClassificationTypes } from '../api/classifications'
import { listBranches } from '../api/delivery'
import { listUsers } from '../api/users'
import ReportCard, { SERIES_COLOURS } from '../components/ReportCard'
import type { ReportColumn } from '../components/ReportCard'
import { FilterSelect as Select } from '../components/SearchControls'

type Preset = 'today' | 'week' | 'month' | 'custom'

/** The filters as chosen: days, not instants, until they are sent. */
interface Draft {
  preset: Preset
  from: string
  to: string
  agentId: string
  branchId: string
  channelId: string
  typeId: string
}

/** A date as the date input writes it, yyyy-mm-dd, in the browser's own time zone. */
function localDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/**
 * The days a preset names, ending today. The week starts on Monday, as the
 * server's weekly trend buckets do, so "this week" and the week rows agree.
 */
function presetRange(preset: Preset, today = new Date()): { from: string; to: string } {
  const to = localDate(today)
  if (preset === 'week') {
    const monday = new Date(today)
    monday.setDate(today.getDate() - ((today.getDay() + 6) % 7))
    return { from: localDate(monday), to }
  }
  if (preset === 'month') {
    return { from: localDate(new Date(today.getFullYear(), today.getMonth(), 1)), to }
  }
  return { from: to, to }
}

function toFilters(d: Draft): ReportFilters {
  const id = (v: string) => (v ? v : undefined)
  return {
    // Days in the supervisor's own time zone, sent as instants: from the start
    // of the first day to the start of the day after the last.
    from: d.from ? startOfDay(d.from) : undefined,
    to: d.to ? startOfNextDay(d.to) : undefined,
    agentId: id(d.agentId),
    branchId: id(d.branchId),
    channelId: id(d.channelId),
    typeId: id(d.typeId),
  }
}

const ORDERS_GROUPINGS: OrdersGrouping[] = ['channel', 'branch', 'agent', 'day']
const TREND_GROUPINGS: TrendGrouping[] = ['day', 'week', 'month', 'hour']

/**
 * The application reports (A-72, R-12, R-13): about messages, never calls.
 *
 * One filter row (S-07) scopes all five reports, so a supervisor narrowing to
 * one agent sees that agent everywhere at once. The filters apply as they are
 * changed: they are dates and drop-downs, nothing is typed, and each report is
 * a handful of aggregate rows. The two groupings belong to one report each and
 * sit on that report's card.
 *
 * Each report is a table and, where the figures are counts or amounts, a chart
 * (S-06): bars for categories, a line for the trend, following the dataviz
 * guidance. A count and an amount never share an axis — the order value goes
 * in the table, or gets a chart of its own.
 */
export default function ApplicationReportsPage() {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const [draft, setDraft] = useState<Draft>(() => ({
    preset: 'week',
    ...presetRange('week'),
    agentId: '', branchId: '', channelId: '', typeId: '',
  }))
  const [ordersBy, setOrdersBy] = useState<OrdersGrouping>('channel')
  const [trendBy, setTrendBy] = useState<TrendGrouping>('day')

  const filters = toFilters(draft)
  const set = <K extends keyof Draft>(key: K) => (value: Draft[K]) => setDraft((d) => ({ ...d, [key]: value }))

  function choosePreset(preset: Preset) {
    setDraft((d) => (preset === 'custom' ? { ...d, preset } : { ...d, preset, ...presetRange(preset) }))
  }

  const agents = useQuery({ queryKey: ['users'], queryFn: listUsers })
  const branches = useQuery({ queryKey: ['branches'], queryFn: listBranches })
  const channels = useQuery({ queryKey: ['channels', 'all'], queryFn: () => listChannels(true) })
  const types = useQuery({ queryKey: ['classification', 'types'], queryFn: listClassificationTypes })

  const byChannel = useQuery({
    queryKey: ['reports', 'applications', 'by-channel', filters],
    queryFn: () => messagesByChannel(filters),
  })
  const orders = useQuery({
    queryKey: ['reports', 'applications', 'orders', filters, ordersBy],
    queryFn: () => ordersReport(filters, ordersBy),
  })
  const trend = useQuery({
    queryKey: ['reports', 'applications', 'trend', filters, trendBy],
    queryFn: () => messagesTrend(filters, trendBy),
  })
  const byAgent = useQuery({
    queryKey: ['reports', 'applications', 'by-agent', filters],
    queryFn: () => messagesByAgent(filters),
  })
  const issues = useQuery({
    queryKey: ['reports', 'applications', 'issues', filters],
    queryFn: () => issuesByChannel(filters),
  })

  const c = (key: string) => t(`applicationReports.columns.${key}`)
  const money = (v: number) => v.toLocaleString(i18n.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 })

  // One column per type seen in the period, after the fixed ones: "per type
  // within each channel" as columns, so the table stays one row per channel.
  const typeNames = [...new Map(
    (byChannel.data ?? []).flatMap((r) => r.byType).map((ty) => [ty.typeName, ty]),
  ).values()]
  const channelColumns: ReportColumn<ChannelReportRow>[] = [
    { key: 'channel', label: c('channel'), value: (r) => r.channel },
    { key: 'messages', label: c('messages'), value: (r) => r.messages, numeric: true, total: true },
    { key: 'unclassified', label: c('unclassified'), value: (r) => r.unclassified, numeric: true, total: true },
    ...typeNames.map((ty): ReportColumn<ChannelReportRow> => ({
      key: `type:${ty.typeName}`,
      label: arabic ? ty.labelAr : ty.labelEn,
      value: (r) => r.byType.find((x) => x.typeName === ty.typeName)?.count ?? 0,
      numeric: true,
      total: true,
    })),
  ]

  const ordersColumns: ReportColumn<OrdersReportRow>[] = [
    { key: 'label', label: t(`applicationReports.groups.${ordersBy}`), value: (r) => r.label },
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true, total: true },
    { key: 'orderValue', label: c('orderValue'), value: (r) => r.orderValue, format: (r) => money(r.orderValue), numeric: true, total: true },
    { key: 'average', label: c('average'), value: (r) => r.average, format: (r) => (r.average === null ? '' : money(r.average)), numeric: true },
  ]

  const trendColumns: ReportColumn<TrendPoint>[] = [
    { key: 'bucket', label: c('period'), value: (r) => r.bucket, format: (r) => bucketLabel(r.bucket, trendBy) },
    { key: 'messages', label: c('messages'), value: (r) => r.messages, numeric: true, total: true },
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true, total: true },
    { key: 'orderValue', label: c('orderValue'), value: (r) => r.orderValue, format: (r) => money(r.orderValue), numeric: true, total: true },
  ]

  const agentColumns: ReportColumn<AgentReportRow>[] = [
    { key: 'agent', label: c('agent'), value: (r) => r.agent },
    { key: 'messages', label: c('messages'), value: (r) => r.messages, numeric: true, total: true },
    { key: 'orders', label: c('orders'), value: (r) => r.orders, numeric: true, total: true },
    { key: 'orderValue', label: c('orderValue'), value: (r) => r.orderValue, format: (r) => money(r.orderValue), numeric: true, total: true },
    { key: 'unclassified', label: c('unclassified'), value: (r) => r.unclassified, numeric: true, total: true },
  ]

  const issuesColumns: ReportColumn<IssuesReportRow>[] = [
    { key: 'channel', label: c('channel'), value: (r) => r.channel },
    { key: 'messages', label: c('messages'), value: (r) => r.messages, numeric: true, total: true },
    { key: 'cancellations', label: c('cancellations'), value: (r) => r.cancellations, numeric: true, total: true },
    { key: 'complaints', label: c('complaints'), value: (r) => r.complaints, numeric: true, total: true },
  ]

  const presets: Preset[] = ['today', 'week', 'month', 'custom']

  return (
    <div className="space-y-6">
      <div>
        <h2 className="page-title">{t('applicationReports.heading')}</h2>
        <p className="page-subtitle">{t('applicationReports.intro')}</p>
      </div>

      <form className="card card-body space-y-4" aria-label={t('applicationReports.filters')} onSubmit={(e) => e.preventDefault()}>
        <div className="flex flex-wrap items-center gap-2" role="group" aria-label={t('applicationReports.period')}>
          <span className="field-label me-2">{t('applicationReports.period')}</span>
          {presets.map((preset) => (
            <button
              key={preset}
              type="button"
              aria-pressed={draft.preset === preset}
              className={draft.preset === preset ? 'btn-primary btn-sm' : 'btn-ghost btn-sm'}
              onClick={() => choosePreset(preset)}
            >
              {t(`applicationReports.presets.${preset}`)}
            </button>
          ))}
        </div>

        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          <label className="field">
            <span className="field-label">{t('applicationReports.from')}</span>
            <input type="date" className="input" value={draft.from} disabled={draft.preset !== 'custom'}
              onChange={(e) => set('from')(e.target.value)} />
          </label>
          <label className="field">
            <span className="field-label">{t('applicationReports.to')}</span>
            <input type="date" className="input" value={draft.to} disabled={draft.preset !== 'custom'}
              onChange={(e) => set('to')(e.target.value)} />
          </label>
          <Select label={t('applicationReports.columns.channel')} value={draft.channelId} onChange={set('channelId')}>
            {(channels.data ?? []).filter((ch) => !ch.isSystem).map((ch) => (
              <option key={ch.id} value={ch.id}>{ch.name}</option>
            ))}
          </Select>
          <Select label={t('calls.columns.agent')} value={draft.agentId} onChange={set('agentId')}>
            {(agents.data ?? []).filter((u) => u.role === 'Agent').map((u) => (
              <option key={u.id} value={u.id}>{u.displayName}</option>
            ))}
          </Select>
          <Select label={t('calls.columns.branch')} value={draft.branchId} onChange={set('branchId')}>
            {(branches.data ?? []).map((b) => (
              <option key={b.id} value={b.id}>{b.name}</option>
            ))}
          </Select>
          <Select label={t('calls.columns.type')} value={draft.typeId} onChange={set('typeId')}>
            {(types.data ?? []).map((ty) => (
              <option key={ty.id} value={ty.id}>{arabic ? ty.labelAr : ty.labelEn}</option>
            ))}
          </Select>
        </div>
      </form>

      <ReportCard
        title={t('applicationReports.sections.byChannel.title')}
        hint={t('applicationReports.sections.byChannel.hint')}
        columns={channelColumns}
        rows={byChannel.data}
        loading={byChannel.isFetching}
        error={byChannel.isError}
        exportName="messages-by-channel"
        chart={{
          kind: 'bar',
          x: (r) => r.channel,
          series: [
            { key: 'messages', label: c('messages'), colour: SERIES_COLOURS[0] },
            { key: 'unclassified', label: c('unclassified'), colour: SERIES_COLOURS[1] },
          ],
        }}
      />

      <ReportCard
        title={t('applicationReports.sections.orders.title')}
        hint={t('applicationReports.sections.orders.hint')}
        columns={ordersColumns}
        rows={orders.data}
        loading={orders.isFetching}
        error={orders.isError}
        exportName={`orders-by-${ordersBy}`}
        chart={{
          kind: ordersBy === 'day' ? 'line' : 'bar',
          x: (r) => r.label,
          series: [{ key: 'orderValue', label: c('orderValue'), colour: SERIES_COLOURS[0] }],
        }}
      >
        <Grouping
          label={t('applicationReports.groupBy')}
          value={ordersBy}
          options={ORDERS_GROUPINGS}
          onChange={setOrdersBy}
        />
      </ReportCard>

      <ReportCard
        title={t('applicationReports.sections.trend.title')}
        hint={t('applicationReports.sections.trend.hint')}
        columns={trendColumns}
        rows={trend.data}
        loading={trend.isFetching}
        error={trend.isError}
        exportName={`messages-by-${trendBy}`}
        chart={{
          kind: 'line',
          x: (r) => bucketLabel(r.bucket, trendBy),
          series: [
            { key: 'messages', label: c('messages'), colour: SERIES_COLOURS[0] },
            { key: 'orders', label: c('orders'), colour: SERIES_COLOURS[1] },
          ],
        }}
      >
        <Grouping
          label={t('applicationReports.groupBy')}
          value={trendBy}
          options={TREND_GROUPINGS}
          onChange={setTrendBy}
        />
      </ReportCard>

      <ReportCard
        title={t('applicationReports.sections.byAgent.title')}
        hint={t('applicationReports.sections.byAgent.hint')}
        columns={agentColumns}
        rows={byAgent.data}
        loading={byAgent.isFetching}
        error={byAgent.isError}
        exportName="messages-by-agent"
        chart={{
          kind: 'bar',
          x: (r) => r.agent,
          series: [
            { key: 'messages', label: c('messages'), colour: SERIES_COLOURS[0] },
            { key: 'orders', label: c('orders'), colour: SERIES_COLOURS[1] },
            { key: 'unclassified', label: c('unclassified'), colour: SERIES_COLOURS[2] },
          ],
        }}
      />

      <ReportCard
        title={t('applicationReports.sections.issues.title')}
        hint={t('applicationReports.sections.issues.hint')}
        columns={issuesColumns}
        rows={issues.data}
        loading={issues.isFetching}
        error={issues.isError}
        exportName="issues-by-channel"
        chart={{
          kind: 'bar',
          x: (r) => r.channel,
          series: [
            { key: 'cancellations', label: c('cancellations'), colour: SERIES_COLOURS[1] },
            { key: 'complaints', label: c('complaints'), colour: SERIES_COLOURS[2] },
          ],
        }}
      />
    </div>
  )
}

/** The bucket as the server writes it, made readable: an hour as "09:00", a week by its Monday. */
function bucketLabel(bucket: string, grouping: TrendGrouping): string {
  return grouping === 'hour' ? `${bucket}:00` : bucket
}

function Grouping<T extends string>({
  label, value, options, onChange,
}: { label: string; value: T; options: T[]; onChange: (value: T) => void }) {
  const { t } = useTranslation()
  return (
    <label className="flex items-center gap-2 text-sm text-slate-400">
      {label}
      <select className="input w-auto py-1" value={value} onChange={(e) => onChange(e.target.value as T)}>
        {options.map((option) => (
          <option key={option} value={option}>{t(`applicationReports.groups.${option}`)}</option>
        ))}
      </select>
    </label>
  )
}
