import { Fragment, useState } from 'react'
import type { FormEvent } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { searchCalls, startOfDay, startOfNextDay } from '../api/calls'
import type { CallFilters, CallRow } from '../api/calls'
import { listChannels } from '../api/channels'
import { listClassificationTypes } from '../api/classifications'
import { listBranches } from '../api/delivery'
import { listUsers } from '../api/users'
import CallDetails from '../components/CallDetails'
import { FilterSelect as Select, Pager } from '../components/SearchControls'
import { noSelectOnDoubleClick } from '../lib/rows'

const PAGE_SIZE = 50

/** The form as typed: every value a string, turned into filters only on Search. */
interface Draft {
  q: string
  agentId: string
  branchId: string
  channelId: string
  typeId: string
  from: string
  to: string
  notes: string
  minOrder: string
  maxOrder: string
  classified: '' | 'yes' | 'no'
}

const EMPTY: Draft = {
  q: '', agentId: '', branchId: '', channelId: '', typeId: '',
  from: '', to: '', notes: '', minOrder: '', maxOrder: '', classified: '',
}

function toFilters(d: Draft): CallFilters {
  const text = (v: string) => (v.trim() ? v.trim() : undefined)
  const amount = (v: string) => (v.trim() && !Number.isNaN(Number(v)) ? Number(v) : undefined)
  const yesNo = (v: '' | 'yes' | 'no') => (v === '' ? undefined : v === 'yes')

  return {
    // Messages only (A-70): the same search as the Calls page, over the other kind.
    kind: 'App',
    q: text(d.q),
    agentId: text(d.agentId),
    branchId: text(d.branchId),
    channelId: text(d.channelId),
    typeId: text(d.typeId),
    // Days in the supervisor's own time zone, sent as instants: from the start
    // of the first day to the start of the day after the last.
    from: d.from ? startOfDay(d.from) : undefined,
    to: d.to ? startOfNextDay(d.to) : undefined,
    notes: text(d.notes),
    minOrder: amount(d.minOrder),
    maxOrder: amount(d.maxOrder),
    classified: yesNo(d.classified),
  }
}

/**
 * Every message, searchable, and any one of them opened in full (A-70): the
 * conversations that reached the restaurant on WhatsApp, Facebook, Instagram
 * or Wheels rather than by phone. Its own page beside Calls, which stays
 * calls only; the same table underneath, so the two pages are one search
 * with a different `kind`.
 *
 * Everything the Calls page learnt applies: the server filters and pages,
 * the filters apply on Search, and a message opens under its own row without
 * scrolling the page. What a message does not have — a result, a direction, a
 * recording — is not offered as a filter or a column; the channel is.
 */
