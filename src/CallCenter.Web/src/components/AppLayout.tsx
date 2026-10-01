import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import LanguageSwitcher from './LanguageSwitcher'
import PageBoundary from './PageBoundary'
import { UserRoles } from '../api/auth'
import { useAuth } from '../auth/context'

/**
 * The shell around the signed-in pages: all of them for a supervisor, and the
 * Agent App download alone for an agent (S-63).
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
  const location = useLocation()

  async function onSignOut() {
    await signOut()
    navigate('/login', { replace: true })
  }

  // An agent signed in here has one page, the Agent App download (S-63).
  const agentSections = [{ to: '/agent-app', label: t('nav.agentApp') }]

  const supervisorSections = [
    { to: '/dashboard', label: t('nav.dashboard') },
    { to: '/calls', label: t('nav.calls') },
    // The call reports right under Calls, as the application reports sit
    // under Applications (Dia, 25 Sep).
    { to: '/call-reports', label: t('nav.callReports') },
    // Applications, and their reports, beside Calls: the same conversations by
    // another route (A-70). Their own pages, so Calls stays calls only.
    { to: '/applications', label: t('nav.applications') },
    { to: '/application-reports', label: t('nav.applicationReports') },
    { to: '/contacts', label: t('nav.contacts') },
    // What went wrong at a branch or with an agent, and who is responsible (S-65).
    { to: '/mistakes', label: t('nav.mistakes') },
    // Their report right under them, as the call reports sit under Calls (R-23).
    { to: '/mistake-reports', label: t('nav.mistakeReports') },
    // Who is on break now, and the agents' breaks on any day (A-86, S-66, R-22).
    { to: '/breaks', label: t('nav.breaks') },
    { to: '/delivery', label: t('nav.delivery') },
    { to: '/menu', label: t('nav.menu') },
    { to: '/classification', label: t('nav.classification') },
    { to: '/users', label: t('nav.users') },
    { to: '/settings', label: t('nav.settings') },
    // What each Agent App laptop logged, errors first (N-12).
    { to: '/logs', label: t('nav.logs') },
    // Where a supervisor uploads a new version for the laptops (S-63).
    { to: '/agent-app', label: t('nav.agentApp') },
  ]

  const sections = user?.role === UserRoles.Supervisor ? supervisorSections : agentSections

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

        <nav aria-label={t('nav.label')} className="flex gap-1 overflow-x-auto px-3 pb-3 lg:flex-col lg:overflow-visible">
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
            {/* Keyed on the page, so a page that failed to load does not
                stay failed when the supervisor goes to another. */}
            <PageBoundary key={location.pathname}>
              <Outlet />
            </PageBoundary>
          </div>
        </main>
      </div>
    </div>
  )
}
