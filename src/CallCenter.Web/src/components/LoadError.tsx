import { useTranslation } from 'react-i18next'

/**
 * What a screen shows when the thing it asked the server for did not come
 * (M-W03): a failure that says so, and a Retry.
 *
 * Before this, a failed request mostly fell through to the same empty state
 * as "there is nothing", so a supervisor with the server stopped was told
 * there were no contacts, no users, no menu, and offered to flag a number
 * "nobody has". An empty list is an answer; this is the absence of one, and
 * the two must never look alike.
 *
 * `inline` is the small form, for a drop-down whose choices did not load: it
 * sits under the field instead of taking the place of a page.
 */
export default function LoadError({
  onRetry,
  message,
  busy = false,
  inline = false,
}: {
  onRetry: () => void
  /** What did not load, when the general sentence would leave it unclear. */
  message?: string
  /** A retry is on its way; the button waits for it. */
  busy?: boolean
  inline?: boolean
}) {
  const { t } = useTranslation()
  const text = message ?? t('common.loadFailed')

  if (inline) {
    return (
      <span role="alert" className="flex flex-wrap items-center gap-2 text-xs text-red-300">
        {text}
        <button type="button" className="font-medium underline hover:text-red-200" onClick={onRetry} disabled={busy}>
          {t('common.retry')}
        </button>
      </span>
    )
  }

  return (
    <div role="alert" className="notice-error flex flex-wrap items-center justify-between gap-3">
      <span>{text}</span>
      <button type="button" className="btn-ghost btn-sm" onClick={onRetry} disabled={busy}>
        {busy ? t('common.retrying') : t('common.retry')}
      </button>
    </div>
  )
}
