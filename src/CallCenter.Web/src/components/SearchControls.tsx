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
