import { Fragment, useState } from 'react'
import type { FormEvent } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { findContactByPhone, searchContacts } from '../api/contacts'
import type { ContactSummary } from '../api/contacts'
import { listBranches } from '../api/delivery'
import { createMistake, deleteMistake, exportMistakes, searchMistakes, updateMistake } from '../api/mistakes'
import type { Mistake, MistakeFilters, Responsible } from '../api/mistakes'
import { errorCodeOf, listUsers } from '../api/users'
import ConfirmButton from '../components/ConfirmButton'
import LoadError from '../components/LoadError'
import { FilterMultiSelect, FilterSelect as Select, Pager } from '../components/SearchControls'
import { looksLikeNumber, ltr, searchDir } from '../lib/bidi'
import { downloadBlob } from '../lib/csv'
import { formatMoney } from '../lib/money'
import { localDate } from '../lib/reportFilters'
import { noSelectOnDoubleClick } from '../lib/rows'
import { useDebounced } from '../lib/useDebounced'

const PAGE_SIZE = 50

/** The table's columns, counting the one for the buttons. */
const COLUMNS = 9

/** The filters as typed, turned into filters only on Search. The lists are the ids ticked; none is all. */
interface Draft {
  q: string
  from: string
  to: string
  branchId: string[]
  responsible: '' | Responsible
  agentId: string[]
  /** '' is both. */
  compensated: '' | 'true' | 'false'
}

/** Today in both date boxes, as every date filter in the app opens (Dia, 1 Oct 2026). */
function todayDraft(): Draft {
  const today = localDate(new Date())
  return { q: '', from: today, to: today, branchId: [], responsible: '', agentId: [], compensated: '' }
}

function toFilters(d: Draft): MistakeFilters {
  const text = (v: string) => (v.trim() ? v.trim() : undefined)
  const list = (v: string[]) => (v.length > 0 ? v : undefined)
  return {
    q: text(d.q),
    from: text(d.from),
    to: text(d.to),
    branchId: list(d.branchId),
    responsible: d.responsible || undefined,
    agentId: list(d.agentId),
    compensated: d.compensated === '' ? undefined : d.compensated === 'true',
  }
}

/** A day the server sent (yyyy-mm-dd) in the reader's language, without a time zone moving it. */
function formatDay(day: string, language: string): string {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(y, m - 1, d).toLocaleDateString(language)
}

/**
 * The mistakes made by the branches and the agents (S-65).
 *
 * **Compensated or not** (تم التعويض; Dia, 3 Oct 2026) is a tick on the
 * mistake, a column in the table and a filter: the customer has been made
 * good for it. A new mistake starts unticked.
 *
 * **Every mistake has a branch**, the one where it happened. It is then put
 * down to the branch as a whole, naming no agent, or to one agent. The value
 * and the customer are optional; a customer is typed as a phone number and
 * found the way the pop-up finds a caller, and a number nobody has on file is
 * kept as typed (Dia, 1 Oct).
 *
 * **The server filters, pages, counts and exports**, as on the Calls page: the
 * count, the total value and the file are every match, not the page on screen
 * (20 Sep, "filtering a page lies"). A mistake opens for correcting under its
 * own row, and Remove asks twice (M-W07).
 */
