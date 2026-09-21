import { Fragment, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  createDeliveryArea,
  deleteDeliveryArea,
  importDeliveryAreas,
  listBranches,
  searchDeliveryAreas,
  updateDeliveryArea,
} from '../api/delivery'
import type { DeliveryArea, ImportResult } from '../api/delivery'
import { errorCodeOf } from '../api/users'

/**
 * Delivery areas: where the restaurant delivers, from which branch, at what
 * price (S-58).
 *
 * The paste box is the point of this screen, not an extra. The lists run to
 * 166 rows for Nablus alone and live in the branches' own spreadsheets — a
 * form that takes them one at a time is a form the supervisor stops using, and
 * then the agents quote last year's prices.
 */
export default function DeliveryPage() {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [branchId, setBranchId] = useState('')
  const [editing, setEditing] = useState<DeliveryArea | 'new' | null>(null)
  const [importing, setImporting] = useState(false)

  const { data: branches } = useQuery({ queryKey: ['branches'], queryFn: listBranches })

  const { data: areas, isLoading } = useQuery({
    queryKey: ['delivery-areas', query, branchId],
    queryFn: () => searchDeliveryAreas(query, branchId || undefined),
  })

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h2 className="page-title">{t('delivery.heading')}</h2>
          <p className="page-subtitle">{t('delivery.intro')}</p>
        </div>
        <div className="flex gap-2">
          <button type="button" onClick={() => setImporting(true)} className="btn-ghost">
            {t('delivery.importButton')}
          </button>
          <button type="button" onClick={() => setEditing('new')} className="btn-primary">
            {t('delivery.add')}
          </button>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <input
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={t('delivery.searchPlaceholder')}
          aria-label={t('delivery.search')}
          className="input max-w-md"
        />

        <select
          value={branchId}
          onChange={(e) => setBranchId(e.target.value)}
          aria-label={t('delivery.branch')}
          className="input max-w-xs"
        >
          <option value="">{t('delivery.allBranches')}</option>
          {branches?.map((branch) => (
            <option key={branch.id} value={branch.id}>
              {branch.name}
            </option>
          ))}
        </select>

        {areas && <span className="text-sm text-slate-400">{t('delivery.count', { count: areas.length })}</span>}
      </div>

      {importing && <ImportPanel onClose={() => setImporting(false)} />}

      {/* A new area has no row to sit under, so it opens here, beside the
          button that asked for it. An edit opens beside its own row. */}
      {editing === 'new' && <AreaForm area={null} onClose={() => setEditing(null)} />}

      {isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : areas && areas.length > 0 ? (
        <AreaTable
          areas={areas}
          editing={editing === 'new' ? null : editing}
          onEdit={setEditing}
          onCloseEdit={() => setEditing(null)}
        />
      ) : (
        <div className="card card-body text-center">
          <p className="text-slate-300">{query ? t('delivery.noMatches') : t('delivery.empty')}</p>
        </div>
      )}
    </div>
  )
}

