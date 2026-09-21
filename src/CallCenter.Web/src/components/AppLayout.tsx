import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import LanguageSwitcher from './LanguageSwitcher'
import { useAuth } from '../auth/context'

/**
 * The shell around the signed-in supervisor pages.
 *
 * Navigation is a sidebar rather than links in the header: the SRS gives the
 * supervisor app seven areas (S-02 search, reports R-01 to R-21, contacts,
 * users, settings…), and a row of header links stops working somewhere around
 * five. A sidebar also keeps the current section visible, which a row of links
 * only manages with an underline nobody notices.
 *
 * It collapses to a horizontal strip on a narrow screen rather than hiding
 * behind a menu button: there are few enough sections to fit, and a supervisor
 * on a laptop should not need two taps to reach reports.
 */
export default function AppLayout() {
  const { t } = useTranslation()
  const { user, signOut } = useAuth()
  const navigate = useNavigate()

  async function onSignOut() {
    await signOut()
    navigate('/login', { replace: true })
  }

  const sections = [
    { to: '/dashboard', label: t('nav.dashboard') },
    { to: '/contacts', label: t('nav.contacts') },
    { to: '/delivery', label: t('nav.delivery') },
    { to: '/menu', label: t('nav.menu') },
    { to: '/users', label: t('nav.users') },
    { to: '/settings', label: t('nav.settings') },
  ]

  return (
    <div className="min-h-screen lg:flex">
      <aside
        className="border-ink-700 bg-ink-900 lg:min-h-screen lg:w-60 lg:shrink-0
                   lg:border-e border-b lg:border-b-0"
      >
        <div className="flex items-center gap-3 px-5 py-4">
          {/* The restaurant's mark. A letter rather than an image keeps the app
              working on a server with no internet and nothing to fetch. */}
          <span
            className="grid h-9 w-9 place-items-center rounded-lg bg-brand-50
                       font-bold text-brand-500"
            aria-hidden="true"
          >
            S
          </span>
          <div className="leading-tight">
            <p className="text-sm font-semibold text-slate-100">{t('app.title')}</p>
            <p className="text-xs text-slate-500">{t('app.subtitle')}</p>
          </div>
        </div>

        <nav className="flex gap-1 overflow-x-auto px-3 pb-3 lg:flex-col lg:overflow-visible">
          {sections.map((section) => (
            <NavLink
              key={section.to}
              to={section.to}
              className={({ isActive }) =>
                `nav-link whitespace-nowrap ${isActive ? 'nav-link-active' : ''}`
              }
            >
              {section.label}
            </NavLink>
          ))}
        </nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center justify-end gap-3 border-b border-ink-700 bg-ink-900 px-6 py-3">
          {/* Who is signed in, so a shared browser never leaves it in doubt. */}
          {user && (
            <span className="flex items-center gap-2 text-sm">
              <span
                className="grid h-7 w-7 place-items-center rounded-full bg-brand-50
                           text-xs font-semibold text-brand-500"
                aria-hidden="true"
              >
                {user.displayName.trim().charAt(0)}
              </span>
              <span className="text-slate-300">{user.displayName}</span>
            </span>
          )}

          <LanguageSwitcher />

          <button type="button" onClick={onSignOut} className="btn-quiet btn-sm">
            {t('nav.logout')}
          </button>
        </header>

        <main className="flex-1 px-6 py-6">
          <div className="mx-auto w-full max-w-6xl">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  )
}
