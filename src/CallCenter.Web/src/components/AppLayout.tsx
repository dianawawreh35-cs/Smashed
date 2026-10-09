import { useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import LanguageSwitcher from './LanguageSwitcher'
import ThemeSwitcher from './ThemeSwitcher'
import PageBoundary from './PageBoundary'
import NavIcon from './NavIcon'
import CustomerSearch from './CustomerSearch'
import LiveStatus from './LiveStatus'
import SessionExpiryNotice from './SessionExpiryNotice'
import Toaster from './Toaster'
import { UserRoles } from '../api/auth'
import { useAuth } from '../auth/context'

/** Whether the supervisor hid the menu, remembered in this browser. */
const MENU_HIDDEN_KEY = 'callcenter.menuHidden'

function storedMenuHidden(): boolean {
  try {
    return localStorage.getItem(MENU_HIDDEN_KEY) === '1'
  } catch {
    return false // private mode / blocked storage - the menu shows
  }
}

/**
 * The shell around the signed-in pages: all of them for a supervisor, and the
 * Agent App download alone for an agent (S-63).
 *
 * Navigation is a sidebar rather than links in the header: the SRS gives the
 * supervisor app seven areas (S-02 search, reports R-01 to R-21, contacts,
 * users, settings…), and a row of header links stops working somewhere around
 * five. A sidebar also keeps the current section visible, which a row of links
 * only manages with an underline nobody notices. Its sections are in headed
 * groups, each with an icon (S-70).
 *
 * It collapses to a horizontal strip on a narrow screen rather than hiding
 * behind a menu button: there are few enough sections to fit, and a supervisor
 * on a laptop should not need two taps to reach reports.
 *
 * On a wide screen the supervisor can hide it, for a report or the dashboard
 * that wants the whole width, with the button at the start of the header. The
 * choice is remembered in this browser, as the language is.
 *
 * The header finds a customer and shows the queue, calls and breaks right
 * now (S-71); the page area warns before the sign-in runs out and the corner
 * says when a change was saved (S-72).
 */
export default function AppLayout() {
  const { t } = useTranslation()
  const { user, signOut } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [menuHidden, setMenuHidden] = useState(storedMenuHidden)

  function toggleMenu() {
    const hidden = !menuHidden
    setMenuHidden(hidden)
    try {
      localStorage.setItem(MENU_HIDDEN_KEY, hidden ? '1' : '0')
    } catch {
      // ignore - the choice simply will not persist
    }
  }

  async function onSignOut() {
    await signOut()
    navigate('/login', { replace: true })
  }

  const isSupervisor = user?.role === UserRoles.Supervisor

  // An agent signed in here has one page, the Agent App download (S-63).
  const agentGroups: NavGroup[] = [
    { sections: [{ to: '/agent-app', label: t('nav.agentApp'), icon: 'agentApp' }] },
  ]

  // In groups by what the supervisor is doing (S-70): seventeen sections in
  // one column had grown past what the eye takes in. Each list keeps the
  // order it had, so a section is where it was within its group.
  const supervisorGroups: NavGroup[] = [
    {
      heading: t('nav.groups.today'),
      sections: [
        { to: '/dashboard', label: t('nav.dashboard'), icon: 'dashboard' },
        { to: '/calls', label: t('nav.calls'), icon: 'calls' },
        // Applications beside Calls: the same conversations by another route
        // (A-70). Their own page, so Calls stays calls only.
        { to: '/applications', label: t('nav.applications'), icon: 'applications' },
        { to: '/contacts', label: t('nav.contacts'), icon: 'contacts' },
        // Who is on break now, and the agents' breaks on any day (A-86, S-66, R-22).
        { to: '/breaks', label: t('nav.breaks'), icon: 'breaks' },
      ],
    },
    {
      // The three reports together, in the order of the pages they report on
      // (they used to sit under those pages, Dia 25 Sep).
      heading: t('nav.groups.reports'),
      sections: [
        { to: '/call-reports', label: t('nav.callReports'), icon: 'callReports' },
        { to: '/application-reports', label: t('nav.applicationReports'), icon: 'applicationReports' },
        { to: '/mistake-reports', label: t('nav.mistakeReports'), icon: 'mistakeReports' },
      ],
    },
    {
      // What went wrong at a branch or with an agent, and who is responsible (S-65).
      heading: t('nav.groups.quality'),
      sections: [{ to: '/mistakes', label: t('nav.mistakes'), icon: 'mistakes' }],
    },
    {
      heading: t('nav.groups.setup'),
      sections: [
        { to: '/delivery', label: t('nav.delivery'), icon: 'delivery' },
        { to: '/menu', label: t('nav.menu'), icon: 'menu' },
        { to: '/classification', label: t('nav.classification'), icon: 'classification' },
        // The tabs inside the Agent App, and their logins (A-88).
        { to: '/websites', label: t('nav.websites'), icon: 'websites' },
        { to: '/users', label: t('nav.users'), icon: 'users' },
        { to: '/settings', label: t('nav.settings'), icon: 'settings' },
      ],
    },
    {
      heading: t('nav.groups.system'),
      sections: [
        // What each Agent App laptop logged, errors first (N-12).
        { to: '/logs', label: t('nav.logs'), icon: 'logs' },
        // Where a supervisor uploads a new version for the laptops (S-63).
        { to: '/agent-app', label: t('nav.agentApp'), icon: 'agentApp' },
      ],
    },
  ]

  const groups = isSupervisor ? supervisorGroups : agentGroups

  return (
    <div className="min-h-screen lg:flex">
      <aside
        id="app-menu"
        className={`border-ink-700 bg-ink-900 lg:min-h-screen lg:w-60 lg:shrink-0
                    lg:border-e border-b lg:border-b-0 ${menuHidden ? 'lg:hidden' : ''}`}
      >
        <div className="flex items-center gap-3 px-5 py-4">
          {/* The restaurant's logo (S-70), the same picture as the browser
              tab's icon (tools/icons/make_icons.py), served by the app itself,
              so it shows with no internet. */}
          <img src="/apple-touch-icon.png" alt="" width={36} height={36} className="h-9 w-9 rounded-full" />
          <div className="leading-tight">
            <p className="text-sm font-semibold text-slate-100">{t('app.title')}</p>
            <p className="text-xs text-slate-500">{t('app.subtitle')}</p>
          </div>
        </div>

        <nav aria-label={t('nav.label')} className="flex gap-1 overflow-x-auto px-3 pb-3 lg:block lg:overflow-visible">
          {groups.map((group, i) => (
            // `contents` on a narrow screen, so every section runs on in the
            // one strip; a block with its heading on a wide one.
            <div key={i} role={group.heading ? 'group' : undefined} aria-labelledby={group.heading ? `nav-group-${i}` : undefined}
                 className="contents lg:block lg:space-y-0.5">
              {group.heading && (
                <p id={`nav-group-${i}`} className="nav-group hidden lg:block">{group.heading}</p>
              )}
              {group.sections.map((section) => (
                <NavLink
                  key={section.to}
                  to={section.to}
                  className={({ isActive }) =>
                    `nav-link whitespace-nowrap ${isActive ? 'nav-link-active' : ''}`
                  }
                >
                  <NavIcon name={section.icon} />
                  {section.label}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex flex-wrap items-center gap-3 border-b border-ink-700 bg-ink-900 px-6 py-3">
          {/* Wide screens only: on a narrow one the menu is a strip above the
              page and takes no width to give back. */}
          <button
            type="button"
            onClick={toggleMenu}
            aria-controls="app-menu"
            aria-expanded={!menuHidden}
            title={menuHidden ? t('nav.showMenu') : t('nav.hideMenu')}
            className="btn-quiet btn-sm hidden lg:inline-flex"
          >
            <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.75"
                 strokeLinecap="round" className="h-4 w-4" aria-hidden="true">
              <path d="M3 5h14M3 10h14M3 15h14" />
            </svg>
            <span className="sr-only">{menuHidden ? t('nav.showMenu') : t('nav.hideMenu')}</span>
          </button>

          {/* Find a customer, and the queue, calls and breaks at a glance
              (S-71). A supervisor's alone: an agent's account may ask for none
              of it. */}
          {isSupervisor && <CustomerSearch />}
          {isSupervisor && <LiveStatus />}

          {/* Who is signed in, so a shared browser never leaves it in doubt. */}
          {user && (
            <span className="ms-auto flex items-center gap-2 text-sm">
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
          <ThemeSwitcher />

          <button type="button" onClick={onSignOut} className="btn-quiet btn-sm">
            {t('nav.logout')}
          </button>
        </header>

        {/* As wide as a large screen allows (S-70): the lists and reports
            had wrapped and scrolled sideways at 1152 px with the screen's
            edges empty. The forms keep their own narrower widths. */}
        <main className="flex-1 px-6 py-6">
          <div className="mx-auto w-full max-w-screen-2xl">
            <SessionExpiryNotice />
            {/* Keyed on the page, so a page that failed to load does not
                stay failed when the supervisor goes to another. */}
            <PageBoundary key={location.pathname}>
              <Outlet />
            </PageBoundary>
          </div>
        </main>
      </div>

      <Toaster />
    </div>
  )
}

interface NavGroup {
  /** None for an agent's one section. */
  heading?: string
  sections: { to: string; label: string; icon: string }[]
}