export default function MistakesPage() {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const [draft, setDraft] = useState<Draft>(todayDraft)
  const [filters, setFilters] = useState<MistakeFilters>(() => toFilters(todayDraft()))
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<Mistake | 'new' | null>(null)
  const [exporting, setExporting] = useState<'idle' | 'busy' | 'failed'>('idle')

  const results = useQuery({
    queryKey: ['mistakes', filters, page],
    queryFn: () => searchMistakes(filters, page, PAGE_SIZE),
    placeholderData: keepPreviousData,
  })

  const users = useQuery({ queryKey: ['users'], queryFn: listUsers })
  const branches = useQuery({ queryKey: ['branches'], queryFn: listBranches })
  const agents = (users.data ?? []).filter((u) => u.role === 'Agent')

  const set = <K extends keyof Draft>(key: K) => (value: Draft[K]) => setDraft((d) => ({ ...d, [key]: value }))

  function onSearch(event: FormEvent) {
    event.preventDefault()
    setFilters(toFilters(draft))
    setPage(1)
  }

  function onClear() {
    const fresh = todayDraft()
    setDraft(fresh)
    setFilters(toFilters(fresh))
    setPage(1)
  }

  async function onExport() {
    setExporting('busy')
    try {
      const blob = await exportMistakes(filters, arabic ? 'ar' : 'en')
      downloadBlob(`mistakes-${localDate(new Date())}`, blob)
      setExporting('idle')
    } catch {
      setExporting('failed')
    }
  }

  const total = results.data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="page-title">{t('mistakes.heading')}</h1>
          <p className="page-subtitle">{t('mistakes.intro')}</p>
        </div>
        <button type="button" className="btn-primary" onClick={() => setEditing('new')}>
          {t('mistakes.add')}
        </button>
      </div>

      {/* A new mistake has no row to sit under, so it opens here, beside the
          button that asked for it. A correction opens under its own row. */}
      {editing === 'new' && <MistakeForm mistake={null} onClose={() => setEditing(null)} />}

      <form onSubmit={onSearch} className="card card-body space-y-4" aria-label={t('mistakes.filters')}>
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
          <label className="field lg:col-span-2">
            <span className="field-label">{t('mistakes.filter.q')}</span>
            <input className="input" value={draft.q} dir={searchDir(draft.q)} onChange={(e) => set('q')(e.target.value)} />
          </label>
          <label className="field">
            <span className="field-label">{t('mistakes.filter.from')}</span>
            <input type="date" className="input" value={draft.from} onChange={(e) => set('from')(e.target.value)} />
          </label>
          <label className="field">
            <span className="field-label">{t('mistakes.filter.to')}</span>
            <input type="date" className="input" value={draft.to} onChange={(e) => set('to')(e.target.value)} />
          </label>

          <FilterMultiSelect label={t('mistakes.columns.branch')} values={draft.branchId} onChange={set('branchId')}
            choices={branches}
            options={(branches.data ?? []).map((b) => ({ value: b.id, label: b.name }))} />
          <Select label={t('mistakes.columns.responsible')} value={draft.responsible} onChange={set('responsible')}>
            <option value="Branch">{t('mistakes.responsibleIs.Branch')}</option>
            <option value="Agent">{t('mistakes.responsibleIs.Agent')}</option>
          </Select>
          <FilterMultiSelect label={t('mistakes.columns.agent')} values={draft.agentId} onChange={set('agentId')}
            choices={users}
            options={agents.map((u) => ({
              value: u.id,
              label: `${u.displayName}${u.isActive ? '' : ` ${t('mistakes.inactive')}`}`,
            }))} />
          <Select label={t('mistakes.columns.compensated')} value={draft.compensated} onChange={set('compensated')}>
            <option value="true">{t('mistakes.compensatedIs.true')}</option>
            <option value="false">{t('mistakes.compensatedIs.false')}</option>
          </Select>
        </div>

        <div className="flex gap-2">
          <button type="submit" className="btn-primary">{t('mistakes.search')}</button>
          <button type="button" className="btn-ghost" onClick={onClear}>{t('mistakes.clear')}</button>
        </div>
      </form>

      {results.isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : results.isError ? (
        <LoadError message={t('mistakes.failed')} onRetry={() => void results.refetch()} busy={results.isFetching} />
      ) : total === 0 ? (
        <div className="card card-body text-center">
          <p className="text-slate-300">{t('mistakes.empty')}</p>
          <p className="field-hint mt-1">{t('mistakes.emptyHint')}</p>
        </div>
      ) : (
        <div
          className={`card table-scroll transition ${results.isPlaceholderData ? 'opacity-60' : ''}`}
          aria-busy={results.isPlaceholderData}
        >
          <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 text-sm text-slate-400">
            <span>
              {t('mistakes.count', { count: total })}
              {' · '}
              {t('mistakes.totalValue', { value: ltr(formatMoney(results.data!.totalValue, i18n.language)) })}
            </span>
            <div className="flex items-center gap-3">
              {exporting === 'failed' && <span className="text-red-300">{t('mistakes.exportFailed')}</span>}
              <button type="button" className="btn-ghost btn-sm" onClick={onExport} disabled={exporting === 'busy'}
                title={t('mistakes.exportHint')}>
                {exporting === 'busy' ? t('mistakes.exporting') : t('mistakes.export', { count: total })}
              </button>
              <Pager page={page} pages={pages} onPage={setPage} />
            </div>
          </div>
          <MistakeTable
            rows={results.data!.rows}
            editing={editing === 'new' ? null : editing}
            onEdit={setEditing}
            onCloseEdit={() => setEditing(null)}
          />
        </div>
      )}
    </div>
  )
}

