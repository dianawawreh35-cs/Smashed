import { useEffect, useMemo, useRef, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { isError, isWarning, listAgentLogs, readAgentLog } from '../api/agentLogs'
import type { AgentLogDay, AgentLogEntry, AgentLogLaptop, AgentLogLevels } from '../api/agentLogs'
import LoadError from '../components/LoadError'
import { useDebounced } from '../lib/useDebounced'

/** How often the page asks again while "Keep up to date" is on: the laptops send every 30 s. */
const REFRESH_MS = 30_000

/**
 * The Agent Apps' logs (N-12): what each laptop logged, as it sent it to the
 * server, so a fault can be read here rather than on the laptop.
 *
 * Errors are what this page is for, so they are what it shows first: the
 * laptops with errors on their latest day come to the top with a count, an
 * error entry is red and a warning amber, and "Next error" walks down them.
 * The text itself stays as the app wrote it, left to right in either
 * language: it is English, file paths and stack traces.
 */
export default function LogsPage() {
  const { t } = useTranslation()
  const [live, setLive] = useState(true)
  const [chosen, setChosen] = useState<string | null>(null)

  const list = useQuery({
    queryKey: ['agent-logs'],
    queryFn: listAgentLogs,
    refetchInterval: live ? REFRESH_MS : false,
  })

  const laptops = useMemo(() => byErrorsFirst(list.data ?? []), [list.data])
  const laptop = laptops.find((l) => l.laptop === chosen) ?? laptops[0]
  const errorsToday = laptops.filter((l) => l.days[0]?.errors > 0 && isToday(l.days[0].date))

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="page-title">{t('logs.heading')}</h1>
          <p className="page-subtitle">{t('logs.intro')}</p>
        </div>
        <label className="flex items-center gap-2 text-sm text-slate-300">
          <input type="checkbox" checked={live} onChange={(e) => setLive(e.target.checked)} />
          {t('logs.live')}
        </label>
      </div>

      {list.isError ? (
        <LoadError message={t('logs.failed')} onRetry={() => void list.refetch()} busy={list.isFetching} />
      ) : list.isPending ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : laptops.length === 0 || !laptop ? (
        <div className="card card-body text-center">
          <p className="text-slate-300">{t('logs.empty')}</p>
          <p className="mt-1 text-sm text-slate-500">{t('logs.emptyHint')}</p>
        </div>
      ) : (
        <>
          {errorsToday.length > 0 && (
            <div role="status" className="notice-error">
              {t('logs.errorsToday', {
                count: errorsToday.reduce((sum, l) => sum + l.days[0].errors, 0),
                laptops: errorsToday.length,
              })}
            </div>
          )}

          <div className="grid gap-6 lg:grid-cols-[16rem_1fr]">
            <LaptopList laptops={laptops} chosen={laptop.laptop} onChoose={setChosen} />
            {/* Keyed by laptop, so a new laptop starts on its newest day with the filter cleared. */}
            <LaptopLog key={laptop.laptop} laptop={laptop} live={live} />
          </div>
        </>
      )}
    </div>
  )
}

/** Laptops whose latest day has errors first, then the most recently heard from. */
function byErrorsFirst(laptops: AgentLogLaptop[]): AgentLogLaptop[] {
  return [...laptops].sort((a, b) => {
    const errors = Number((b.days[0]?.errors ?? 0) > 0) - Number((a.days[0]?.errors ?? 0) > 0)
    return errors !== 0 ? errors : b.lastWriteAt.localeCompare(a.lastWriteAt)
  })
}

function isToday(date: string): boolean {
  const now = new Date()
  const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
  return date === today
}

