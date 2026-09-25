import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

/**
 * The pieces a search page is built from, shared by the Calls and Applications
 * pages (S-02, A-70) so the two screens stay one screen to the eye.
 */

/** A filter drop-down whose first choice is "Any": blank means the filter is off. */
export function FilterSelect<T extends string>({
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