export default function ApplicationsPage() {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const [draft, setDraft] = useState<Draft>(EMPTY)
  const [filters, setFilters] = useState<CallFilters>({ kind: 'App' })
  const [page, setPage] = useState(1)
  const [openId, setOpenId] = useState<string | null>(null)

  const results = useQuery({
    queryKey: ['calls', 'search', filters, page],
    queryFn: () => searchCalls(filters, page, PAGE_SIZE),
    placeholderData: keepPreviousData,
  })

  const agents = useQuery({ queryKey: ['users'], queryFn: listUsers })
  const branches = useQuery({ queryKey: ['branches'], queryFn: listBranches })
  // Hidden channels too: a message on a channel since hidden is still a message.
  const channels = useQuery({ queryKey: ['channels', 'all'], queryFn: () => listChannels(true) })
  const types = useQuery({ queryKey: ['classification', 'types'], queryFn: listClassificationTypes })

  const set = <K extends keyof Draft>(key: K) => (value: Draft[K]) => setDraft((d) => ({ ...d, [key]: value }))

  function onSearch(event: FormEvent) {
    event.preventDefault()
    setFilters(toFilters(draft))
    setPage(1)
  }

  function onClear() {
    setDraft(EMPTY)
    setFilters({ kind: 'App' })
    setPage(1)
  }

  const total = results.data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="space-y-6">
      <div>
        <h2 className="page-title">{t('applications.heading')}</h2>
        <p className="page-subtitle">{t('applications.intro')}</p>
      </div>

      <form onSubmit={onSearch} className="card card-body space-y-4" aria-label={t('applications.filters')}>
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

          <Select label={t('applications.columns.channel')} value={draft.channelId} onChange={set('channelId')}>
            {(channels.data ?? []).filter((c) => !c.isSystem).map((c) => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </Select>
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

          <label className="field lg:col-span-2">
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
          <Select label={t('calls.filter.classified')} value={draft.classified} onChange={set('classified')}>
            <option value="yes">{t('common.yes')}</option>
            <option value="no">{t('common.no')}</option>
          </Select>
        </div>

        <div className="flex gap-2">
          <button type="submit" className="btn-primary">{t('calls.search')}</button>
          <button type="button" className="btn-ghost" onClick={onClear}>{t('calls.clear')}</button>
        </div>
      </form>

      {results.isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : results.isError ? (
        <p className="notice-error">{t('applications.failed')}</p>
      ) : total === 0 ? (
        <div className="card card-body text-center">
          <p className="text-slate-300">{t('applications.empty')}</p>
          <p className="field-hint mt-1">{t('applications.emptyHint')}</p>
        </div>
      ) : (
        <div className="card overflow-x-auto">
          <div className="flex items-center justify-between px-4 py-3 text-sm text-slate-400">
            <span>{t('applications.count', { count: total })}</span>
            <Pager page={page} pages={pages} onPage={setPage} />
          </div>
          <table className="table">
            <thead>
              <tr>
                <th>{t('calls.columns.when')}</th>
                <th>{t('applications.columns.channel')}</th>
                <th>{t('calls.columns.customer')}</th>
                <th>{t('calls.columns.agent')}</th>
                <th>{t('calls.columns.type')}</th>
                <th>{t('calls.columns.branch')}</th>
                <th>{t('calls.columns.orderValue')}</th>
                <th>{t('calls.columns.notes')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {results.data!.rows.map((row) => (
                <Fragment key={row.id}>
                  <Row
                    row={row}
                    open={row.id === openId}
                    onToggle={() => setOpenId(row.id === openId ? null : row.id)}
                  />
                  {/* Where the supervisor is already looking, not at the top of
                      a list they have scrolled past. */}
                  {row.id === openId && (
                    <tr>
                      <td colSpan={COLUMNS} className="bg-ink-950/60 p-3">
                        {/* w-0 min-w-full: as wide as the table, and never
                            wider, so opening a message cannot make every
                            column jump. */}
                        <div className="w-0 min-w-full">
                          <CallDetails row={row} onClose={() => setOpenId(null)} />
                        </div>
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/** The table's columns, counting the one for the Open button. */
const COLUMNS = 9

function Row({ row, open, onToggle }: { row: CallRow; open: boolean; onToggle: () => void }) {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')

  return (
    // Double-click is a shortcut, never the only way in: the Open button does
    // the same, and is what a keyboard reaches.
    <tr
      onDoubleClick={onToggle}
      onMouseDown={noSelectOnDoubleClick}
      className={`hover:bg-ink-800/60 ${open ? 'bg-ink-800' : ''}`}
    >
      <td className="whitespace-nowrap">{new Date(row.startedAt).toLocaleString(i18n.language)}</td>
      <td>{row.channelName ?? ''}</td>
      <td>
        <div className="text-slate-200">{row.contactName ?? t('calls.unknownCaller')}</div>
        <div className="text-xs text-slate-500" dir="ltr">{row.remoteNumberRaw}</div>
      </td>
      <td>{row.agentDisplayName ?? '–'}</td>
      <td>{row.typeName ? (arabic ? row.typeLabelAr : row.typeLabelEn) : ''}</td>
      <td>{row.branchName ?? ''}</td>
      <td className="tabular" dir="ltr">{row.orderValue === null ? '' : row.orderValue.toLocaleString(i18n.language)}</td>
      <td className="max-w-[16rem] truncate text-slate-400" title={row.notes ?? undefined}>{row.notes}</td>
      {/* The double-click stops here, so a quick double press of the button
          does not open the message and close it again. */}
      <td className="text-end" onDoubleClick={(e) => e.stopPropagation()}>
        <button type="button" className="btn-ghost btn-sm" aria-expanded={open} onClick={onToggle}>
          {open ? t('calls.details.close') : t('calls.open')}
        </button>
      </td>
    </tr>
  )
}
