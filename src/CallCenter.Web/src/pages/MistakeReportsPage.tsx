import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { listBranches } from '../api/delivery'
import { mistakesByAgent, mistakesByBranch, mistakesTrend, repeatCustomers } from '../api/mistakes'
import type {
  MistakeAgentRow,
  MistakeBranchRow,
  MistakeCustomerRow,
  MistakeReportFilters,
  MistakeTrendGrouping,
  MistakeTrendPoint,
} from '../api/mistakes'
import { listUsers } from '../api/users'
import ReportCard, { SERIES_COLOURS } from '../components/ReportCard'
import type { ReportColumn } from '../components/ReportCard'
import { Grouping, PrintPageButton, ReportPrintHeading } from '../components/ReportFilters'
import { FilterSelect as Select } from '../components/SearchControls'
import { formatMoney } from '../lib/money'
import { printPage } from '../lib/print'
import { useReportFilters } from '../lib/reportFilters'
import type { Preset, ReportDraft } from '../lib/reportFilters'

const TREND_GROUPINGS: MistakeTrendGrouping[] = ['day', 'week', 'month']
const PRESETS: Preset[] = ['today', 'week', 'month', 'custom']

/** The most customers drawn; the export holds them all. */
const CUSTOMER_LIMIT = 50

/** Days, not instants: a mistake is kept by its day. */
function toFilters(d: ReportDraft): MistakeReportFilters {
  const id = (v: string) => (v ? v : undefined)
  return { from: id(d.from), to: id(d.to), branchId: id(d.branchId), agentId: id(d.agentId) }
}

/**
 * The mistakes report (R-23): per branch, per agent, over time, and the
 * customers it happened to more than once.
 *
 * One filter row scopes the four cards, as on the other report pages, and opens
 * on today as they do. It is its own row rather than the shared bar because a
 * mistake has a branch and an agent but no channel or call type to filter by.
 * Each card is a table, a chart where the figures are counts, an Export CSV
 * and a Print, from `ReportCard`, like every other report.
 */