function MistakeTable({
  rows, editing, onEdit, onCloseEdit,
}: {
  rows: Mistake[]
  editing: Mistake | null
  onEdit: (mistake: Mistake) => void
  onCloseEdit: () => void
}) {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)

  const remove = useMutation({
    meta: { toast: 'deleted' },
    mutationFn: deleteMistake,
    onMutate: () => setError(null),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['mistakes'] }),
    onError: (e) => setError(t(`mistakes.errors.${errorCodeOf(e)}`, { defaultValue: t('mistakes.errors.server_error') })),
  })

  return (
    <>
      {error && <div role="alert" className="notice-error m-3">{error}</div>}
      <table className="table">
        <thead>
          <tr>
            <th>{t('mistakes.columns.date')}</th>
            <th>{t('mistakes.columns.branch')}</th>
            <th>{t('mistakes.columns.responsible')}</th>
            <th>{t('mistakes.columns.agent')}</th>
            <th>{t('mistakes.columns.customer')}</th>
            <th>{t('mistakes.columns.value')}</th>
            <th>{t('mistakes.columns.compensated')}</th>
            <th>{t('mistakes.columns.notes')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <Fragment key={row.id}>
              {/* Double-click is a shortcut, never the only way in: Edit does the same. */}
              <tr
                onDoubleClick={() => onEdit(row)}
                onMouseDown={noSelectOnDoubleClick}
                className={`hover:bg-ink-800/60 ${editing?.id === row.id ? 'bg-ink-800' : ''}`}
              >
                <td className="whitespace-nowrap">{formatDay(row.occurredOn, i18n.language)}</td>
                <td>{row.branchName}</td>
                <td>
                  <span className={row.responsible === 'Agent' ? 'badge-warn' : 'badge-muted'}>
                    {t(`mistakes.responsibleIs.${row.responsible}`)}
                  </span>
                </td>
                <td>{row.agentDisplayName ?? ''}</td>
                <td>
                  {row.customerNumber && (
                    <>
                      <div className={row.contactId ? 'text-slate-200' : 'text-slate-500'}>
                        {row.contactId ? (row.contactName ?? t('mistakes.customerUnnamed')) : t('mistakes.notOnFile')}
                      </div>
                      <div className="text-xs text-slate-500" dir="ltr">{row.customerNumber}</div>
                    </>
                  )}
                </td>
                <td className="tabular" dir="ltr">{row.value === null ? '' : formatMoney(row.value, i18n.language)}</td>
                <td className="whitespace-nowrap">
                  <span className={row.compensated ? 'badge-ok' : 'badge-muted'}>
                    {t(`mistakes.compensatedIs.${row.compensated}`)}
                  </span>
                </td>
                <td className="max-w-[18rem] whitespace-pre-line text-slate-300">{row.notes}</td>
                {/* The double-click stops here, so two quick clicks on Remove
                    do not also open the editor. */}
                <td className="text-end whitespace-nowrap" onDoubleClick={(e) => e.stopPropagation()}>
                  <button type="button" className="btn-ghost btn-sm" onClick={() => onEdit(row)}>
                    {t('mistakes.edit')}
                  </button>
                  <ConfirmButton label={t('mistakes.remove')} onConfirm={() => remove.mutate(row.id)}
                    disabled={remove.isPending} />
                </td>
              </tr>
              {editing?.id === row.id && (
                <tr>
                  <td colSpan={COLUMNS} className="row-panel">
                    <div className="w-0 min-w-full">
                      <MistakeForm mistake={row} onClose={onCloseEdit} />
                    </div>
                  </td>
                </tr>
              )}
            </Fragment>
          ))}
        </tbody>
      </table>
    </>
  )
}

/**
 * Records a mistake, or corrects one. Choosing "The branch" empties and closes
 * the agent box: a branch's mistake names no agent, and the server refuses one.
 * A disabled branch or agent is not offered, except the one an old mistake
 * already has, so correcting its notes does not force a new choice.
 */
