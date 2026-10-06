import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { dashboardPeriod, dashboardToday } from '../api/callReports'
import type { Count } from '../api/callReports'
import type { CallDirection, TypeCount } from '../api/applicationReports'
import { ReportChart, SERIES_COLOURS } from '../components/ReportCard'
import type { ReportChartSpec } from '../components/ReportCard'
import { DirectionSwitch, PrintPageButton, ReportFilterBar, ReportPrintHeading } from '../components/ReportFilters'
import LoadError from '../components/LoadError'
import QueueSwitchCard from '../components/QueueSwitchCard'
import { formatMoney } from '../lib/money'
import { printPage } from '../lib/print'
import { useReportFilters } from '../lib/reportFilters'

/** Today's figures come back every minute, so the page left open on a screen stays current. */
const TODAY_REFRESH_MS = 60_000

/**
 * The supervisor's home page (S-20): the queue switch (S-60), today's
 * figures, then four charts for a chosen period — communications per day, per type, per channel, per hour.
 *
 * Calls and messages together, because the dashboard is the whole picture;
 * abandoned and unclassified are calls' own words and say so. **Today** is the
 * restaurant's, worked out by the server, whatever period the charts show.
 *
 * Missed and rejected rings are shown, not counted (Dia, 26 Sep): the queue
 * passes a call from agent to agent, so a ring one agent missed is usually a
 * call another answered. The server leaves those rings out of every total
 * and chart here, and a call nobody answered is an abandoned one; but how
 * often a ringing phone went untaken is how efficient the agents are, so the
 * rings have a tile of their own.
 *
 * The figures are stat tiles, not charts: each is one number, and a chart of
 * one number is a worse way to read it (dataviz). The communications count
 * leads, as the one hero figure.
 *
 * Calls in and calls out have a block each, split by result (Dia, 2 Oct):
 * the PBX's call report counts incoming calls only, so the incoming block is
 * the figure to set beside it, and the outgoing one is what makes up the rest.
 *
 * **Incoming or outgoing, never both added together** (Dia, 6 Oct 2026): the
 * switch at the top decides which calls every figure and chart counts, today
 * and the period alike; the applications are counted either way. The
 * complaints count each complaint once, its call back included, whichever
 * way is chosen.
 */
