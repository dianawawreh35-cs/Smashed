import { useEffect, useId, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import LoadError from './LoadError'

/**
 * The pieces a search page is built from, shared by the Calls and Applications
 * pages (S-02, A-70) so the two screens stay one screen to the eye.
 */

/** What a drop-down's choices came from, when they are fetched: enough to say it failed and try again. */
export interface ChoicesQuery {
  isError: boolean
  refetch: () => unknown
}

/**
 * A filter drop-down whose first choice is "Any": blank means the filter is off.
 *
 * Given the query its choices came from, it says so under the field when they
 * did not load (M-W03). Otherwise a failed list of agents is a drop-down
 * offering only "Any", which reads as there being no agents.
 */
export function FilterSelect<T extends string>({
  label, value, onChange, children, choices,
}: { label: string; value: T; onChange: (value: T) => void; children: ReactNode; choices?: ChoicesQuery }) {
  const { t } = useTranslation()
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      <select className="input" value={value} onChange={(e) => onChange(e.target.value as T)}>
        <option value="">{t('calls.filter.any')}</option>
        {children}
      </select>
      {choices?.isError && (
        <LoadError inline message={t('common.listFailed')} onRetry={() => void choices.refetch()} />
      )}
    </label>
  )
}

/** One choice in a {@link FilterMultiSelect}. */
export interface FilterOption {
  value: string
  label: string
}

/**
 * A filter drop-down that takes several choices (Dia, 2 Oct 2026): none ticked
 * is "Any", the filter off, and each one ticked widens the match — two agents
 * are either agent's calls. The ticks open in a list under the field; "Any" at
 * its top clears them. The field shows the names chosen, cut short when they
 * do not fit, with all of them in its tooltip.
 *
 * It closes on a click outside, on Escape and when focus leaves it, so Tab
 * moves on through the form as from a plain drop-down. A failed list of
 * choices says so under the field, as in {@link FilterSelect} (M-W03).
 */
export function FilterMultiSelect({
  label, values, onChange, options, choices,
}: {
  label: string
  values: string[]
  onChange: (values: string[]) => void
  options: FilterOption[]
  choices?: ChoicesQuery
}) {
  const { t, i18n } = useTranslation()
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const id = useId()

  useEffect(() => {
    if (!open) return
    const away = (e: MouseEvent) => {
      if (!box.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', away)
    return () => document.removeEventListener('mousedown', away)
  }, [open])

  const chosen = options.filter((o) => values.includes(o.value))
  const summary = chosen.length === 0
    ? t('calls.filter.any')
    : chosen.map((o) => o.label).join(i18n.language.startsWith('ar') ? '، ' : ', ')

  // Kept in the list's own order, not the order they were ticked in, so the
  // field, the printed heading and the query all name them the same way.
  const toggle = (value: string) =>
    onChange(options.map((o) => o.value).filter((v) => (v === value ? !values.includes(v) : values.includes(v))))

  return (
    <div
      ref={box}
      className="field relative"
      onKeyDown={(e) => {
        if (e.key === 'Escape' && open) {
          e.stopPropagation()
          setOpen(false)
          button.current?.focus()
        }
      }}
      onBlur={(e) => {
        if (!box.current?.contains(e.relatedTarget as Node | null)) setOpen(false)
      }}
    >
      <span className="field-label" id={`${id}-label`}>{label}</span>
      <button
        ref={button}
        id={`${id}-button`}
        type="button"
        className="input flex items-center gap-2 text-start"
        aria-labelledby={`${id}-label ${id}-button`}
        aria-haspopup="true"
        aria-expanded={open}
        title={chosen.length > 1 ? summary : undefined}
        onClick={() => setOpen((o) => !o)}
      >
        <span className="min-w-0 flex-1 truncate">{summary}</span>
        {chosen.length > 1 && (
          <span className="shrink-0 rounded-full bg-brand-50 px-1.5 text-xs text-brand-800" dir="ltr">
            {chosen.length}
          </span>
        )}
        <span className="shrink-0 text-slate-500" aria-hidden="true">▾</span>
      </button>
      {open && (
        <div
          role="group"
          aria-labelledby={`${id}-label`}
          className="absolute inset-x-0 top-full z-20 mt-1 max-h-64 overflow-y-auto rounded-md border border-ink-700 bg-ink-900 p-1 shadow-raised"
        >
          <label className="flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-sm text-slate-200 hover:bg-ink-800">
            <input type="checkbox" checked={values.length === 0} onChange={() => onChange([])} />
            {t('calls.filter.any')}
          </label>
          {options.map((o) => (
            <label key={o.value}
              className="flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-sm text-slate-200 hover:bg-ink-800">
              <input type="checkbox" checked={values.includes(o.value)} onChange={() => toggle(o.value)} />
              {o.label}
            </label>
          ))}
        </div>
      )}
      {choices?.isError && (
        <LoadError inline message={t('common.listFailed')} onRetry={() => void choices.refetch()} />
      )}
    </div>
  )
}

export function Pager({ page, pages, onPage }: { page: number; pages: number; onPage: (page: number) => void }) {
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
