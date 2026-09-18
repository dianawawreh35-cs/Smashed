import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/context'

/**
 * Wraps the supervisor pages. Anyone not signed in is sent to the login screen,
 * with the page they asked for remembered so they land there afterwards.
 */
export default function RequireAuth() {
  const { user, isLoading } = useAuth()
  const location = useLocation()
  const { t } = useTranslation()

  // A token from a previous visit is still being checked. Showing the page now
  // would flash it before a redirect; showing the login form would be wrong if
  // the token turns out to be good.
  if (isLoading) {
    return (
      <div className="min-h-screen grid place-items-center text-slate-500">
        {t('app.loading')}
      </div>
    )
  }

  if (!user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  return <Outlet />
}