export default function DashboardPage() {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const { draft, filters, set, choosePreset } = useReportFilters()
  const [direction, setDirection] = useState<CallDirection>('In')
  const incoming = direction === 'In'

  const today = useQuery({
    queryKey: ['dashboard', 'today', direction],
    queryFn: () => dashboardToday(direction),
    refetchInterval: TODAY_REFRESH_MS,
  })
  const period = useQuery({
    queryKey: ['dashboard', 'period', filters.from, filters.to, direction],
    queryFn: () => dashboardPeriod(filters.from, filters.to, direction),
  })

  const n = (v: number) => v.toLocaleString(i18n.language)
  const money = (v: number) => formatMoney(v, i18n.language)
  const typeLabel = (ty: TypeCount) => (arabic ? ty.labelAr : ty.labelEn)
  const d = today.data

  return (
    <div className="space-y-6">
      <ReportPrintHeading title={t('dashboard.heading')} draft={draft} periodOnly
        extra={[`${t('callReports.direction.label')}: ${t(`callReports.direction.${direction}`)}`]} />
      <div className="no-print flex items-start justify-between gap-4">
        <div>
          <h1 className="page-title">{t('dashboard.heading')}</h1>
          <p className="page-subtitle">{t('dashboard.intro')}</p>
        </div>
        <PrintPageButton onPrint={printPage} />
      </div>

      {/* S-60: open or close the call queue. Not printed with the figures. */}
      <div className="no-print">
        <QueueSwitchCard />
      </div>

      <div className="no-print card card-body">
        <DirectionSwitch value={direction} onChange={setDirection} />
      </div>

      <section className="space-y-3" aria-label={t('dashboard.today')}>
        <h3 className="font-semibold text-slate-100">{t('dashboard.today')}</h3>
        {/* Said when a refresh fails too: the figures below are then the last
            ones that arrived, not today's as they stand. */}
        {today.isError && (
          <LoadError message={t('dashboard.failed')} onRetry={() => void today.refetch()} busy={today.isFetching} />
        )}
        {today.isError && !d ? null : (
          <>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <Tile hero label={t('dashboard.tiles.communications')} value={d ? n(d.communications) : undefined}
                note={d ? t(incoming ? 'dashboard.tiles.splitIn' : 'dashboard.tiles.splitOut', { calls: n(d.calls), messages: n(d.messages) }) : undefined} />
              <Tile label={t('dashboard.tiles.orders')} value={d ? n(d.orders) : undefined}
                note={d ? t('dashboard.tiles.worth', { value: money(d.orderValue) }) : undefined} />
              <Tile label={t('dashboard.tiles.complaints')} value={d ? n(d.complaints) : undefined} />
              {/* Rings are incoming by nature. */}
              {incoming && (
                <Tile label={t('dashboard.tiles.untaken')} value={d ? n(d.missedRings + d.rejectedRings) : undefined}
                  note={d ? t('dashboard.tiles.untakenNote', { missed: n(d.missedRings), rejected: n(d.rejectedRings) }) : undefined} />
              )}
              <Tile label={t('dashboard.tiles.unclassified')} value={d ? n(d.unclassified) : undefined} note={t('dashboard.tiles.unclassifiedNote')} />
              <Tile label={t('dashboard.tiles.agentsOnline')} value={d ? n(d.agentsOnline) : undefined}
                note={t(d?.fromPbx ? 'dashboard.tiles.agentsOnlinePbxNote' : 'dashboard.tiles.agentsOnlineNote')} />
              {/* S-61: only the PBX knows who is on a call. */}
              {d?.fromPbx && (
                <Tile label={t('dashboard.tiles.agentsInCall')} value={n(d.agentsInCall)} note={t('dashboard.tiles.agentsInCallNote')} />
              )}
            </div>

            <div className="grid gap-3 md:grid-cols-2">
              {incoming ? (
                <CallBlock label={t('dashboard.calls.incoming')} note={t('dashboard.calls.incomingNote')}
                  total={d?.incoming} lines={d ? [
                    { label: t('dashboard.calls.answered'), count: d.incomingAnswered },
                    { label: t('dashboard.calls.abandonedLine'), count: d.abandoned },
                    { label: t('dashboard.calls.otherIncoming'), count: d.incoming - d.incomingAnswered - d.abandoned, onlyIfAny: true },
                  ] : []} />
              ) : (
                <CallBlock label={t('dashboard.calls.outgoing')} note={t('dashboard.calls.outgoingNote')}
                  total={d?.outgoing} lines={d ? [
                    { label: t('dashboard.calls.answered'), count: d.outgoingAnswered },
                    { label: t('dashboard.calls.notAnswered'), count: d.outgoingNotAnswered },
                    { label: t('dashboard.calls.otherOutgoing'), count: d.outgoing - d.outgoingAnswered - d.outgoingNotAnswered, onlyIfAny: true },
                  ] : []} />
              )}
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
          <LoadError message={t('dashboard.failed')} onRetry={() => void period.refetch()} busy={period.isFetching} />
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

/**
 * Calls in one direction: the total, and under it how many ended each way.
 * A line marked `onlyIfAny` is the remainder (blocked, still ringing), shown
 * only when there is one, so the lines always add up to the total.
 */
function CallBlock({ label, note, total, lines }: {
  label: string
  note: string
  total?: number
  lines: { label: string; count: number; onlyIfAny?: boolean }[]
}) {
  const { i18n } = useTranslation()
  const n = (v: number) => v.toLocaleString(i18n.language)
  return (
    <div className="card card-body" role="group" aria-label={label}>
      <p className="text-sm text-slate-400">{label}</p>
      {total === undefined ? (
        <div className="mt-2 h-8 w-16 animate-pulse rounded bg-ink-800/60" aria-hidden="true" />
      ) : (
        <>
          <p className="mt-1 text-3xl font-semibold text-slate-100">{n(total)}</p>
          <table className="table mt-2">
            <tbody>
              {lines.filter((l) => !l.onlyIfAny || l.count > 0).map((l) => (
                <tr key={l.label}>
                  <td>{l.label}</td>
                  <td className="tabular text-end"><span dir="ltr">{n(l.count)}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
      <p className="mt-2 text-xs text-slate-500">{note}</p>
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
                <td className="tabular text-end"><span dir="ltr">{r.count.toLocaleString(i18n.language)}</span></td>
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
