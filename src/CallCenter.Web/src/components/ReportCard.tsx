import { useLayoutEffect, useRef, useState } from 'react'
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
import { downloadCsv, toCsv } from '../lib/csv'
import type { CsvCell } from '../lib/csv'

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
  /** Summed in a last row. Only for a count or an amount, never an average. */
  total?: boolean
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
 * first, then the dataviz reference palette's dark-surface orange and aqua.
 * Validated together against the card surface (#171A21) with the dataviz
 * palette checker: every adjacent pair clears the colour-blindness floor
 * (worst ΔE 9.4) and 3:1 contrast. A fourth series would need re-validating.
 */
export const SERIES_COLOURS = ['#4F8CFF', '#d95926', '#199e70'] as const

export default function ReportCard<T>({
  title,
  hint,
  columns,
  rows,
  loading,
  error,
  chart,
  exportName,
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
  /** Controls that belong to this report alone, such as its grouping. */
  children?: React.ReactNode
}) {
  const { t, i18n } = useTranslation()

  function onExport() {
    if (!rows) return
    downloadCsv(exportName, toCsv(columns.map((c) => c.label), rows.map((r) => columns.map((c) => c.value(r)))))
  }

  const number = (v: CsvCell) => (typeof v === 'number' ? v.toLocaleString(i18n.language) : (v ?? ''))
  const totals = columns.some((c) => c.total)

  return (
    <section className="card" aria-label={title}>
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
        </div>
      </div>

      <div className="card-body space-y-4">
        {error ? (
          <p className="notice-error">{t('applicationReports.failed')}</p>
        ) : loading && !rows ? (
          // Its space, held while it comes, so the page does not jump.
          <div className="h-40 animate-pulse rounded-md bg-ink-800/60" aria-hidden="true" />
        ) : !rows || rows.length === 0 ? (
          <p className="text-center text-slate-400">{t('applicationReports.empty')}</p>
        ) : (
          // Held at reduced opacity while a new slice loads, not replaced by a
          // skeleton: the layout must not jump every time a filter changes.
          <div className={loading ? 'opacity-60 transition' : 'transition'}>
            {chart && <ReportChart rows={rows} spec={chart} />}
            <div className="overflow-x-auto">
              <table className="table">
                <thead>
                  <tr>
                    {columns.map((c) => (
                      <th key={c.key} className={c.numeric ? 'text-end' : undefined}>{c.label}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row, i) => (
                    <tr key={i}>
                      {columns.map((c) => (
                        <td key={c.key} className={c.numeric ? 'tabular text-end' : undefined} dir={c.numeric ? 'ltr' : undefined}>
                          {c.format ? c.format(row) : number(c.value(row))}
                        </td>
                      ))}
                    </tr>
                  ))}
                  {totals && (
                    <tr className="font-semibold text-slate-100">
                      {columns.map((c, i) => (
                        <td key={c.key} className={c.numeric ? 'tabular text-end' : undefined} dir={c.numeric ? 'ltr' : undefined}>
                          {i === 0
                            ? t('applicationReports.total')
                            : c.total
                              ? number(rows.reduce((sum, r) => sum + (Number(c.value(r)) || 0), 0))
                              : ''}
                        </td>
                      ))}
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </section>
  )
}

/** The chart chrome, in the app's own dark palette (index.css, tailwind.config). */
const CHROME = {
  surface: '#171A21', // ink-900, the card
  raised: '#1E222B', // ink-800
  grid: '#2A2F3A', // ink-700
  ink: '#e2e8f0', // slate-200
  muted: '#94a3b8', // slate-400
}

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
function ReportChart<T>({ rows, spec }: { rows: T[]; spec: ReportChartSpec<T> }) {
  const { i18n } = useTranslation()
  const rtl = i18n.dir() === 'rtl'
  const ref = useRef<HTMLDivElement>(null)
  const width = useWidth(ref)

  const data = rows.map((row) => {
    const point: Record<string, unknown> = { x: spec.x(row) }
    for (const s of spec.series) point[s.key] = row[s.key]
    return point
  })

  const format = (v: unknown) => (typeof v === 'number' ? v.toLocaleString(i18n.language) : String(v ?? ''))
  const legend = spec.series.length > 1

  // Shared by both chart kinds: hairline grid, recessive axes, dark tooltip.
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
        cursor={{ fill: 'rgba(255,255,255,0.04)', stroke: CHROME.grid }}
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
    <div ref={ref} dir="ltr" className="mb-4 w-full" style={{ height: CHART_HEIGHT }} data-testid="report-chart">
      {width > 0 && spec.kind === 'bar' && (
        <BarChart width={width} height={CHART_HEIGHT} data={data} barGap={2} barCategoryGap="30%">
          {axes}
          {spec.series.map((s) => (
            <Bar key={s.key} dataKey={s.key} name={s.label} fill={s.colour} maxBarSize={24} radius={[4, 4, 0, 0]} />
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
              stroke={s.colour}
              strokeWidth={2}
              // A surface ring on every marker, so dots stay legible where lines cross.
              dot={{ r: 4, fill: s.colour, stroke: CHROME.surface, strokeWidth: 2 }}
              activeDot={{ r: 6, stroke: CHROME.surface, strokeWidth: 2 }}
            />
          ))}
        </LineChart>
      )}
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
