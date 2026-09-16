import { Link, Outlet, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import LanguageSwitcher from './LanguageSwitcher'
import { useAuth } from '../auth/context'

/** Shell around the authenticated supervisor pages. */
export default function AppLayout() {
  const { t } = useTranslation()
  const { user, signOut } = useAuth()
  const navigate = useNavigate()

  async function onSignOut() {
    await signOut()
    navigate('/login', { replace: true })
  }

  return (
    <div className="min-h-screen flex flex-col">
      <header className="bg-white border-b border-slate-200">
        <div className="mx-auto max-w-7xl px-4 py-3 flex items-center justify-between gap-4">
          <div>
            <h1 className="text-lg font-semibold">{t('app.title')}</h1>
            <p className="text-xs text-slate-500">{t('app.subtitle')}</p>
          </div>

          <nav className="flex items-center gap-4 text-sm">
            <Link to="/dashboard" className="text-slate-600 hover:text-slate-900">
              {t('nav.dashboard')}
            </Link>
            <Link to="/users" className="text-slate-600 hover:text-slate-900">
              {t('nav.users')}
            </Link>
            <Link to="/settings" className="text-slate-600 hover:text-slate-900">
              {t('nav.settings')}
            </Link>
          </nav>

          <div className="flex items-center gap-3">
            {/* Who is signed in, so a shared browser never leaves it in doubt. */}
            {user && <span className="text-sm text-slate-600">{user.displayName}</span>}
            <LanguageSwitcher />
            <button
              type="button"
              onClick={onSignOut}
              className="rounded border border-slate-300 px-3 py-1.5 text-sm hover:bg-slate-50"
            >
              {t('nav.logout')}
            </button>
          </div>
        </div>
      </header>

      <main className="flex-1 mx-auto w-full max-w-7xl px-4 py-6">
        <Outlet />
      </main>
    </div>
  )
}
