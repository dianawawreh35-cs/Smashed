import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { dashboardPeriod, dashboardToday } from '../api/callReports'
import type { Count } from '../api/callReports'
import type { TypeCount } from '../api/applicationReports'
import { ReportChart, SERIES_COLOURS } from '../components/ReportCard'
import type { ReportChartSpec } from '../components/ReportCard'
import { ReportFilterBar } from '../components/ReportFilters'
import { useReportFilters } from '../lib/reportFilters'

/** Today's figures come back every minute, so the page left open on a screen stays current. */
const TODAY_REFRESH_MS = 60_000

/**
 * The supervisor's home page (S-20): today's figures, then four charts for a
 * chosen period — communications per day, per type, per channel, per hour.
 *
 * Calls and messages together, because the dashboard is the whole picture;
 * missed and unclassified are calls' own words and say so. **Today** is the
 * restaurant's, worked out by the server, whatever period the charts show.
 *
 * The figures are stat tiles, not charts: each is one number, and a chart of
 * one number is a worse way to read it (dataviz). The communications count
 * leads, as the one hero figure.
 */
export default function DashboardPage() {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const { draft, filters, set, choosePreset } = useReportFilters('week')

  const today = useQuery({ queryKey: ['dashboard', 'today'], queryFn: dashboardToday, refetchInterval: TODAY_REFRESH_MS })
  const period = useQuery({
    queryKey: ['dashboard', 'period', filters.from, filters.to],
    queryFn: () => dashboardPeriod(filters.from, filters.to),
  })

  const n = (v: number) => v.toLocaleString(i18n.language)
  const money = (v: number) => v.toLocaleString(i18n.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  const typeLabel = (ty: TypeCount) => (arabic ? ty.labelAr : ty.labelEn)
  const d = today.data

  return (
    <div className="space-y-6">
      <div>
        <h2 className="page-title">{t('dashboard.heading')}</h2>
        <p className="page-subtitle">{t('dashboard.intro')}</p>
      </div>

      <section className="space-y-3" aria-label={t('dashboard.today')}>
        <h3 className="font-semibold text-slate-100">{t('dashboard.today')}</h3>
        {today.isError ? (
          <p className="notice-error">{t('dashboard.failed')}</p>
        ) : (
          <>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <Tile hero label={t('dashboard.tiles.communications')} value={d ? n(d.communications) : undefined}
                note={d ? t('dashboard.tiles.split', { calls: n(d.calls), messages: n(d.messages) }) : undefined} />
              <Tile label={t('dashboard.tiles.orders')} value={d ? n(d.orders) : undefined}
                note={d ? t('dashboard.tiles.worth', { value: money(d.orderValue) }) : undefined} />
              <Tile label={t('dashboard.tiles.complaints')} value={d ? n(d.complaints) : undefined} />
              <Tile label={t('dashboard.tiles.missed')} value={d ? n(d.missed) : undefined} note={t('dashboard.tiles.missedNote')} />
              <Tile label={t('dashboard.tiles.unclassified')} value={d ? n(d.unclassified) : undefined} note={t('dashboard.tiles.unclassifiedNote')} />
              <Tile label={t('dashboard.tiles.agentsOnline')} value={d ? n(d.agentsOnline) : undefined} note={t('dashboard.tiles.agentsOnlineNote')} />
            </div>

            <div className="grid gap-3 md:grid-cols-2">
              <Breakdown title={t('dashboard.byType')}
                rows={(d?.byType ?? []).map((ty) => ({ key: ty.typeName, label: typeLabel(ty), count: ty.count }))} />
              <Breakdown title={t('dashboard.byChannel')} rows={d?.byChannel ?? []} />
            </div>
          </>
        )}
      </section>

      <section className="space-y-3" aria-label={t('dashboard.period')}>
        <h3 className="font-semibold text-slate-100">{t('dashboard.period')}</h3>
        <ReportFilterBar draft={draft} set={set} choosePreset={choosePreset} periodOnly />
        {period.isError ? (
          <p className="notice-error">{t('dashboard.failed')}</p>
        ) : (
          <div className="grid gap-4 lg:grid-cols-2">
            <ChartCard title={t('dashboard.charts.perDay')} imageName="dashboard-perday" rows={period.data?.perDay}
              spec={{ kind: (period.data?.perDay.length ?? 0) > 1 ? 'line' : 'bar', x: (r) => r.label, series: [series(t('dashboard.communications'))] }} />
            <ChartCard title={t('dashboard.charts.perType')} imageName="dashboard-pertype"
              rows={period.data?.perType.map((ty) => ({ key: ty.typeName, label: typeLabel(ty), count: ty.count }))}
              spec={{ kind: 'bar', x: (r) => r.label, series: [series(t('dashboard.communications'))] }} />
            <ChartCard title={t('dashboard.charts.perChannel')} imageName="dashboard-perchannel" rows={period.data?.perChannel}
              spec={{ kind: 'bar', x: (r) => r.label, series: [series(t('dashboard.communications'))] }} />
            <ChartCard title={t('dashboard.charts.perHour')} imageName="dashboard-perhour" rows={period.data?.perHour}
              spec={{ kind: 'bar', x: (r) => r.label, series: [series(t('dashboard.communications'))] }} />
          </div>
        )}
      </section>
    </div>
  )
}

/** One series, in the first slot: every dashboard chart counts the same thing. */
const series = (label: string) => ({ key: 'count' as const, label, colour: SERIES_COLOURS[0] })

/** A stat tile: a label, one number, and a line saying what it counts where that could be wondered. */
function Tile({ label, value, note, hero = false }: { label: string; value?: string; note?: string; hero?: boolean }) {
  return (
    <div className="card card-body" role="group" aria-label={label}>
      <p className="text-sm text-slate-400">{label}</p>
      {value === undefined ? (
        <div className="mt-2 h-8 w-16 animate-pulse rounded bg-ink-800/60" aria-hidden="true" />
      ) : (
        <p className={`${hero ? 'text-5xl' : 'text-3xl'} mt-1 font-semibold text-slate-100`}>{value}</p>
      )}
      {note && <p className="mt-1 text-xs text-slate-500">{note}</p>}
    </div>
  )
}

/** Today by type or by channel: a short list of names and counts, largest first. */
function Breakdown({ title, rows }: { title: string; rows: Count[] }) {
  const { t, i18n } = useTranslation()
  return (
    <section className="card card-body" aria-label={title}>
      <h4 className="mb-2 text-sm font-semibold text-slate-200">{title}</h4>
      {rows.length === 0 ? (
        <p className="text-sm text-slate-400">{t('dashboard.nothingToday')}</p>
      ) : (
        <table className="table">
          <tbody>
            {rows.map((r) => (
              <tr key={r.key}>
                <td>{r.label}</td>
                <td className="tabular text-end" dir="ltr">{r.count.toLocaleString(i18n.language)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

function ChartCard({ title, rows, spec, imageName }: {
  title: string
  rows: Count[] | undefined
  spec: ReportChartSpec<Count>
  imageName: string
}) {
  const { t } = useTranslation()
  const empty = rows !== undefined && rows.every((r) => r.count === 0)
  return (
    <section className="card card-body" aria-label={title}>
      <h4 className="mb-2 font-semibold text-slate-100">{title}</h4>
      {rows === undefined ? (
        <div className="h-60 animate-pulse rounded-md bg-ink-800/60" aria-hidden="true" />
      ) : empty ? (
        <p className="py-10 text-center text-slate-400">{t('applicationReports.empty')}</p>
      ) : (
        <ReportChart rows={rows} spec={spec} imageName={imageName} />
      )}
    </section>
  )
}
