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
  TrendGrouping,
  TrendPoint,
} from '../api/applicationReports'
import ReportCard, { SERIES_COLOURS } from '../components/ReportCard'
import type { ReportColumn } from '../components/ReportCard'
import { Grouping, ReportFilterBar } from '../components/ReportFilters'
import { useReportFilters } from '../lib/reportFilters'

const ORDERS_GROUPINGS: OrdersGrouping[] = ['channel', 'branch', 'agent', 'day']
const TREND_GROUPINGS: TrendGrouping[] = ['day', 'week', 'month', 'hour']

/**
 * The application reports (A-72, R-12, R-13): about messages, never calls.
 *
 * One filter row (S-07, `ReportFilterBar`, shared with the call reports) scopes all five reports, so a supervisor narrowing to
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
  const { draft, filters, set, choosePreset } = useReportFilters('week')
  const [ordersBy, setOrdersBy] = useState<OrdersGrouping>('channel')
  const [trendBy, setTrendBy] = useState<TrendGrouping>('day')

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
  ]

  const issuesColumns: ReportColumn<IssuesReportRow>[] = [
    { key: 'channel', label: c('channel'), value: (r) => r.channel },
    { key: 'messages', label: c('messages'), value: (r) => r.messages, numeric: true, total: true },
    { key: 'cancellations', label: c('cancellations'), value: (r) => r.cancellations, numeric: true, total: true },
    { key: 'complaints', label: c('complaints'), value: (r) => r.complaints, numeric: true, total: true },
  ]

  return (
    <div className="space-y-6">
      <div>
        <h2 className="page-title">{t('applicationReports.heading')}</h2>
        <p className="page-subtitle">{t('applicationReports.intro')}</p>
      </div>

      <ReportFilterBar draft={draft} set={set} choosePreset={choosePreset} />

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

