import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/context'
import { tokenExpiresAt } from '../auth/token'

/** How long before the end the warning shows. */
export const WARN_BEFORE_MS = 10 * 60_000

/**
 * A warning ten minutes before the sign-in runs out (S-72, N-05). The token
 * lasts a fixed 12 hours from sign-in, and when it ends the next request
 * sends the supervisor to the login page, with whatever they were typing
 * lost. This says when, so they can save first, and offers to sign in again
 * now, at a moment of their choosing.
 *
 * It does not extend the sign-in: that needs a refresh endpoint the server
 * does not have yet (DECISIONS, Open items).
 */
export default function SessionExpiryNotice() {
  const { t, i18n } = useTranslation()
  const { user, signOut } = useAuth()
  const navigate = useNavigate()
  const [now, setNow] = useState(() => Date.now())

  // Read again whenever the user changes: a new sign-in is a new token.
  const expiresAt = user ? tokenExpiresAt() : null

  useEffect(() => {
    if (expiresAt === null) return
    // Once a minute is plenty for a ten-minute warning, and costs nothing.
    const timer = setInterval(() => setNow(Date.now()), 60_000)
    return () => clearInterval(timer)
  }, [expiresAt])

  if (expiresAt === null || expiresAt - now > WARN_BEFORE_MS || expiresAt <= now) return null

  const time = new Date(expiresAt).toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' })

  async function signInAgain() {
    await signOut()
    navigate('/login', { replace: true })
  }

  return (
    <div role="alert" className="notice-warning no-print mb-4 flex flex-wrap items-center justify-between gap-3">
      <span>{t('session.endsSoon', { time })}</span>
      <button type="button" className="btn-ghost btn-sm" onClick={() => void signInAgain()}>
        {t('session.signInAgain')}
      </button>
    </div>
  )
}