function MistakeForm({ mistake, onClose }: { mistake: Mistake | null; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const users = useQuery({ queryKey: ['users'], queryFn: listUsers })
  const branches = useQuery({ queryKey: ['branches'], queryFn: listBranches })

  const today = localDate(new Date())
  const [occurredOn, setOccurredOn] = useState(mistake?.occurredOn ?? today)
  const [branchId, setBranchId] = useState(mistake?.branchId ?? '')
  const [responsible, setResponsible] = useState<Responsible>(mistake?.responsible ?? 'Agent')
  const [agentId, setAgentId] = useState(mistake?.agentId ?? '')
  const [value, setValue] = useState(mistake?.value === null || mistake === null ? '' : String(mistake.value))
  const [number, setNumber] = useState(mistake?.customerNumber ?? '')
  const [notes, setNotes] = useState(mistake?.notes ?? '')
  const [compensated, setCompensated] = useState(mistake?.compensated ?? false)
  const [error, setError] = useState<string | null>(null)

  const branchChoices = (branches.data ?? []).map((b) => ({ id: b.id, name: b.name, active: true }))
  if (mistake && !branchChoices.some((b) => b.id === mistake.branchId)) {
    branchChoices.push({ id: mistake.branchId, name: mistake.branchName, active: false })
  }

  const agentChoices = (users.data ?? [])
    .filter((u) => u.role === 'Agent' && (u.isActive || u.id === mistake?.agentId))
    .map((u) => ({ id: u.id, name: u.displayName, active: u.isActive }))

  // The customer box takes a number or a name (Dia, 1 Oct). A number is looked
  // up as it is typed, so the supervisor sees whose it is before saving; the
  // server finds it again on save, so this is only what the screen shows. A
  // name lists the saved customers who have it, and picking one puts their
  // number in the box: the mistake is always kept by number.
  const lookedUp = useDebounced(number.trim())
  const isNumber = lookedUp === '' || looksLikeNumber(lookedUp)
  const digits = isNumber ? lookedUp.replace(/\D/g, '') : ''
  const customer = useQuery({
    queryKey: ['contacts', 'by-phone', lookedUp],
    queryFn: () => findContactByPhone(lookedUp),
    enabled: isNumber && digits.length >= 3,
  })
  const byName = useQuery({
    queryKey: ['contacts', 'search', lookedUp],
    queryFn: () => searchContacts(lookedUp),
    enabled: !isNumber && lookedUp.length >= 2,
  })
  const typedName = number.trim() !== '' && !looksLikeNumber(number)

  const save = useMutation({
    meta: { toast: 'saved' },
    mutationFn: () => {
      const request = {
        occurredOn,
        branchId,
        responsible,
        agentId: responsible === 'Agent' ? agentId : null,
        value: value.trim() === '' ? null : Number(value),
        customerNumber: number.trim() || null,
        notes: notes.trim(),
        compensated,
      }
      return mistake ? updateMistake(mistake.id, request) : createMistake(request)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['mistakes'] })
      onClose()
    },
    onError: (e) => setError(t(`mistakes.errors.${errorCodeOf(e)}`, { defaultValue: t('mistakes.errors.invalid_request') })),
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    save.mutate()
  }

  function chooseResponsible(next: Responsible) {
    setResponsible(next)
    if (next === 'Branch') setAgentId('')
  }

  const valueOk = value.trim() === '' || (!Number.isNaN(Number(value)) && Number(value) >= 0)
  // A name left in the customer box has not been turned into a customer yet.
  const canSave = occurredOn !== '' && branchId !== '' && notes.trim() !== '' && valueOk && !typedName
    && (responsible === 'Branch' || agentId !== '')

  return (
    <form onSubmit={submit} className="card card-body space-y-4">
      <h3 className="text-base font-semibold text-slate-100">
        {mistake ? t('mistakes.editHeading') : t('mistakes.addHeading')}
      </h3>

      {error && <div role="alert" className="notice-error">{error}</div>}

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        <label className="field">
          <span className="field-label">{t('mistakes.columns.date')}</span>
          <input type="date" className="input" value={occurredOn} max={today} onChange={(e) => setOccurredOn(e.target.value)} />
        </label>

        <label className="field">
          <span className="field-label">{t('mistakes.columns.branch')}</span>
          <select className="input" value={branchId} onChange={(e) => setBranchId(e.target.value)}>
            <option value="">{t('mistakes.chooseBranch')}</option>
            {branchChoices.map((b) => (
              <option key={b.id} value={b.id}>{b.name}{b.active ? '' : ` ${t('mistakes.inactive')}`}</option>
            ))}
          </select>
          {branches.isError && (
            <LoadError inline message={t('common.listFailed')} onRetry={() => void branches.refetch()} />
          )}
        </label>

        <fieldset className="field">
          <legend className="field-label">{t('mistakes.columns.responsible')}</legend>
          <div className="flex gap-4 py-2 text-sm text-slate-300">
            {(['Agent', 'Branch'] as const).map((r) => (
              <label key={r} className="flex items-center gap-2">
                <input type="radio" name="responsible" className="accent-brand-500" checked={responsible === r}
                  onChange={() => chooseResponsible(r)} />
                {t(`mistakes.responsibleIs.${r}`)}
              </label>
            ))}
          </div>
        </fieldset>

        <label className="field">
          <span className="field-label">{t('mistakes.columns.agent')}</span>
          <select className="input" value={agentId} disabled={responsible === 'Branch'}
            onChange={(e) => setAgentId(e.target.value)}>
            <option value="">{t('mistakes.chooseAgent')}</option>
            {agentChoices.map((a) => (
              <option key={a.id} value={a.id}>{a.name}{a.active ? '' : ` ${t('mistakes.inactive')}`}</option>
            ))}
          </select>
          {responsible === 'Branch' && <span className="field-hint">{t('mistakes.agentNone')}</span>}
          {users.isError && (
            <LoadError inline message={t('common.listFailed')} onRetry={() => void users.refetch()} />
          )}
        </label>

        {/* Compensated sits under the value it is about, in the same cell. */}
        <div className="field">
          <label className="field-label" htmlFor="mistake-value">{t('mistakes.columns.value')}</label>
          <input id="mistake-value" className="input tabular" inputMode="decimal" dir="ltr" value={value}
            onChange={(e) => setValue(e.target.value)} />
          <span className="field-hint">{t('mistakes.valueHint')}</span>
          <label className="mt-2 flex items-center gap-2 text-sm text-slate-300">
            <input type="checkbox" className="accent-brand-500" checked={compensated}
              onChange={(e) => setCompensated(e.target.checked)} />
            {t('mistakes.compensatedBox')}
          </label>
        </div>

        <div className="field">
          <label className="field-label" htmlFor="mistake-customer">{t('mistakes.customerNumber')}</label>
          <input id="mistake-customer" className="input" dir={searchDir(number)} value={number}
            onChange={(e) => setNumber(e.target.value)} />
          {typedName ? (
            <NameMatches pending={number.trim() !== lookedUp || byName.isFetching} failed={byName.isError}
              matches={byName.data} onPick={(phone) => setNumber(phone)} />
          ) : (
            <CustomerHint digits={digits} pending={number.trim() !== lookedUp || customer.isFetching}
              failed={customer.isError} found={customer.data} />
          )}
        </div>

        <label className="field md:col-span-2">
          <span className="field-label">{t('mistakes.columns.notes')}</span>
          <textarea className="input" rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} />
          <span className="field-hint">{t('mistakes.notesHint')}</span>
        </label>
      </div>

      <div className="flex gap-2">
        <button type="submit" className="btn-primary" disabled={save.isPending || !canSave}>{t('mistakes.save')}</button>
        <button type="button" className="btn-ghost" onClick={onClose}>{t('mistakes.cancel')}</button>
      </div>
    </form>
  )
}

