import { useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { searchCalls, startOfDay, startOfNextDay } from '../api/calls'
import type { CallFilters, CallRow } from '../api/calls'
import { listClassificationTypes } from '../api/classifications'
import { listBranches } from '../api/delivery'
import { listUsers } from '../api/users'
import CallDetails from '../components/CallDetails'
import { formatClock } from '../lib/recordingWav'

const PAGE_SIZE = 50

/** What the supervisor can pick a call's result from. Ringing and Logged never reach this list. */
const STATUSES = ['Answered', 'Missed', 'Rejected', 'NoAnswer', 'Blocked', 'Failed', 'Abandoned']

/** The form as typed: every value a string, turned into filters only on Search. */
interface Draft {
  q: string
  agentId: string
  branchId: string
  typeId: string
  status: string
  direction: string
  from: string
  to: string
  notes: string
  minOrder: string
  maxOrder: string
  recording: '' | 'yes' | 'no'
  classified: '' | 'yes' | 'no'
}

const EMPTY: Draft = {
  q: '', agentId: '', branchId: '', typeId: '', status: '', direction: '',
  from: '', to: '', notes: '', minOrder: '', maxOrder: '', recording: '', classified: '',
}

function toFilters(d: Draft): CallFilters {
  const text = (v: string) => (v.trim() ? v.trim() : undefined)
  const amount = (v: string) => (v.trim() && !Number.isNaN(Number(v)) ? Number(v) : undefined)
  const yesNo = (v: '' | 'yes' | 'no') => (v === '' ? undefined : v === 'yes')

  return {
    q: text(d.q),
    agentId: text(d.agentId),
    branchId: text(d.branchId),
    typeId: text(d.typeId),
    status: text(d.status),
    direction: text(d.direction),
    // Days in the supervisor's own time zone, sent as instants: from the start
    // of the first day to the start of the day after the last.
    from: d.from ? startOfDay(d.from) : undefined,
    to: d.to ? startOfNextDay(d.to) : undefined,
    notes: text(d.notes),
    minOrder: amount(d.minOrder),
    maxOrder: amount(d.maxOrder),
    hasRecording: yesNo(d.recording),
    classified: yesNo(d.classified),
  }
}

/**
 * Every call, searchable (S-02), and any one of them opened in full (S-03).
 *
 * **The server filters and pages.** Nothing here narrows a fetched page, which
 * would say "no calls" whenever the match was older than the page (20 Sep).
 * The filters apply on Search, not on every keystroke, so typing a number does
 * not send a query per digit across a year of calls.
 */
export default function CallsPage() {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const [draft, setDraft] = useState<Draft>(EMPTY)
  const [filters, setFilters] = useState<CallFilters>({})
  const [page, setPage] = useState(1)
  const [openId, setOpenId] = useState<string | null>(null)

  const results = useQuery({
    queryKey: ['calls', 'search', filters, page],
    queryFn: () => searchCalls(filters, page, PAGE_SIZE),
    placeholderData: keepPreviousData,
  })

  const agents = useQuery({ queryKey: ['users'], queryFn: listUsers })
  const branches = useQuery({ queryKey: ['branches'], queryFn: listBranches })
  const types = useQuery({ queryKey: ['classification', 'types'], queryFn: listClassificationTypes })

  const set = <K extends keyof Draft>(key: K) => (value: Draft[K]) => setDraft((d) => ({ ...d, [key]: value }))

  function onSearch(event: FormEvent) {
    event.preventDefault()
    setFilters(toFilters(draft))
    setPage(1)
  }

  function onClear() {
    setDraft(EMPTY)
    setFilters({})
    setPage(1)
  }

  const total = results.data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="space-y-6">
      <div>
        <h2 className="page-title">{t('calls.heading')}</h2>
        <p className="page-subtitle">{t('calls.intro')}</p>
      </div>

      <form onSubmit={onSearch} className="card card-body space-y-4" aria-label={t('calls.filters')}>
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
          <label className="field lg:col-span-2">
            <span className="field-label">{t('calls.filter.q')}</span>
            <input className="input" value={draft.q} onChange={(e) => set('q')(e.target.value)} />
          </label>
          <label className="field">
            <span className="field-label">{t('calls.filter.from')}</span>
            <input type="date" className="input" value={draft.from} onChange={(e) => set('from')(e.target.value)} />
          </label>
          <label className="field">
            <span className="field-label">{t('calls.filter.to')}</span>
            <input type="date" className="input" value={draft.to} onChange={(e) => set('to')(e.target.value)} />
          </label>

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
          <Select label={t('calls.columns.status')} value={draft.status} onChange={set('status')}>
            {STATUSES.map((s) => (
              <option key={s} value={s}>{t(`history.statuses.${s}`, { defaultValue: s })}</option>
            ))}
          </Select>

          <Select label={t('calls.columns.direction')} value={draft.direction} onChange={set('direction')}>
            <option value="In">{t('calls.directions.In')}</option>
            <option value="Out">{t('calls.directions.Out')}</option>
          </Select>
          <label className="field">
            <span className="field-label">{t('calls.filter.notes')}</span>
            <input className="input" value={draft.notes} onChange={(e) => set('notes')(e.target.value)} />
          </label>
          <div className="grid grid-cols-2 gap-2">
            <label className="field">
              <span className="field-label">{t('calls.filter.minOrder')}</span>
              <input type="number" min={0} className="input" dir="ltr" value={draft.minOrder}
                onChange={(e) => set('minOrder')(e.target.value)} />
            </label>
            <label className="field">
              <span className="field-label">{t('calls.filter.maxOrder')}</span>
              <input type="number" min={0} className="input" dir="ltr" value={draft.maxOrder}
                onChange={(e) => set('maxOrder')(e.target.value)} />
            </label>
          </div>
          <div className="grid grid-cols-2 gap-2">
            <Select label={t('calls.filter.recording')} value={draft.recording} onChange={set('recording')}>
              <option value="yes">{t('common.yes')}</option>
              <option value="no">{t('common.no')}</option>
            </Select>
            <Select label={t('calls.filter.classified')} value={draft.classified} onChange={set('classified')}>
              <option value="yes">{t('common.yes')}</option>
              <option value="no">{t('common.no')}</option>
            </Select>
          </div>
        </div>

        <div className="flex gap-2">
          <button type="submit" className="btn-primary">{t('calls.search')}</button>
          <button type="button" className="btn-ghost" onClick={onClear}>{t('calls.clear')}</button>
        </div>
      </form>

      {openId && <CallDetails id={openId} onClose={() => setOpenId(null)} />}

      {results.isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : results.isError ? (
        <p className="notice-error">{t('calls.failed')}</p>
      ) : total === 0 ? (
        <div className="card card-body text-center">
          <p className="text-slate-300">{t('calls.empty')}</p>
          <p className="field-hint mt-1">{t('calls.emptyHint')}</p>
        </div>
      ) : (
        <div className="card overflow-x-auto">
          <div className="flex items-center justify-between px-4 py-3 text-sm text-slate-400">
            <span>{t('calls.count', { count: total })}</span>
            <Pager page={page} pages={pages} onPage={setPage} />
          </div>
          <table className="table">
            <thead>
              <tr>
                <th>{t('calls.columns.when')}</th>
                <th>{t('calls.columns.direction')}</th>
                <th>{t('calls.columns.customer')}</th>
                <th>{t('calls.columns.agent')}</th>
                <th>{t('calls.columns.status')}</th>
                <th>{t('calls.columns.type')}</th>
                <th>{t('calls.columns.branch')}</th>
                <th>{t('calls.columns.orderValue')}</th>
                <th>{t('calls.columns.duration')}</th>
                <th>{t('calls.columns.recording')}</th>
                <th>{t('calls.columns.notes')}</th>
              </tr>
            </thead>
            <tbody>
              {results.data!.rows.map((row) => (
                <Row key={row.id} row={row} open={row.id === openId} onOpen={() => {
                  setOpenId(row.id)
                  window.scrollTo?.({ top: 0, behavior: 'smooth' })
                }} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function Row({ row, open, onOpen }: { row: CallRow; open: boolean; onOpen: () => void }) {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')

  return (
    <tr
      onClick={onOpen}
      onKeyDown={(e) => e.key === 'Enter' && onOpen()}
      tabIndex={0}
      aria-selected={open}
      className={`cursor-pointer hover:bg-ink-800 ${open ? 'bg-ink-800' : ''}`}
    >
      <td className="whitespace-nowrap">{new Date(row.startedAt).toLocaleString(i18n.language)}</td>
      <td>{t(`calls.directions.${row.direction}`)}</td>
      <td>
        <div className="text-slate-200">{row.contactName ?? t('calls.unknownCaller')}</div>
        <div className="text-xs text-slate-500" dir="ltr">{row.remoteNumberRaw}</div>
      </td>
      <td>{row.agentDisplayName ?? '–'}</td>
      <td>{t(`history.statuses.${row.status}`, { defaultValue: row.status })}</td>
      <td>{row.typeName ? (arabic ? row.typeLabelAr : row.typeLabelEn) : ''}</td>
      <td>{row.branchName ?? ''}</td>
      <td className="tabular" dir="ltr">{row.orderValue === null ? '' : row.orderValue.toLocaleString(i18n.language)}</td>
      {/* Blank rather than 0:00 for a call never answered: a zero reads as a
          call that connected and was silent. */}
      <td className="tabular" dir="ltr">{row.durationSec === null ? '' : formatClock(row.durationSec)}</td>
      <td>
        {row.hasRecording ? (
          <span className="badge-ok">{t('calls.recording.yes')}</span>
        ) : row.recordingExpired ? (
          <span className="badge-muted">{t('calls.recording.expiredShort')}</span>
        ) : null}
      </td>
      <td className="max-w-[16rem] truncate text-slate-400" title={row.notes ?? undefined}>{row.notes}</td>
    </tr>
  )
}

function Select<T extends string>({
  label, value, onChange, children,
}: { label: string; value: T; onChange: (value: T) => void; children: ReactNode }) {
  const { t } = useTranslation()
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      <select className="input" value={value} onChange={(e) => onChange(e.target.value as T)}>
        <option value="">{t('calls.filter.any')}</option>
        {children}
      </select>
    </label>
  )
}

function Pager({ page, pages, onPage }: { page: number; pages: number; onPage: (page: number) => void }) {
  const { t } = useTranslation()
  return (
    <div className="flex items-center gap-2">
      <button type="button" className="btn-ghost btn-sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        {t('calls.previous')}
      </button>
      <span dir="ltr">{page} / {pages}</span>
      <button type="button" className="btn-ghost btn-sm" disabled={page >= pages} onClick={() => onPage(page + 1)}>
        {t('calls.next')}
      </button>
    </div>
  )
}