function LaptopList({
  laptops,
  chosen,
  onChoose,
}: {
  laptops: AgentLogLaptop[]
  chosen: string
  onChoose: (laptop: string) => void
}) {
  const { t, i18n } = useTranslation()

  return (
    <nav aria-label={t('logs.laptops')} className="card divide-y divide-ink-700 self-start">
      {laptops.map((laptop) => {
        const latest = laptop.days[0]
        const selected = laptop.laptop === chosen
        return (
          <button
            key={laptop.laptop}
            type="button"
            onClick={() => onChoose(laptop.laptop)}
            aria-current={selected ? 'true' : undefined}
            className={`block w-full px-4 py-3 text-start transition hover:bg-ink-800/60 ${
              selected ? 'bg-ink-800' : ''
            } ${latest?.errors ? 'border-s-2 border-red-500' : 'border-s-2 border-transparent'}`}
          >
            <span className="block font-medium text-slate-100" dir="ltr">
              {laptop.laptop}
            </span>
            <span className="block text-xs text-slate-500">
              {t('logs.lastHeard', { when: formatWhen(laptop.lastWriteAt, i18n.language) })}
            </span>
            {latest && (
              <span className="mt-1.5 flex flex-wrap gap-1.5">
                <Counts day={latest} />
              </span>
            )}
          </button>
        )
      })}
    </nav>
  )
}

/** A day's errors and warnings as badges, or a quiet "no errors". */
function Counts({ day }: { day: AgentLogDay }) {
  const { t } = useTranslation()

  if (day.errors === 0 && day.warnings === 0) {
    return <span className="badge-ok">{t('logs.noProblems')}</span>
  }

  return (
    <>
      {day.errors > 0 && <span className="badge-blocked">{t('logs.errors', { count: day.errors })}</span>}
      {day.warnings > 0 && <span className="badge-vip">{t('logs.warnings', { count: day.warnings })}</span>}
    </>
  )
}

