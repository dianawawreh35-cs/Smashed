import { Fragment, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { chartToPng } from '../lib/chartImage'
import { downloadBlob, downloadCsv, toCsv } from '../lib/csv'
import { printOnly } from '../lib/print'
import { useTheme, type Theme } from '../lib/theme'
import type { CsvCell } from '../lib/csv'
import LoadError from './LoadError'

/**
 * One report: a table, a chart where the figures are numeric (S-06), and an
 * Export CSV button (S-05). Written once here so the application reports and,
 * later, the call reports draw the same way.
 *
 * The table is the report; the chart is a second reading of the same rows, and
 * the CSV is those rows verbatim. Nothing is fetched for the export: the rows
 * on screen are the whole report, never a page of one.
 */

export interface ReportColumn<T> {
  key: string
  label: string
  /** The cell as data: what the CSV holds and what the table shows unless `format` says otherwise. */
  value: (row: T) => CsvCell
  /** For the screen only; the CSV always gets `value`. */
  format?: (row: T) => string
  numeric?: boolean
  /**
   * Text held left to right, such as a phone number (M-W01): in Arabic its
   * groups would otherwise come out reversed. A numeric column always is.
   */
  ltr?: boolean
  /** Summed in a last row. Only for a count or an amount, never an average. */
  total?: boolean
  /**
   * A heat table's cell (S-06, R-10): shaded by its value against the largest
   * in the heat columns, one hue, more is brighter.
   */
  heat?: boolean
}

/**
 * A heat cell's shade: the first series colour, the brand blue, from nearly
 * the card's own surface for "almost none" to strong for the most (dataviz:
 * sequential is one hue, light to dark — on a dark card, faint to bright).
 * Zero is left unshaded, so an empty hour reads as empty. The number is
 * always printed, in the strongest ink: the shade adds, it never carries the
 * value alone. Capped at 60% so that ink keeps 5.7:1 on the strongest shade
 * in dark (at 75% it fell to 3.9:1, under the 4.5 small text needs), and
 * 6.4:1 in light (S-69).
 */
function heatStyle(value: number, max: number, theme: Theme): React.CSSProperties | undefined {
  if (value <= 0 || max <= 0) return undefined
  const alpha = (0.1 + 0.5 * (value / max)).toFixed(3)
  return theme === 'light'
    ? { backgroundColor: `rgba(36, 98, 214, ${alpha})`, color: '#1B1F27' }
    : { backgroundColor: `rgba(79, 140, 255, ${alpha})`, color: '#f1f5f9' }
}

export interface ReportSeries<T> {
  key: keyof T & string
  label: string
  colour: string
}

export interface ReportChartSpec<T> {
  /** Bars for categories, a line for time (dataviz). */
  kind: 'bar' | 'line'
  /** The category or bucket under each bar or point. */
  x: (row: T) => string
  series: ReportSeries<T>[]
}

/**
 * The series colours, in a fixed order that never cycles: the brand blue
 * first, then the dataviz reference palette's dark-surface orange, aqua and
 * yellow. Validated together against the card surface (#171A21) with the
 * dataviz palette checker (26 Sep, with the yellow added for abandoned calls):
 * every adjacent pair clears the colour-blindness target (worst ΔE 8.4,
 * yellow against aqua) and 3:1 contrast. A fifth series would need
 * re-validating.
 */
export const SERIES_COLOURS = ['#4F8CFF', '#d95926', '#199e70', '#c98500'] as const

/**
 * The same four in light (S-69): the light brand blue, then the dataviz
 * palette's light-surface orange, aqua and yellow. Validated on the white card
 * with the same checker (4 Oct): worst adjacent pair ΔE 9.1 for colour
 * blindness, 22.9 for everyone. The aqua and the yellow are under 3:1 on
 * white, which the checker allows because every chart has a legend and its
 * table beside it.
 */
const LIGHT_SERIES_COLOURS = ['#2462D6', '#eb6834', '#1baf7a', '#eda100'] as const

/** A series' colour in the theme on screen: pages name the dark one. */
function seriesColour(colour: string, theme: Theme): string {
  const slot = (SERIES_COLOURS as readonly string[]).indexOf(colour)
  return theme === 'light' && slot >= 0 ? LIGHT_SERIES_COLOURS[slot] : colour
}

export default function ReportCard<T>({
  title,
  hint,
  columns,
  rows,
  loading,
  error,
  chart,
  exportName,
  limit,
  expand,
  children,
}: {
  title: string
  hint?: string
  columns: ReportColumn<T>[]
  rows: T[] | undefined
  loading: boolean
  error: boolean
  chart?: ReportChartSpec<T>
  /** The file name without .csv. */
  exportName: string
  /**
   * For a report that is a list (the complaints, the customers): how many rows
   * to draw. The export still holds every row, and the card says so, because
   * a year of complaints is a file to open, not a page to scroll.
   */
  limit?: number
  /**
   * What a row opens to, under it (the complaints: their calls, to listen to).
   * The first cell becomes the button that opens and closes it.
   */
  expand?: (row: T) => React.ReactNode
  /** Controls that belong to this report alone, such as its grouping. */
  children?: React.ReactNode
}) {
  const { t, i18n } = useTranslation()
  const theme = useTheme()
  const section = useRef<HTMLElement>(null)
  const queryClient = useQueryClient()
  const [opened, setOpened] = useState<number | null>(null)

  // A card knows its rows, not the query behind them, so Retry asks again for
  // every report on the page that failed: when one fails, it is usually the
  // server, and the others failed with it.
  const retry = () =>
    void queryClient.refetchQueries({ type: 'active', predicate: (query) => query.state.status === 'error' })

  function onExport() {
    if (!sorted) return
    // In the table's order, as it is on screen.
    downloadCsv(exportName, toCsv(columns.map((c) => c.label), sorted.map((r) => columns.map((c) => c.value(r)))))
  }

  // The table's order, chosen by its headings (S-70): the report's own until
  // a heading is pressed. The chart keeps the report's order, which for a
  // line over time is the only one that reads.
  const [sort, setSort] = useState<{ key: string; descending: boolean } | null>(null)
  const sorted = useMemo(() => {
    const column = sort && columns.find((c) => c.key === sort.key)
    if (!rows || !column) return rows
    const collator = new Intl.Collator(i18n.language, { numeric: true, sensitivity: 'base' })
    const compare = (a: CsvCell, b: CsvCell) => {
      // A blank goes last whichever way the column runs.
      const blankA = a === null || a === undefined || a === ''
      const blankB = b === null || b === undefined || b === ''
      if (blankA || blankB) return blankA === blankB ? 0 : blankA ? 1 : -1
      const order = typeof a === 'number' && typeof b === 'number' ? a - b : collator.compare(String(a), String(b))
      return sort!.descending ? -order : order
    }
    return [...rows].sort((a, b) => compare(column.value(a), column.value(b)))
  }, [rows, columns, sort, i18n.language])

  /** First press: most first for a figure, A to Z for text; then the other way; then the report's own order. */
  function sortBy(column: ReportColumn<T>) {
    setOpened(null)
    const first = Boolean(column.numeric)
    if (sort?.key !== column.key) setSort({ key: column.key, descending: first })
    else if (sort.descending === first) setSort({ key: column.key, descending: !first })
    else setSort(null)
  }

  const number = (v: CsvCell) => (typeof v === 'number' ? v.toLocaleString(i18n.language) : (v ?? ''))
  const totals = columns.some((c) => c.total)
  const shown = sorted && limit !== undefined && sorted.length > limit ? sorted.slice(0, limit) : sorted
  const heatColumns = columns.filter((c) => c.heat)
  const heatMax = rows && heatColumns.length > 0
    ? Math.max(0, ...rows.flatMap((r) => heatColumns.map((c) => Number(c.value(r)) || 0)))
    : 0

  return (
    <section ref={section} className="card report-card" aria-label={title}>
      <div className="card-header">
        <div>
          <h3 className="font-semibold text-slate-100">{title}</h3>
          {hint && <p className="text-sm text-slate-400">{hint}</p>}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {children}
          <button type="button" className="btn-ghost btn-sm" onClick={onExport} disabled={!rows || rows.length === 0}>
            {t('applicationReports.export')}
          </button>
          {/* This report alone, with the page's printed heading (lib/print). */}
          <button type="button" className="btn-ghost btn-sm" onClick={() => section.current && printOnly(section.current)}
            disabled={!rows || rows.length === 0}>
            {t('callReports.print')}
          </button>
        </div>
      </div>

      <div className="card-body space-y-4">
        {error ? (
          <LoadError message={t('applicationReports.failed')} onRetry={retry} busy={loading} />
        ) : loading && !rows ? (
          // Its space, held while it comes, so the page does not jump.
          <div className="h-40 animate-pulse rounded-md bg-ink-800/60" aria-hidden="true" />
        ) : !rows || rows.length === 0 ? (
          <p className="text-center text-slate-400">{t('applicationReports.empty')}</p>
        ) : (
          // Held at reduced opacity while a new slice loads, not replaced by a
          // skeleton: the layout must not jump every time a filter changes.
          <div className={loading ? 'opacity-60 transition' : 'transition'}>
            {chart && <ReportChart rows={rows} spec={chart} imageName={exportName} />}
            <div className="overflow-x-auto">
              <table className="table">
                <thead>
                  <tr>
                    {/* A number column is centred, heading and figures alike, so
                        each figure sits under its heading in either language
                        (Dia, 26 Sep). `!` because `.table thead th` sets
                        text-start and outranks a plain utility. */}
                    {columns.map((c) => {
                      const by = sort?.key === c.key ? sort : null
                      return (
                        <th key={c.key} className={c.numeric ? '!text-center' : undefined}
                            aria-sort={by ? (by.descending ? 'descending' : 'ascending') : undefined}>
                          <button type="button" className="th-sort print-keep" onClick={() => sortBy(c)}>
                            {c.label}
                            <span aria-hidden="true" className={`print:hidden ${by ? '' : 'opacity-0'}`}>
                              {by?.descending ? '↓' : '↑'}
                            </span>
                          </button>
                        </th>
                      )
                    })}
                  </tr>
                </thead>
                <tbody>
                  {shown!.map((row, i) => (
                    <Fragment key={i}>
                      <tr>
                        {columns.map((c, ci) => {
                          // Centred under its heading; only the figure itself is
                          // held left-to-right, so 1,587.74 reads the same in both.
                          const text = c.numeric || c.ltr
                            ? <span dir="ltr">{c.format ? c.format(row) : number(c.value(row))}</span>
                            : c.format ? c.format(row) : number(c.value(row))
                          return (
                            <td key={c.key}
                              className={[c.numeric ? 'tabular text-center' : '', c.heat ? 'heat-cell' : ''].join(' ').trim() || undefined}
                              style={c.heat ? heatStyle(Number(c.value(row)) || 0, heatMax, theme) : undefined}>
                              {expand && ci === 0 ? (
                                <button type="button" className="print-keep text-start text-brand-500 underline" aria-expanded={opened === i}
                                  onClick={() => setOpened(opened === i ? null : i)}>
                                  <span aria-hidden="true" className="print:hidden">{opened === i ? '▾ ' : '▸ '}</span>{text}
                                </button>
                              ) : text}
                            </td>
                          )
                        })}
                      </tr>
                      {expand && opened === i && (
                        <tr className="no-print">
                          <td colSpan={columns.length} className="bg-ink-900/40">{expand(row)}</td>
                        </tr>
                      )}
                    </Fragment>
                  ))}
                  {totals && (
                    <tr className="font-semibold text-slate-100">
                      {columns.map((c, i) => (
                        <td key={c.key} className={c.numeric ? 'tabular text-center' : undefined}>
                          {i === 0
                            ? t('applicationReports.total')
                            : c.total
                              ? <span dir="ltr">{number(rows.reduce((sum, r) => sum + (Number(c.value(r)) || 0), 0))}</span>
                              : ''}
                        </td>
                      ))}
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
            {heatMax > 0 && (
              // The scale, so the shade is never the only way to read a cell:
              // the number is in it, and this says which way the shade runs.
              <div className="mt-2 flex items-center gap-2 text-xs text-slate-400" aria-hidden="true">
                <span>{t('callReports.fewer')}</span>
                {[0.25, 0.5, 0.75, 1].map((f) => (
                  <span key={f} className="heat-cell inline-block h-3 w-6 rounded-sm" style={heatStyle(f, 1, theme)} />
                ))}
                <span>{t('callReports.more')}</span>
              </div>
            )}
            {shown !== rows && (
              <p className="mt-2 text-sm text-slate-400">
                {t('callReports.limited', { shown: shown!.length, total: rows.length })}
              </p>
            )}
          </div>
        )}
      </div>
    </section>
  )
}

/**
 * The chart chrome, in the app's own palette (index.css, tailwind.config), one
 * set per theme (S-69). Recharts writes these onto the SVG, so they are
 * values here rather than CSS variables, and the picture download keeps them.
 */
const CHROMES = {
  dark: {
    surface: '#171A21', // ink-900, the card
    raised: '#1E222B', // ink-800
    grid: '#2A2F3A', // ink-700
    ink: '#e2e8f0', // slate-200
    muted: '#94a3b8', // slate-400
    cursor: 'rgba(255,255,255,0.04)',
  },
  light: {
    surface: '#FFFFFF', // ink-900, the card
    raised: '#FFFFFF', // the tooltip: a white card with a border
    grid: '#D5D9E0', // ink-700
    ink: '#262C38', // slate-200
    muted: '#555E70', // slate-400
    cursor: 'rgba(16,24,40,0.05)',
  },
} as const

const CHART_HEIGHT = 240

/**
 * The chart, sized to its card. Recharts' ResponsiveContainer needs a
 * ResizeObserver, which the test browser lacks, so the width is measured here
 * and the chart waits until there is one; zero width draws nothing.
 *
 * **Right-to-left**: the categories run right to left and the value axis sits
 * on the right, so the chart reads the way the Arabic table beside it does.
 * The SVG itself is drawn left-to-right, or the text anchors would mirror.
 */
export function ReportChart<T>({ rows, spec, imageName }: {
  rows: T[]
  spec: ReportChartSpec<T>
  /** The picture's file name without .png (S-06: a chart can be downloaded as an image). */
  imageName: string
}) {
  const { t, i18n } = useTranslation()
  const rtl = i18n.dir() === 'rtl'
  const ref = useRef<HTMLDivElement>(null)
  const width = useWidth(ref)
  const [saving, setSaving] = useState<'idle' | 'busy' | 'failed'>('idle')
  const theme = useTheme()
  const CHROME = CHROMES[theme]
  const colour = (s: ReportSeries<T>) => seriesColour(s.colour, theme)

  async function onDownload() {
    const svg = ref.current?.querySelector('svg.recharts-surface') as SVGSVGElement | null
    if (!svg) return
    setSaving('busy')
    try {
      const legend = spec.series.map((s) => ({ label: s.label, colour: colour(s) }))
      downloadBlob(`${imageName}.png`, await chartToPng(svg, legend, rtl, { background: CHROME.surface, ink: CHROME.ink }))
      setSaving('idle')
    } catch {
      setSaving('failed')
    }
  }

  const data = rows.map((row) => {
    const point: Record<string, unknown> = { x: spec.x(row) }
    for (const s of spec.series) point[s.key] = row[s.key]
    return point
  })

  const format = (v: unknown) => (typeof v === 'number' ? v.toLocaleString(i18n.language) : String(v ?? ''))
  const legend = spec.series.length > 1

  // Shared by both chart kinds: hairline grid, recessive axes, a tooltip in the card's own colours.
  const axes = (
    <>
      <CartesianGrid vertical={false} stroke={CHROME.grid} />
      <XAxis
        dataKey="x"
        reversed={rtl}
        tick={{ fill: CHROME.muted, fontSize: 12 }}
        axisLine={{ stroke: CHROME.grid }}
        tickLine={false}
        interval="preserveStartEnd"
      />
      <YAxis
        orientation={rtl ? 'right' : 'left'}
        allowDecimals={false}
        width={48}
        tick={{ fill: CHROME.muted, fontSize: 12 }}
        axisLine={false}
        tickLine={false}
        tickFormatter={format}
      />
      <Tooltip
        cursor={{ fill: CHROME.cursor, stroke: CHROME.grid }}
        contentStyle={{ background: CHROME.raised, border: `1px solid ${CHROME.grid}`, borderRadius: 8 }}
        labelStyle={{ color: CHROME.ink }}
        itemStyle={{ color: CHROME.ink }}
        formatter={(value) => format(value)}
      />
      {/* Text never wears the series colour: the swatch carries identity. */}
      {legend && <Legend formatter={(value) => <span style={{ color: CHROME.ink }}>{value}</span>} />}
    </>
  )

  return (
    <div className="mb-4">
    <div ref={ref} dir="ltr" className="w-full" style={{ height: CHART_HEIGHT }} data-testid="report-chart">
      {width > 0 && spec.kind === 'bar' && (
        <BarChart width={width} height={CHART_HEIGHT} data={data} barGap={2} barCategoryGap="30%">
          {axes}
          {spec.series.map((s) => (
            <Bar key={s.key} dataKey={s.key} name={s.label} fill={colour(s)} maxBarSize={24} radius={[4, 4, 0, 0]} />
          ))}
        </BarChart>
      )}
      {width > 0 && spec.kind === 'line' && (
        <LineChart width={width} height={CHART_HEIGHT} data={data}>
          {axes}
          {spec.series.map((s) => (
            <Line
              key={s.key}
              type="monotone"
              dataKey={s.key}
              name={s.label}
              stroke={colour(s)}
              strokeWidth={2}
              // A surface ring on every marker, so dots stay legible where lines cross.
              dot={{ r: 4, fill: colour(s), stroke: CHROME.surface, strokeWidth: 2 }}
              activeDot={{ r: 6, stroke: CHROME.surface, strokeWidth: 2 }}
            />
          ))}
        </LineChart>
      )}
    </div>
    <div className="mt-1 flex items-center justify-end gap-2 text-sm">
      {saving === 'failed' && <span className="text-red-300">{t('callReports.imageFailed')}</span>}
      <button type="button" className="btn-quiet btn-sm" onClick={onDownload} disabled={saving === 'busy' || width === 0}>
        {t('callReports.downloadImage')}
      </button>
    </div>
    </div>
  )
}

/** The element's width, followed as the window changes. Zero until measured. */
function useWidth(ref: React.RefObject<HTMLElement>): number {
  const [width, setWidth] = useState(0)
  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    const measure = () => setWidth(element.getBoundingClientRect().width)
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [ref])
  return width
}
