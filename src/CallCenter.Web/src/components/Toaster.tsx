import { useTranslation } from 'react-i18next'
import { dismissToast, useToasts } from '../lib/toast'

/**
 * Where the "Saved." and "Deleted." notices appear (S-72): the bottom corner on
 * the end side, so in Arabic the bottom left, away from the menu. A polite
 * live region, so a screen reader says it without interrupting.
 */
export default function Toaster() {
  const { t } = useTranslation()
  const toasts = useToasts()

  return (
    <div role="status" aria-live="polite"
         className="no-print pointer-events-none fixed bottom-4 end-4 z-50 flex flex-col items-end gap-2">
      {toasts.map((toast) => (
        <div key={toast.id}
             className="pointer-events-auto flex animate-fade-in items-center gap-3 rounded-md border
                        border-emerald-500/60 bg-ink-900 px-4 py-2.5 text-sm text-emerald-300 shadow-raised">
          <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"
               strokeLinejoin="round" className="h-4 w-4 shrink-0" aria-hidden="true">
            <path d="M4 10.5l4 4 8-9" />
          </svg>
          <span>{t(`toast.${toast.kind}`)}</span>
          <button type="button" onClick={() => dismissToast(toast.id)}
                  className="text-slate-400 hover:text-slate-200" aria-label={t('toast.dismiss')}>
            <span aria-hidden="true">×</span>
          </button>
        </div>
      ))}
    </div>
  )
}