function AreaTable({
  areas,
  editing,
  onEdit,
  onCloseEdit,
}: {
  areas: DeliveryArea[]
  /** The area being edited, so its form can open under its own row. */
  editing: DeliveryArea | null
  onEdit: (area: DeliveryArea) => void
  onCloseEdit: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const remove = useMutation({
    mutationFn: deleteDeliveryArea,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['delivery-areas'] }),
  })

  return (
    <div className="card overflow-x-auto">
      <table className="table">
        <thead>
          <tr>
            <th>{t('delivery.area')}</th>
            <th>{t('delivery.branch')}</th>
            <th>{t('delivery.price')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {areas.map((area) => (
            <Fragment key={area.id}>
            {/* Double-click opens the editor, as it does on the contacts list.
                The Edit button stays: a double-click is a shortcut, never the
                only way in. */}
            <tr
              onDoubleClick={() => onEdit(area)}
              className={editing?.id === area.id ? 'bg-ink-800/40' : undefined}
            >
              <td className="font-medium text-slate-100">
                {area.name}
                {!area.isActive && (
                  <span className="badge-muted ms-2">{t('delivery.inactive')}</span>
                )}
              </td>
              <td className="text-slate-400">{area.branchName}</td>
              {/* Zero is a real price, so it is shown as a figure rather than
                  blanked — and labelled, because a bare 0 reads as missing. */}
              <td className="tabular text-slate-300">
                {area.price === 0 ? t('delivery.free') : area.price}
              </td>
              {/* The double-click must not reach here: two quick clicks on
                  Remove would delete the row and then open an editor for it. */}
              <td className="text-end whitespace-nowrap" onDoubleClick={(e) => e.stopPropagation()}>
                <button type="button" onClick={() => onEdit(area)} className="btn-ghost btn-sm">
                  {t('delivery.edit')}
                </button>
                <button
                  type="button"
                  onClick={() => remove.mutate(area.id)}
                  disabled={remove.isPending}
                  className="btn-ghost btn-sm"
                >
                  {t('delivery.remove')}
                </button>
              </td>
            </tr>

            {/* The editor, where the supervisor is already looking rather than
                at the top of a list of 228 areas they have scrolled past. */}
            {editing?.id === area.id && (
              <tr>
                <td colSpan={4} className="bg-ink-900/60">
                  <AreaForm area={area} onClose={onCloseEdit} />
                </td>
              </tr>
            )}
            </Fragment>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function AreaForm({ area, onClose }: { area: DeliveryArea | null; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const { data: branches } = useQuery({ queryKey: ['branches'], queryFn: listBranches })

  const [name, setName] = useState(area?.name ?? '')
  const [branchId, setBranchId] = useState(area?.branchId ?? '')
  const [price, setPrice] = useState(String(area?.price ?? ''))
  const [isActive, setIsActive] = useState(area?.isActive ?? true)
  const [error, setError] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: () => {
      const request = { name: name.trim(), branchId, price: Number(price), isActive }
      return area ? updateDeliveryArea(area.id, request) : createDeliveryArea(request)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['delivery-areas'] })
      onClose()
    },
    onError: (e) => setError(t(`delivery.errors.${errorCodeOf(e)}`)),
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    save.mutate()
  }

  // Price may be "0", which is valid, so this checks for a number rather than
  // for truthiness.
  const canSave = name.trim().length > 0 && branchId.length > 0 && price.trim() !== '' && Number(price) >= 0

  return (
    <form onSubmit={submit} className="card card-body space-y-4">
      <h3 className="text-base font-semibold text-slate-100">
        {area ? t('delivery.editHeading') : t('delivery.addHeading')}
      </h3>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      <div className="grid gap-3 sm:grid-cols-3 max-w-3xl">
        <label className="field">
          <span className="field-label">{t('delivery.area')}</span>
          <input value={name} onChange={(e) => setName(e.target.value)} className="input" />
        </label>

        <label className="field">
          <span className="field-label">{t('delivery.branch')}</span>
          <select value={branchId} onChange={(e) => setBranchId(e.target.value)} className="input">
            <option value="">{t('delivery.chooseBranch')}</option>
            {branches?.map((branch) => (
              <option key={branch.id} value={branch.id}>
                {branch.name}
              </option>
            ))}
          </select>
        </label>

        <label className="field">
          <span className="field-label">{t('delivery.price')}</span>
          <input
            value={price}
            onChange={(e) => setPrice(e.target.value)}
            inputMode="decimal"
            className="input tabular"
          />
          <span className="field-hint">{t('delivery.priceHint')}</span>
        </label>
      </div>

      <label className="flex items-center gap-2 text-sm text-slate-300">
        <input
          type="checkbox"
          checked={isActive}
          onChange={(e) => setIsActive(e.target.checked)}
          className="accent-brand-500"
        />
        {t('delivery.activeLabel')}
      </label>

      <div className="flex gap-2">
        <button type="submit" disabled={save.isPending || !canSave} className="btn-primary">
          {t('delivery.save')}
        </button>
        <button type="button" onClick={onClose} className="btn-ghost">
          {t('delivery.cancel')}
        </button>
      </div>
    </form>
  )
}

/**
 * Paste a branch's whole list (S-58).
 *
 * Every rejected line comes back with its number and a reason, because "154 of
 * 168 added" leaves the supervisor to find the other fourteen themselves.
 */
function ImportPanel({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const { data: branches } = useQuery({ queryKey: ['branches'], queryFn: listBranches })

  const [branchId, setBranchId] = useState('')
  const [lines, setLines] = useState('')
  const [replace, setReplace] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)

  const run = useMutation({
    mutationFn: () => importDeliveryAreas(branchId, lines, replace),
    onSuccess: (imported) => {
      void queryClient.invalidateQueries({ queryKey: ['delivery-areas'] })
      setResult(imported)
      setLines('')
    },
    onError: (e) => setError(t(`delivery.errors.${errorCodeOf(e)}`)),
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setResult(null)
    run.mutate()
  }

  return (
    <form onSubmit={submit} className="card card-body space-y-4">
      <h3 className="text-base font-semibold text-slate-100">{t('delivery.importHeading')}</h3>
      <p className="field-hint">{t('delivery.importHint')}</p>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      {result && (
        <div role="status" className={result.problems.length > 0 ? 'notice-warning' : 'notice-success'}>
          <p className="font-medium">
            {t('delivery.importDone', {
              added: result.added,
              updated: result.updated,
              removed: result.removed,
            })}
          </p>

          {result.problems.length > 0 && (
            <>
              <p className="mt-2">{t('delivery.importProblems', { count: result.problems.length })}</p>
              <ul className="mt-1 space-y-0.5 text-xs">
                {result.problems.map((problem, index) => (
                  <li key={index}>
                    {problem.line > 0 && <span className="tabular me-2">{problem.line}</span>}
                    <span className="text-slate-300">{problem.text}</span>
                    <span className="ms-2">
                      {t(`delivery.importReasons.${problem.reason}`, { defaultValue: problem.reason })}
                    </span>
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}

      <label className="field max-w-xs">
        <span className="field-label">{t('delivery.branch')}</span>
        <select value={branchId} onChange={(e) => setBranchId(e.target.value)} className="input">
          <option value="">{t('delivery.chooseBranch')}</option>
          {branches?.map((branch) => (
            <option key={branch.id} value={branch.id}>
              {branch.name}
            </option>
          ))}
        </select>
      </label>

      <label className="field">
        <span className="field-label">{t('delivery.pasteLabel')}</span>
        <textarea
          value={lines}
          onChange={(e) => setLines(e.target.value)}
          rows={10}
          className="input font-mono text-xs"
          placeholder={t('delivery.pastePlaceholder')}
        />
      </label>

      {/* Destructive, so it is off by default and says what it does in full. */}
      <label className="flex items-start gap-2 text-sm text-slate-300">
        <input
          type="checkbox"
          checked={replace}
          onChange={(e) => setReplace(e.target.checked)}
          className="mt-1 accent-brand-500"
        />
        <span>
          {t('delivery.replaceLabel')}
          <span className="block field-hint">{t('delivery.replaceHint')}</span>
        </span>
      </label>

      <div className="flex gap-2">
        <button
          type="submit"
          disabled={run.isPending || !branchId || lines.trim().length === 0}
          className="btn-primary"
        >
          {t('delivery.importSubmit')}
        </button>
        <button type="button" onClick={onClose} className="btn-ghost">
          {t('delivery.cancel')}
        </button>
      </div>
    </form>
  )
}