function LaptopLog({ laptop, live }: { laptop: AgentLogLaptop; live: boolean }) {
  const { t } = useTranslation()
  const [file, setFile] = useState(laptop.days[0].file)
  const [levels, setLevels] = useState<AgentLogLevels>('all')
  const [search, setSearch] = useState('')
  const searched = useDebounced(search)

  // A day that has gone (retention) falls back to the newest.
  const day = laptop.days.find((d) => d.file === file) ?? laptop.days[0]

  const page = useQuery({
    queryKey: ['agent-log', laptop.laptop, day.file, levels, searched],
    queryFn: () => readAgentLog(laptop.laptop, day.file, levels, searched),
    placeholderData: keepPreviousData,
    refetchInterval: live ? REFRESH_MS : false,
  })

  const entries = page.data?.entries ?? []
  const errorRows = useRef<(HTMLLIElement | null)[]>([])
  const [atError, setAtError] = useState(-1)
  const errorCount = entries.filter((e) => isError(e.level)).length

  // A new day, filter or search starts the walk over.
  useEffect(() => setAtError(-1), [day.file, levels, searched])

  function nextError() {
    if (errorCount === 0) return
    const next = (atError + 1) % errorCount
    setAtError(next)
    const row = errorRows.current[next]
    row?.scrollIntoView?.({ block: 'center', behavior: 'smooth' })
    row?.focus({ preventScroll: true })
  }

  let errorIndex = 0

  return (
    <section aria-label={laptop.laptop} className="min-w-0 space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <select
          value={day.file}
          onChange={(e) => setFile(e.target.value)}
          aria-label={t('logs.day')}
          className="input max-w-xs"
        >
          {laptop.days.map((d) => (
            <option key={d.file} value={d.file}>
              {t('logs.dayOption', { date: d.date, errors: d.errors, warnings: d.warnings })}
            </option>
          ))}
        </select>

        <div role="group" aria-label={t('logs.show')} className="inline-flex rounded-md border border-ink-700">
          {(['all', 'warnings', 'errors'] as const).map((value) => (
            <button
              key={value}
              type="button"
              onClick={() => setLevels(value)}
              aria-pressed={levels === value}
              className={`px-3 py-1.5 text-sm ${
                levels === value ? 'bg-ink-800 text-slate-100' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              {t(`logs.levels.${value}`)}
            </button>
          ))}
        </div>

        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t('logs.searchPlaceholder')}
          aria-label={t('logs.search')}
          className="input max-w-xs"
        />

        <button type="button" onClick={nextError} disabled={errorCount === 0} className="btn-ghost btn-sm">
          {t('logs.nextError')}
        </button>
      </div>

      {page.isError ? (
        <LoadError message={t('logs.dayFailed')} onRetry={() => void page.refetch()} busy={page.isFetching} />
      ) : page.isPending ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : entries.length === 0 ? (
        <div className="card card-body text-center">
          <p className="text-slate-300">{searched || levels !== 'all' ? t('logs.noMatches') : t('logs.emptyDay')}</p>
        </div>
      ) : (
        <>
          <p className="text-sm text-slate-400">
            {page.data.matched > entries.length
              ? t('logs.shownOf', { shown: entries.length, count: page.data.matched })
              : t('logs.shown', { count: entries.length })}
          </p>
          <ol
            dir="ltr"
            className={`card divide-y divide-ink-800 overflow-hidden font-mono text-xs ${
              page.isPlaceholderData ? 'opacity-60' : ''
            }`}
            aria-busy={page.isPlaceholderData}
          >
            {entries.map((entry) => {
              const index = isError(entry.level) ? errorIndex++ : -1
              return (
                <EntryRow
                  key={`${entry.line}-${entry.time}`}
                  entry={entry}
                  current={index >= 0 && index === atError}
                  rowRef={index >= 0 ? (el) => { errorRows.current[index] = el } : undefined}
                />
              )
            })}
          </ol>
        </>
      )}
    </section>
  )
}

/** How many lines of a long entry show before "Show all". An error shows whole. */
const FOLDED_LINES = 1

function EntryRow({
  entry,
  current,
  rowRef,
}: {
  entry: AgentLogEntry
  current: boolean
  /** An error's row, for "Next error" to scroll to. */
  rowRef?: (el: HTMLLIElement | null) => void
}) {
  const { t } = useTranslation()
  const error = isError(entry.level)
  const warning = isWarning(entry.level)
  const lines = entry.text.split('\n')
  const [open, setOpen] = useState(error)
  const shown = open ? lines : lines.slice(0, FOLDED_LINES)

  const tone = error
    ? 'bg-red-500/10 border-red-500 text-red-200'
    : warning
      ? 'bg-amber-500/10 border-amber-500 text-amber-100'
      : 'border-transparent text-slate-300'

  return (
    <li
      ref={rowRef}
      tabIndex={error ? -1 : undefined}
      data-level={error ? 'error' : warning ? 'warning' : 'info'}
      className={`flex gap-3 border-s-2 px-3 py-1.5 outline-none ${tone} ${current ? 'ring-2 ring-inset ring-red-400' : ''}`}
    >
      <span className="shrink-0 text-slate-500" title={entry.time}>
        {entry.time.slice(11, 19)}
      </span>
      <span
        className={`w-8 shrink-0 font-semibold ${error ? 'text-red-400' : warning ? 'text-amber-400' : 'text-slate-500'}`}
      >
        {entry.level}
      </span>
      <span className="min-w-0 flex-1 whitespace-pre-wrap break-words">
        {shown.join('\n')}
        {lines.length > FOLDED_LINES && (
          <button
            type="button"
            onClick={() => setOpen(!open)}
            className="ms-2 font-sans text-xs text-slate-500 underline hover:text-slate-300"
          >
            {open ? t('logs.fold') : t('logs.unfold', { count: lines.length - FOLDED_LINES })}
          </button>
        )}
      </span>
    </li>
  )
}

/** A time today, or a date and time before it, in the page's language. */
function formatWhen(iso: string, language: string): string {
  const when = new Date(iso)
  const sameDay = when.toDateString() === new Date().toDateString()
  return sameDay
    ? when.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' })
    : when.toLocaleString(language, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })
}
