import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { breakMonitor, breakReport, exportBreaks, listBreaks } from '../api/breaks'
import type { BreakAgentTotal, BreakDay, BreakFilters, BreakMonitor, BreakMonitorRow, BreakState } from '../api/breaks'
import LoadError from '../components/LoadError'
import ReportCard, { SERIES_COLOURS } from '../components/ReportCard'
import type { ReportColumn } from '../components/ReportCard'
import { ReportFilterBar } from '../components/ReportFilters'
import { Pager } from '../components/SearchControls'
import { elapsed } from '../lib/agentPhones'
import { useClock } from '../lib/clock'
import { downloadBlob } from '../lib/csv'
import { localDate, useReportFilters } from '../lib/reportFilters'

/** How often the monitor asks the server. The times going count on every second in between. */
export const MONITOR_REFRESH_MS = 10_000

const PAGE_SIZE = 50

/** On break first, then the ones working, then the rest; by name within each. */
const STATE_ORDER: Record<BreakState, number> = { OnBreak: 0, Working: 1, NotHeard: 2, SignedOut: 3 }

const STATE_BADGE: Record<BreakState, string> = {
  OnBreak: 'badge-warn',
  Working: 'badge-ok',
  NotHeard: 'badge-muted',
  SignedOut: 'badge-muted',
}

/** h:mm:ss, or m:ss under an hour: a length of time, the same in both languages. */
const length = (seconds: number) => elapsed(0, Math.max(0, seconds) * 1000)

/** Minutes to one decimal, for the CSV and the chart: what a spreadsheet adds up. */
const minutes = (seconds: number) => Math.round(seconds / 6) / 10

/** A day the server sent (yyyy-mm-dd) in the reader's language, without a time zone moving it. */
function formatDay(day: string, language: string): string {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(y, m - 1, d).toLocaleDateString(language)
}

/**
 * The agents' breaks (A-86): who is on break now and for how long (S-66), and
 * the break report for any period (R-22).
 *
 * **The day's total adds up**: an agent's second break counts on from where the
 * first stopped, as the Agent App's own timer does. **The daily allowance warns,
 * it never stops a break** (Dia, 1 Oct 2026): past it the time is in red, with
 * by how much. The allowance is `breaks.daily_limit_minutes` in Settings.
 *
 * The monitor asks the server every ten seconds and counts the breaks going on
 * every second in between, from the server's own clock, so a browser whose
 * clock is wrong still shows the right minutes. The report and the list are
 * worked out and paged by the server (20 Sep, "filtering a page lies").
 */
export default function BreaksPage() {
  const { t } = useTranslation()

  return (
    <div className="space-y-6">
      <div>
        <h1 className="page-title">{t('breaks.heading')}</h1>
        <p className="page-subtitle">{t('breaks.intro')}</p>
      </div>

      <MonitorCard />
      <BreakReports />
    </div>
  )
}

// ---- now (S-66) --------------------------------------------------------------

