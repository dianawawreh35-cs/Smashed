import { useEffect, useMemo, useRef, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  NICKNAME_MAX,
  acknowledgeErrors,
  getAcknowledgement,
  isError,
  isWarning,
  laptopName,
  listAgentLogs,
  readAgentLog,
  setLaptopNickname,
} from '../api/agentLogs'
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
 *
 * One date for the whole page, today until another is picked, so the page
 * opens on what is happening now rather than on each laptop's latest day.
 */
export default function LogsPage() {
  const { t } = useTranslation()
  const [live, setLive] = useState(true)
  const [chosen, setChosen] = useState<string | null>(null)
  // Null follows today, so a page left open past midnight moves on with it.
  const [picked, setPicked] = useState<string | null>(null)
  const today = localDate(new Date())
  const date = picked ?? today

  const list = useQuery({
    queryKey: ['agent-logs'],
    queryFn: listAgentLogs,
    refetchInterval: live ? REFRESH_MS : false,
  })

  const laptops = useMemo(() => byErrorsFirst(list.data ?? [], date), [list.data, date])
  const laptop = laptops.find((l) => l.laptop === chosen) ?? laptops[0]
  const oldest = laptops.flatMap((l) => l.days.map((d) => d.date)).sort()[0]

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="page-title">{t('logs.heading')}</h1>
          <p className="page-subtitle">{t('logs.intro')}</p>
        </div>
        <div className="flex flex-wrap items-center gap-4">
          <label className="flex items-center gap-2 text-sm text-slate-300">
            {t('logs.date')}
            <input
              type="date"
              value={date}
              min={oldest}
              max={today}
              // Clearing the box goes back to today rather than to no date.
              onChange={(e) => setPicked(e.target.value && e.target.value !== today ? e.target.value : null)}
              className="input w-auto"
            />
          </label>
          {date !== today && (
            <button type="button" onClick={() => setPicked(null)} className="btn-ghost btn-sm">
              {t('logs.today')}
            </button>
          )}
          <label className="flex items-center gap-2 text-sm text-slate-300">
            <input type="checkbox" checked={live} onChange={(e) => setLive(e.target.checked)} />
            {t('logs.live')}
          </label>
        </div>
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
          <ErrorsAlert laptops={laptops} today={today} live={live} />

          <div className="grid gap-6 lg:grid-cols-[16rem_1fr]">
            <LaptopList laptops={laptops} date={date} chosen={laptop.laptop} onChoose={setChosen} />
            {/* Keyed by laptop, so a new laptop starts with the filter cleared. */}
            <LaptopLog key={laptop.laptop} laptop={laptop} day={dayOf(laptop, date)} date={date} live={live} />
          </div>
        </>
      )}
    </div>
  )
}

/**
 * The red line about today's errors, until a supervisor acknowledges them.
 *
 * The acknowledgement keeps each laptop's count, for every supervisor, so an
 * error that arrives afterwards brings the line back with only the new ones.
 * If it cannot be read the line shows, rather than hiding errors nobody saw.
 */
function ErrorsAlert({ laptops, today, live }: { laptops: AgentLogLaptop[]; today: string; live: boolean }) {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()

  const ack = useQuery({
    queryKey: ['agent-logs-acknowledged'],
    queryFn: getAcknowledgement,
    refetchInterval: live ? REFRESH_MS : false,
  })

  const counts = Object.fromEntries(
    laptops.map((l) => [l.laptop, dayOf(l, today)?.errors ?? 0] as const).filter(([, errors]) => errors > 0),
  )
  const seen = ack.data?.date === today ? ack.data : null
  const fresh = Object.entries(counts)
    .map(([laptop, errors]) => errors - (seen?.errors[laptop] ?? 0))
    .filter((errors) => errors > 0)

  const acknowledge = useMutation({
    mutationFn: () => acknowledgeErrors(today, counts),
    onSuccess: (saved) => queryClient.setQueryData(['agent-logs-acknowledged'], saved),
  })

  if (fresh.length === 0) {
    return seen && Object.keys(counts).length > 0 ? (
      <p className="text-sm text-slate-500">
        {t('logs.acknowledged', { by: seen.by ?? '—', when: formatWhen(seen.at, i18n.language) })}
      </p>
    ) : null
  }

  const count = fresh.reduce((sum, errors) => sum + errors, 0)

  return (
    <div role="status" className="notice-error flex flex-wrap items-center justify-between gap-3">
      <span>
        {t(seen ? 'logs.newErrorsToday' : 'logs.errorsToday', { count, laptops: fresh.length })}
        {acknowledge.isError && <span className="ms-2 font-semibold">{t('logs.acknowledgeFailed')}</span>}
      </span>
      <button
        type="button"
        onClick={() => acknowledge.mutate()}
        disabled={acknowledge.isPending}
        className="btn-ghost btn-sm"
      >
        {t('logs.acknowledge')}
      </button>
    </div>
  )
}

/**
 * Laptops with errors on the date first, then those with a log that day, then
 * the most recently heard from.
 */
function byErrorsFirst(laptops: AgentLogLaptop[], date: string): AgentLogLaptop[] {
  const rank = (l: AgentLogLaptop) => {
    const day = dayOf(l, date)
    return day ? (day.errors > 0 ? 2 : 1) : 0
  }
  return [...laptops].sort((a, b) => rank(b) - rank(a) || b.lastWriteAt.localeCompare(a.lastWriteAt))
}

function dayOf(laptop: AgentLogLaptop, date: string): AgentLogDay | undefined {
  return laptop.days.find((d) => d.date === date)
}