/**
 * Under a name: the saved customers who have it, each with their main number.
 * Picking one puts that number in the box. Until then the form will not save,
 * because a name on its own is not a customer the mistake can be kept by.
 */
function NameMatches({
  pending, failed, matches, onPick,
}: {
  pending: boolean
  failed: boolean
  matches: ContactSummary[] | undefined
  onPick: (phone: string) => void
}) {
  const { t } = useTranslation()
  if (pending) return <span className="field-hint">{t('mistakes.customerLooking')}</span>
  if (failed) return <span className="field-hint text-red-300">{t('mistakes.customerSearchFailed')}</span>

  const withNumber = (matches ?? []).filter((c) => c.phones.length > 0)
  if (withNumber.length === 0) return <span className="field-hint">{t('mistakes.customerNoMatch')}</span>

  return (
    <div className="mt-1 space-y-1">
      <span className="field-hint">{t('mistakes.customerPick')}</span>
      <ul className="max-h-48 space-y-1 overflow-y-auto">
        {withNumber.map((c) => (
          <li key={c.id}>
            <button type="button" className="btn-ghost btn-sm w-full justify-between text-start"
              onClick={() => onPick(c.phones[0])}>
              <span>{c.name ?? t('mistakes.customerUnnamed')}</span>
              <span className="text-xs text-slate-500" dir="ltr">{c.phones[0]}</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}

/** Under the number: whose it is, that nobody has it on file, or nothing yet. */
function CustomerHint({
  digits, pending, failed, found,
}: {
  digits: string
  pending: boolean
  failed: boolean
  found: { name: string | null } | null | undefined
}) {
  const { t } = useTranslation()
  if (digits.length < 3) return <span className="field-hint">{t('mistakes.customerHint')}</span>
  if (pending) return <span className="field-hint">{t('mistakes.customerLooking')}</span>
  if (failed) return <span className="field-hint">{t('mistakes.customerLookupFailed')}</span>
  if (found) {
    return (
      <span className="field-hint text-slate-300">
        {found.name ? t('mistakes.customerFound', { name: found.name }) : t('mistakes.customerUnnamed')}
      </span>
    )
  }
  return <span className="field-hint">{t('mistakes.customerNotFound')}</span>
}