function MonitorCard() {
  const { t } = useTranslation()
  const monitor = useQuery({ queryKey: ['breaks', 'monitor'], queryFn: breakMonitor, refetchInterval: MONITOR_REFRESH_MS })

  // When the answer arrived, by this browser's clock: the server's "as of" is
  // that moment, whatever either clock says.
  const receivedAt = monitor.dataUpdatedAt
  const anyGoing = (monitor.data?.agents ?? []).some((a) => a.state === 'OnBreak')
  const now = useClock(anyGoing)

  const rows = [...(monitor.data?.agents ?? [])].sort((a, b) =>
    STATE_ORDER[a.state] - STATE_ORDER[b.state] || a.agentDisplayName.localeCompare(b.agentDisplayName))

  const onBreak = rows.filter((r) => r.state === 'OnBreak').length

  return (
    <section className="card" aria-label={t('breaks.now.title')}>
      <div className="card-header">
        <div>
          <h2 className="font-semibold text-slate-100">{t('breaks.now.title')}</h2>
          <p className="text-sm text-slate-400">
            {monitor.data
              ? t('breaks.now.hint', { count: onBreak, minutes: monitor.data.dailyLimitMinutes })
              : t('breaks.now.hintLoading')}
          </p>
        </div>
      </div>

      <div className="card-body">
        {monitor.isLoading ? (
          <p className="text-slate-400">{t('app.loading')}</p>
        ) : monitor.isError && !monitor.data ? (
          <LoadError message={t('breaks.now.failed')} onRetry={() => void monitor.refetch()} busy={monitor.isFetching} />
        ) : rows.length === 0 ? (
          <p className="text-center text-slate-400">{t('breaks.now.empty')}</p>
        ) : (
          <>
            {monitor.isError && <p role="status" className="notice-warning mb-3">{t('breaks.now.stale')}</p>}
            <div className="overflow-x-auto">
              <table className="table">
                <thead>
                  <tr>
                    <th>{t('breaks.columns.agent')}</th>
                    <th>{t('breaks.columns.state')}</th>
                    <th>{t('breaks.columns.thisBreak')}</th>
                    <th className="!text-center">{t('breaks.columns.today')}</th>
                    <th className="!text-center">{t('breaks.columns.breaks')}</th>
                    <th className="!text-center">{t('breaks.columns.over')}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <MonitorRow key={row.agentId} row={row} monitor={monitor.data!} since={now - receivedAt} />
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </section>
  )
}

/**
 * One agent now. `since` is how long ago, in milliseconds, the server worked
 * the figures out; a break going has gone on that much longer.
 */
function MonitorRow({ row, monitor, since }: { row: BreakMonitorRow; monitor: BreakMonitor; since: number }) {
  const { t, i18n } = useTranslation()
  const going = row.state === 'OnBreak' && row.breakStartedAt !== null
  const extra = going ? Math.max(0, Math.floor(since / 1000)) : 0

  const asOf = Date.parse(monitor.asOf)
  const today = row.todaySeconds + extra
  const over = Math.max(0, today - monitor.dailyLimitMinutes * 60)

  return (
    <tr>
      <td className="font-medium text-slate-200">{row.agentDisplayName}</td>
      <td>
        <span className={STATE_BADGE[row.state]}>{t(`breaks.states.${row.state}`)}</span>
      </td>
      <td>
        {going && (
          <span className="tabular" dir="ltr">
            {length(Math.floor((asOf - Date.parse(row.breakStartedAt!)) / 1000) + extra)}
            <span className="ms-2 text-xs text-slate-500">
              {t('breaks.since', {
                time: new Date(row.breakStartedAt!).toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' }),
              })}
            </span>
          </span>
        )}
      </td>
      <td className={`tabular text-center ${over > 0 ? 'font-semibold text-red-300' : ''}`} dir="ltr">
        {length(today)}
      </td>
      <td className="tabular text-center">{row.todayBreaks}</td>
      <td className="tabular text-center text-red-300" dir="ltr">{over > 0 ? length(over) : ''}</td>
    </tr>
  )
}

// ---- the report (R-22) -----------------------------------------------------

function BreakReports() {
  const { t, i18n } = useTranslation()
  const { draft, set, choosePreset } = useReportFilters()

  const filters: BreakFilters = {
    from: draft.from || undefined,
    to: draft.to || undefined,
    agentId: draft.agentId.length > 0 ? draft.agentId : undefined,
  }

  const report = useQuery({
    queryKey: ['breaks', 'report', filters],
    queryFn: () => breakReport(filters),
    placeholderData: keepPreviousData,
  })

  const c = (key: string) => t(`breaks.columns.${key}`)
  const limit = report.data?.dailyLimitMinutes

  const agentColumns: ReportColumn<BreakAgentTotal & { minutes: number }>[] = [
    { key: 'agent', label: c('agent'), value: (r) => r.agentDisplayName },
    { key: 'breaks', label: c('breaks'), value: (r) => r.breaks, numeric: true, total: true },
    // Minutes in every cell, so a cell, the total under it and the CSV agree.
    { key: 'minutes', label: c('minutes'), value: (r) => r.minutes, numeric: true, total: true },
    { key: 'days', label: c('days'), value: (r) => r.days, numeric: true },
    { key: 'daysOver', label: c('daysOver'), value: (r) => r.daysOver, numeric: true },
    { key: 'over', label: c('overMinutes'), value: (r) => minutes(r.overSeconds), numeric: true, total: true },
  ]

  const dayColumns: ReportColumn<BreakDay>[] = [
    { key: 'day', label: c('day'), value: (r) => r.day, format: (r) => formatDay(r.day, i18n.language) },
    { key: 'agent', label: c('agent'), value: (r) => r.agentDisplayName },
    { key: 'breaks', label: c('breaks'), value: (r) => r.breaks, numeric: true },
    { key: 'minutes', label: c('minutes'), value: (r) => minutes(r.seconds), numeric: true },
    { key: 'over', label: c('overMinutes'), value: (r) => minutes(r.overSeconds), numeric: true },
  ]

  const period = `${draft.from}-${draft.to}`

  return (
    <>
      <ReportFilterBar draft={draft} set={set} choosePreset={choosePreset} agentOnly />

      <ReportCard
        title={t('breaks.perAgent.title')}
        hint={limit !== undefined ? t('breaks.perAgent.hint', { minutes: limit }) : undefined}
        columns={agentColumns}
        rows={report.data?.agents.map((r) => ({ ...r, minutes: minutes(r.seconds) }))}
        loading={report.isFetching}
        error={report.isError}
        chart={{
          kind: 'bar',
          x: (r) => r.agentDisplayName,
          series: [{ key: 'minutes', label: c('minutes'), colour: SERIES_COLOURS[0] }],
        }}
        exportName={`breaks-by-agent-${period}`}
      />

      <ReportCard
        title={t('breaks.perDay.title')}
        hint={t('breaks.perDay.hint')}
        columns={dayColumns}
        rows={report.data?.days}
        loading={report.isFetching}
        error={report.isError}
        exportName={`breaks-by-day-${period}`}
      />

      <BreakList filters={filters} />
    </>
  )
}

/** Every single break of the period, newest first, a page at a time; the export holds them all. */
function BreakList({ filters }: { filters: BreakFilters }) {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const [page, setPage] = useState(1)
  const [exporting, setExporting] = useState<'idle' | 'busy' | 'failed'>('idle')

  // A new period starts again at the first page.
  const key = JSON.stringify(filters)
  const [lastKey, setLastKey] = useState(key)
  if (key !== lastKey) {
    setLastKey(key)
    setPage(1)
  }

  const list = useQuery({
    queryKey: ['breaks', 'list', filters, page],
    queryFn: () => listBreaks(filters, page, PAGE_SIZE),
    placeholderData: keepPreviousData,
  })

  async function onExport() {
    setExporting('busy')
    try {
      downloadBlob(`breaks-${filters.from ?? localDate(new Date())}-${filters.to ?? localDate(new Date())}`,
        await exportBreaks(filters, arabic ? 'ar' : 'en'))
      setExporting('idle')
    } catch {
      setExporting('failed')
    }
  }

  const total = list.data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const time = (iso: string) => new Date(iso).toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' })
  const day = (iso: string) => new Date(iso).toLocaleDateString(i18n.language)

  return (
    <section className="card" aria-label={t('breaks.list.title')}>
      <div className="card-header">
        <div>
          <h3 className="font-semibold text-slate-100">{t('breaks.list.title')}</h3>
          <p className="text-sm text-slate-400">{t('breaks.list.hint')}</p>
        </div>
        <div className="flex flex-wrap items-center gap-3 text-sm text-slate-400">
          {total > 0 && <span>{t('breaks.list.count', { count: total })}</span>}
          {exporting === 'failed' && <span className="text-red-300">{t('breaks.list.exportFailed')}</span>}
          <button type="button" className="btn-ghost btn-sm" onClick={onExport} disabled={exporting === 'busy' || total === 0}>
            {exporting === 'busy' ? t('breaks.list.exporting') : t('breaks.list.export', { count: total })}
          </button>
          {pages > 1 && <Pager page={page} pages={pages} onPage={setPage} />}
        </div>
      </div>

      <div className="card-body">
        {list.isLoading ? (
          <p className="text-slate-400">{t('app.loading')}</p>
        ) : list.isError ? (
          <LoadError message={t('breaks.list.failed')} onRetry={() => void list.refetch()} busy={list.isFetching} />
        ) : total === 0 ? (
          <p className="text-center text-slate-400">{t('breaks.list.empty')}</p>
        ) : (
          <div className={`overflow-x-auto transition ${list.isPlaceholderData ? 'opacity-60' : ''}`}>
            <table className="table">
              <thead>
                <tr>
                  <th>{t('breaks.columns.agent')}</th>
                  <th>{t('breaks.columns.day')}</th>
                  <th>{t('breaks.columns.in')}</th>
                  <th>{t('breaks.columns.out')}</th>
                  <th className="!text-center">{t('breaks.columns.length')}</th>
                  <th>{t('breaks.columns.endedBy')}</th>
                </tr>
              </thead>
              <tbody>
                {list.data!.rows.map((b) => (
                  <tr key={b.id}>
                    <td className="text-slate-200">{b.agentDisplayName}</td>
                    <td className="whitespace-nowrap">{day(b.startedAt)}</td>
                    <td className="tabular" dir="ltr">{time(b.startedAt)}</td>
                    <td className="tabular" dir="ltr">{b.endedAt ? time(b.endedAt) : ''}</td>
                    <td className="tabular text-center" dir="ltr">{length(b.seconds)}</td>
                    <td>
                      {b.endedBy === null ? (
                        <span className="badge-warn">{t('breaks.endedBy.going')}</span>
                      ) : (
                        <span className={b.endedBy === 'BreakOut' ? 'text-slate-300' : 'text-slate-500'}
                          title={t(`breaks.endedByHint.${b.endedBy}`)}>
                          {t(`breaks.endedBy.${b.endedBy}`)}
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  )
}