/** `yyyy-MM-dd` in the browser's time, as the laptops name their days. */
function localDate(when: Date): string {
  return `${when.getFullYear()}-${String(when.getMonth() + 1).padStart(2, '0')}-${String(when.getDate()).padStart(2, '0')}`
}

function LaptopList({
  laptops,
  date,
  chosen,
  onChoose,
}: {
  laptops: AgentLogLaptop[]
  date: string
  chosen: string
  onChoose: (laptop: string) => void
}) {
  const { t, i18n } = useTranslation()

  return (
    <nav aria-label={t('logs.laptops')} className="card divide-y divide-ink-700 self-start">
      {laptops.map((laptop) => {
        const day = dayOf(laptop, date)
        const selected = laptop.laptop === chosen
        return (
          <button
            key={laptop.laptop}
            type="button"
            onClick={() => onChoose(laptop.laptop)}
            aria-current={selected ? 'true' : undefined}
            className={`block w-full px-4 py-3 text-start transition hover:bg-ink-800/60 ${
              selected ? 'bg-ink-800' : ''
            } ${day?.errors ? 'border-s-2 border-red-500' : 'border-s-2 border-transparent'}`}
          >
            <span className="block font-medium text-slate-100" dir="auto">
              {laptopName(laptop)}
            </span>
            {laptop.nickname && (
              <span className="block text-xs text-slate-500" dir="ltr">
                {laptop.laptop}
              </span>
            )}
            <span className="block text-xs text-slate-500">
              {t('logs.lastHeard', { when: formatWhen(laptop.lastWriteAt, i18n.language) })}
            </span>
            <span className="mt-1.5 flex flex-wrap gap-1.5">
              {day ? <Counts day={day} /> : <span className="text-xs text-slate-500">{t('logs.noLogThatDay')}</span>}
            </span>
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

function LaptopLog({
  laptop,
  day,
  date,
  live,
}: {
  laptop: AgentLogLaptop
  /** The laptop's log on the page's date; none when it sent nothing that day. */
  day: AgentLogDay | undefined
  date: string
  live: boolean
}) {
  const { t } = useTranslation()
  const [levels, setLevels] = useState<AgentLogLevels>('all')
  const [search, setSearch] = useState('')
  const searched = useDebounced(search)

  const page = useQuery({
    queryKey: ['agent-log', laptop.laptop, day?.file, levels, searched],
    queryFn: () => readAgentLog(laptop.laptop, day!.file, levels, searched),
    enabled: day !== undefined,
    placeholderData: keepPreviousData,
    refetchInterval: live ? REFRESH_MS : false,
  })

  // Not the last day's, kept as a placeholder, on a date this laptop has none.
  const entries = (day && page.data?.entries) || []
  const errorRows = useRef<(HTMLLIElement | null)[]>([])
  const [atError, setAtError] = useState(-1)
  const errorCount = entries.filter((e) => isError(e.level)).length

  // A new day, filter or search starts the walk over.
  useEffect(() => setAtError(-1), [day?.file, levels, searched])

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
    <section aria-label={laptopName(laptop)} className="min-w-0 space-y-4">
      <Nickname laptop={laptop} />

      <div className="flex flex-wrap items-center gap-3">
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

      {!day ? (
        <div className="card card-body text-center">
          <p className="text-slate-300">{t('logs.noLogOn', { date })}</p>
        </div>
      ) : page.isError ? (
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
          {/* Scrolls inside itself, so the filters stay in view over a long day. */}
          <ol
            dir="ltr"
            className={`card max-h-[70vh] divide-y divide-ink-800 overflow-y-auto font-mono text-xs ${
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

/** The laptop's name above its log, and a box to change it. */
function Nickname({ laptop }: { laptop: AgentLogLaptop }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState('')

  const save = useMutation({
    meta: { toast: 'saved' },
    mutationFn: () => setLaptopNickname(laptop.laptop, name),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['agent-logs'] })
      setEditing(false)
    },
  })

  if (!editing) {
    return (
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <h2 className="text-lg font-semibold text-slate-100" dir="auto">
          {laptopName(laptop)}
        </h2>
        {laptop.nickname && (
          <span className="text-sm text-slate-500" dir="ltr">
            {laptop.laptop}
          </span>
        )}
        <button
          type="button"
          onClick={() => {
            setName(laptop.nickname ?? '')
            save.reset()
            setEditing(true)
          }}
          className="btn-ghost btn-sm"
        >
          {laptop.nickname ? t('logs.rename') : t('logs.nameIt')}
        </button>
      </div>
    )
  }

  return (
    <form
      className="space-y-2"
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
    >
      <div className="flex flex-wrap items-center gap-2">
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={NICKNAME_MAX}
          placeholder={t('logs.nicknamePlaceholder')}
          aria-label={t('logs.nickname', { laptop: laptop.laptop })}
          dir="auto"
          autoFocus
          className="input max-w-xs"
        />
        <button type="submit" disabled={save.isPending} className="btn-primary btn-sm">
          {t('logs.saveNickname')}
        </button>
        <button type="button" onClick={() => setEditing(false)} className="btn-ghost btn-sm">
          {t('logs.cancel')}
        </button>
      </div>
      <p className="field-hint">{t('logs.nicknameHint', { laptop: laptop.laptop })}</p>
      {save.isError && (
        <div role="alert" className="notice-error">
          {t('logs.nicknameFailed')}
        </div>
      )}
    </form>
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
