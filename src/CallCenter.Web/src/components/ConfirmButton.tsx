import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

/** How long "Confirm remove" waits for the second click before it goes back. */
const ARMED_MS = 4_000

/**
 * A Remove that asks twice (M-W07): the first click turns the button red and
 * says "Confirm remove", the second one removes. It goes back by itself after
 * a few seconds, on Escape, or when focus leaves it, so a stray click that was
 * never followed up cannot be completed by accident later. A double-click
 * does not count as the second click: its second press is ignored, so one
 * hurried double-click only arms it.
 *
 * The second click rather than a dialog (Dia, 27 Sep): the rows these sit on
 * are edited in place, and a box over the page would be the only one in the
 * app. The delivery areas, the menu items and the menu categories all use
 * this, so Remove behaves the same on each.
 */
export default function ConfirmButton({
  label,
  onConfirm,
  disabled = false,
}: {
  /** The resting label, e.g. "Remove". */
  label: string
  onConfirm: () => void
  disabled?: boolean
}) {
  const { t } = useTranslation()
  const [armed, setArmed] = useState(false)

  useEffect(() => {
    if (!armed) return
    const timer = window.setTimeout(() => setArmed(false), ARMED_MS)
    return () => window.clearTimeout(timer)
  }, [armed])

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={(event) => {
        if (event.detail > 1) return
        if (armed) {
          setArmed(false)
          onConfirm()
        } else {
          setArmed(true)
        }
      }}
      onKeyDown={(e) => {
        if (e.key === 'Escape') setArmed(false)
      }}
      onBlur={() => setArmed(false)}
      className={armed ? 'btn-danger btn-sm' : 'btn-ghost btn-sm'}
    >
      {armed ? t('common.confirmRemove') : label}
    </button>
  )
}