export default function MistakeReportsPage() {
  const { t, i18n } = useTranslation()
  const { draft, set, choosePreset } = useReportFilters()
  const filters = toFilters(draft)
  const [trendBy, setTrendBy] = useState<MistakeTrendGrouping>('day')

  const byBranch = useQuery({ queryKey: ['reports', 'mistakes', 'by-branch', filters], queryFn: () => mistakesByBranch(filters) })
  const byAgent = useQuery({ queryKey: ['reports', 'mistakes', 'by-agent', filters], queryFn: () => mistakesByAgent(filters) })
  const trend = useQuery({
    queryKey: ['reports', 'mistakes', 'trend', filters, trendBy],
    queryFn: () => mistakesTrend(filters, trendBy),
  })
  const customers = useQuery({
    queryKey: ['reports', 'mistakes', 'repeat-customers', filters],
    queryFn: () => repeatCustomers(filters),
  })

  const c = (key: string) => t(`mistakeReports.columns.${key}`)
  const money = (v: number) => formatMoney(v, i18n.language)
  const day = (v: string) => {
    const [y, m, d] = v.split('-').map(Number)
    return new Date(y, m - 1, d).toLocaleDateString(i18n.language)
  }

  const branchColumns: ReportColumn<MistakeBranchRow>[] = [
    { key: 'branch', label: c('branch'), value: (r) => r.branch },
    { key: 'mistakes', label: c('mistakes'), value: (r) => r.mistakes, numeric: true, total: true },
    { key: 'branchOwn', label: c('branchOwn'), value: (r) => r.branchOwn, numeric: true, total: true },
    { key: 'byAgents', label: c('byAgents'), value: (r) => r.byAgents, numeric: true, total: true },
    { key: 'value', label: c('value'), value: (r) => r.value, format: (r) => money(r.value), numeric: true, total: true },
  ]

  const agentColumns: ReportColumn<MistakeAgentRow>[] = [
    { key: 'agent', label: c('agent'), value: (r) => r.agent },
    { key: 'mistakes', label: c('mistakes'), value: (r) => r.mistakes, numeric: true, total: true },
    { key: 'value', label: c('value'), value: (r) => r.value, format: (r) => money(r.value), numeric: true, total: true },
  ]

  const trendColumns: ReportColumn<MistakeTrendPoint>[] = [
    { key: 'bucket', label: c('period'), value: (r) => r.bucket },
    { key: 'mistakes', label: c('mistakes'), value: (r) => r.mistakes, numeric: true, total: true },
    { key: 'branchOwn', label: c('branchOwn'), value: (r) => r.branchOwn, numeric: true, total: true },
    { key: 'byAgents', label: c('byAgents'), value: (r) => r.byAgents, numeric: true, total: true },
    { key: 'value', label: c('value'), value: (r) => r.value, format: (r) => money(r.value), numeric: true, total: true },
  ]

  const customerColumns: ReportColumn<MistakeCustomerRow>[] = [
    {
      key: 'customer', label: c('customer'),
      value: (r) => (r.contactId ? (r.customer ?? '') : t('mistakeReports.notOnFile')),
    },
    { key: 'number', label: c('number'), value: (r) => r.number, ltr: true },
    { key: 'mistakes', label: c('mistakes'), value: (r) => r.mistakes, numeric: true },
    { key: 'value', label: c('value'), value: (r) => r.value, format: (r) => money(r.value), numeric: true },
    { key: 'last', label: c('last'), value: (r) => r.last, format: (r) => day(r.last) },
  ]

  return (
    <div className="space-y-6">
      <ReportPrintHeading title={t('mistakeReports.heading')} draft={draft} />
      <div className="no-print flex items-start justify-between gap-4">
        <div>
          <h1 className="page-title">{t('mistakeReports.heading')}</h1>
          <p className="page-subtitle">{t('mistakeReports.intro')}</p>
        </div>
        <PrintPageButton onPrint={printPage} />
      </div>

      <FilterBar draft={draft} set={set} choosePreset={choosePreset} />

      <ReportCard
        title={t('mistakeReports.sections.byBranch.title')}
        hint={t('mistakeReports.sections.byBranch.hint')}
        columns={branchColumns}
        rows={byBranch.data}
        loading={byBranch.isFetching}
        error={byBranch.isError}
        exportName="mistakes-by-branch"
        chart={{
          kind: 'bar',
          x: (r) => r.branch,
          series: [
            { key: 'branchOwn', label: c('branchOwn'), colour: SERIES_COLOURS[0] },
            { key: 'byAgents', label: c('byAgents'), colour: SERIES_COLOURS[1] },
          ],
        }}
      />

      <ReportCard
        title={t('mistakeReports.sections.byAgent.title')}
        hint={t('mistakeReports.sections.byAgent.hint')}
        columns={agentColumns}
        rows={byAgent.data}
        loading={byAgent.isFetching}
        error={byAgent.isError}
        exportName="mistakes-by-agent"
        chart={{
          kind: 'bar',
          x: (r) => r.agent,
          series: [{ key: 'mistakes', label: c('mistakes'), colour: SERIES_COLOURS[0] }],
        }}
      />

      <ReportCard
        title={t('mistakeReports.sections.trend.title')}
        hint={t('mistakeReports.sections.trend.hint')}
        columns={trendColumns}
        rows={trend.data}
        loading={trend.isFetching}
        error={trend.isError}
        exportName={`mistakes-by-${trendBy}`}
        chart={{
          kind: 'line',
          x: (r) => r.bucket,
          series: [
            { key: 'branchOwn', label: c('branchOwn'), colour: SERIES_COLOURS[0] },
            { key: 'byAgents', label: c('byAgents'), colour: SERIES_COLOURS[1] },
          ],
        }}
      >
        <Grouping label={t('mistakeReports.groupBy')} value={trendBy} options={TREND_GROUPINGS} onChange={setTrendBy} />
      </ReportCard>

      <ReportCard
        title={t('mistakeReports.sections.customers.title')}
        hint={t('mistakeReports.sections.customers.hint')}
        columns={customerColumns}
        rows={customers.data}
        loading={customers.isFetching}
        error={customers.isError}
        exportName="mistakes-repeat-customers"
        limit={CUSTOMER_LIMIT}
      />
    </div>
  )
}

/** The period, a branch and an agent: what a mistake can be narrowed by. */
function FilterBar({
  draft, set, choosePreset,
}: {
  draft: ReportDraft
  set: <K extends keyof ReportDraft>(key: K) => (value: ReportDraft[K]) => void
  choosePreset: (preset: Preset) => void
}) {
  const { t } = useTranslation()
  const users = useQuery({ queryKey: ['users'], queryFn: listUsers })
  const branches = useQuery({ queryKey: ['branches'], queryFn: listBranches })

  return (
    <form className="no-print card card-body space-y-4" aria-label={t('mistakeReports.filters')} onSubmit={(e) => e.preventDefault()}>
      <div className="flex flex-wrap items-center gap-2" role="group" aria-label={t('applicationReports.period')}>
        <span className="field-label me-2">{t('applicationReports.period')}</span>
        {PRESETS.map((preset) => (
          <button key={preset} type="button" aria-pressed={draft.preset === preset}
            className={draft.preset === preset ? 'btn-primary btn-sm' : 'btn-ghost btn-sm'}
            onClick={() => choosePreset(preset)}>
            {t(`applicationReports.presets.${preset}`)}
          </button>
        ))}
      </div>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
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
        <Select label={t('mistakeReports.columns.branch')} value={draft.branchId} onChange={set('branchId')} choices={branches}>
          {(branches.data ?? []).map((b) => (
            <option key={b.id} value={b.id}>{b.name}</option>
          ))}
        </Select>
        <Select label={t('mistakeReports.columns.agent')} value={draft.agentId} onChange={set('agentId')} choices={users}>
          {(users.data ?? []).filter((u) => u.role === 'Agent').map((u) => (
            <option key={u.id} value={u.id}>{u.displayName}</option>
          ))}
        </Select>
      </div>
    </form>
  )
}
