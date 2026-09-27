import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from '../App'
import { ApiError } from '../api/client'
import { shouldRetry } from '../lib/queryClient'
import { AuthProvider } from './AuthProvider'
import { setToken } from './token'
import i18n from '../i18n'

/**
 * Sign-in for the supervisor app (S-01), against a stubbed `fetch` — these
 * check the browser side of the flow, not the API.
 */

const SUPERVISOR = {
  id: '11111111-1111-1111-1111-111111111111',
  login: 'supervisor',
  displayName: 'Rana',
  role: 'Supervisor',
}

const AGENT = { ...SUPERVISOR, login: 'sara', displayName: 'Sara', role: 'Agent' }

function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: String(status),
    headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response
}

function renderApp(path = '/login') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const result = render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider>
          <App />
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { ...result, queryClient }
}

/**
 * The dashboard and the reports are fetched on first opening (App.tsx). The
 * first import of one compiles the chart library, which in a busy test run
 * takes several seconds, so it is done once before these tests rather than
 * inside whichever test opens a report first; the pages then load as fast as
 * the others. The longer wait is a margin, not the expectation.
 */
const LAZY_PAGE = { timeout: 5_000 }

beforeAll(async () => {
  await Promise.all([import('../pages/DashboardPage'), import('../pages/CallReportsPage')])
}, 60_000)

const LOGGED_IN = { accessToken: 'token-123', expiresAt: '', user: SUPERVISOR, sessionId: 's1', extensions: null }

/** Fills the form and submits it. */
function signIn(login: string, password: string) {
  fireEvent.change(screen.getByLabelText('Username'), { target: { value: login } })
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } })
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))
}

beforeEach(async () => {
  setToken(null)
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('supervisor sign-in', () => {
  it('sends the credentials and lands on the dashboard', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ accessToken: 'token-123', expiresAt: '', user: SUPERVISOR, sessionId: null, extensions: null }),
    )
    vi.stubGlobal('fetch', fetchMock)

    renderApp()
    signIn('supervisor', 'TempPass!2026')

    expect(await screen.findByRole('heading', { name: 'Dashboard' }, LAZY_PAGE)).toBeInTheDocument()

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/auth/login')
    expect(JSON.parse(init.body)).toEqual({ login: 'supervisor', password: 'TempPass!2026' })
  })

  it('turns the server error code into a translated message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({ title: 'Sign-in failed', detail: 'nope', code: 'invalid_credentials' }, 401),
    ))

    renderApp()
    signIn('supervisor', 'wrong')

    expect(await screen.findByRole('alert')).toHaveTextContent('The username or password is not correct.')
  })

  it('tells a disabled account apart from a wrong password', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'account_disabled' }, 401)))

    renderApp()
    signIn('supervisor', 'TempPass!2026')

    expect(await screen.findByRole('alert')).toHaveTextContent(/disabled/i)
  })

  it('signs an agent in to the Agent App page and nothing else', async () => {
    // S-63: agents download the installer here. The menu offers them that
    // page alone, and the supervisor pages send them back to it.
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async (url: string) =>
      url === '/api/auth/login'
        ? jsonResponse({ accessToken: 'token-123', expiresAt: '', user: AGENT, sessionId: null, extensions: null })
        : jsonResponse({ code: 'no_installer' }, 404),
    ))

    renderApp()
    signIn('sara', 'whatever')

    expect(await screen.findByRole('heading', { name: 'Agent App' })).toBeInTheDocument()
    expect(await screen.findByText('No installer has been uploaded yet. Ask your supervisor.')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Dashboard' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Upload a new version' })).not.toBeInTheDocument()
  })

  it('sends an agent who opens a supervisor page to the Agent App page', async () => {
    setToken('token-123')
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async (url: string) =>
      url === '/api/auth/me' ? jsonResponse(AGENT) : jsonResponse({ code: 'no_installer' }, 404),
    ))

    renderApp('/users')

    expect(await screen.findByRole('heading', { name: 'Agent App' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Users' })).not.toBeInTheDocument()
  })

  it('says so when the server refuses for too many attempts', async () => {
    // The login's rate limit (F-12) answers 429, possibly with no body: not a
    // wrong password, and not "try again" at once.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(null, 429)))

    renderApp()
    signIn('supervisor', 'TempPass!2026')

    expect(await screen.findByRole('alert')).toHaveTextContent('Too many sign-in attempts. Wait a few minutes, then try again.')
  })

  it('says the server is unreachable rather than blaming the password', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    renderApp()
    signIn('supervisor', 'TempPass!2026')

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be reached/i)
  })
})

describe('route guard', () => {
  it('returns to the page asked for, query string and all', async () => {
    // A link to a report's tab used to land on its first tab after signing in.
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async (url: string) =>
      url === '/api/auth/login' ? jsonResponse(LOGGED_IN) : jsonResponse([]),
    ))

    renderApp('/call-reports?tab=abandoned')
    await screen.findByRole('heading', { name: 'Sign in' })
    signIn('supervisor', 'TempPass!2026')

    expect(await screen.findByRole('tab', { name: 'Abandoned', selected: true }, LAZY_PAGE)).toBeInTheDocument()
  })

  it('sends a signed-out visitor to the login screen', async () => {
    vi.stubGlobal('fetch', vi.fn())

    renderApp('/dashboard')

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('verifies a kept token against the server before showing anything', async () => {
    setToken('token-from-last-time')
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(SUPERVISOR))
    vi.stubGlobal('fetch', fetchMock)

    renderApp('/dashboard')

    expect(await screen.findByRole('heading', { name: 'Dashboard' }, LAZY_PAGE)).toBeInTheDocument()

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/auth/me')
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer token-from-last-time')
  })

  it('drops a token the server no longer accepts', async () => {
    setToken('stale-token')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({}, 401)))

    renderApp('/dashboard')

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
    await waitFor(() => expect(sessionStorage.getItem('callcenter.token')).toBeNull())
    // Nobody was working, so there is nothing to explain.
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('what a sign-in leaves behind', () => {
  it('empties the query cache on sign-out', async () => {
    // A shared browser: the next supervisor must not see the last one's data,
    // even for the moment before their own arrives.
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async (url: string) =>
      url === '/api/auth/login' ? jsonResponse(LOGGED_IN) : jsonResponse(url.includes('/logout') ? null : [], url.includes('/logout') ? 204 : 200),
    ))

    const { queryClient } = renderApp('/contacts')
    signIn('supervisor', 'TempPass!2026')
    await screen.findByRole('heading', { name: 'Contacts' })
    await waitFor(() => expect(queryClient.getQueryCache().getAll().length).toBeGreaterThan(0))

    fireEvent.click(screen.getByRole('button', { name: 'Log out' }))

    await screen.findByRole('heading', { name: 'Sign in' })
    await waitFor(() => expect(queryClient.getQueryCache().getAll()).toHaveLength(0))
  })

  it('empties it too when the server ends the sign-in, and asks nothing more', async () => {
    // The server session makes logout end the token (M-S01). A 401 must not
    // start a loop of requests: one refusal, then the login screen.
    setToken('token-before-the-reset')
    const fetchMock = vi.fn().mockImplementation(async (url: string) =>
      url === '/api/auth/me' ? jsonResponse(SUPERVISOR) : jsonResponse({}, 401),
    )
    vi.stubGlobal('fetch', fetchMock)

    const { queryClient } = renderApp('/contacts')
    await screen.findByRole('heading', { name: 'Sign in' })
    await waitFor(() => expect(queryClient.getQueryCache().getAll()).toHaveLength(0))

    const asked = fetchMock.mock.calls.length
    await new Promise((resolve) => setTimeout(resolve, 300))
    expect(fetchMock.mock.calls.length).toBe(asked)
  })

  it('does not retry a refusal, and retries a dropped connection once', () => {
    expect(shouldRetry(0, new ApiError(401, 'Unauthorized', null))).toBe(false)
    expect(shouldRetry(0, new ApiError(404, 'Not Found', null))).toBe(false)
    expect(shouldRetry(0, new ApiError(500, 'Server Error', null))).toBe(true)
    expect(shouldRetry(0, new TypeError('Failed to fetch'))).toBe(true)
    expect(shouldRetry(1, new TypeError('Failed to fetch'))).toBe(false)
  })
})

describe('when the server ends the sign-in (N-05)', () => {
  it('signs the supervisor out at the first refused request and says why', async () => {
    // A password reset, a disable or a role change, done elsewhere while this
    // supervisor was working: the token was good at /auth/me and is refused
    // from then on.
    setToken('token-before-the-reset')
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async (url: string) =>
      url === '/api/auth/me' ? jsonResponse(SUPERVISOR) : jsonResponse({}, 401),
    ))

    renderApp('/contacts')

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent(/signed out/i)
    expect(sessionStorage.getItem('callcenter.token')).toBeNull()
  })

  it('does not treat a wrong password at the login as the server ending a sign-in', async () => {
    // The login sends no token, so its 401 is only a wrong password.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'invalid_credentials' }, 401)))

    renderApp()
    signIn('supervisor', 'wrong')

    expect(await screen.findByRole('alert')).toHaveTextContent('The username or password is not correct.')
  })

  it('clears the message once the supervisor signs in again', async () => {
    setToken('token-before-the-reset')
    const fetchMock = vi.fn().mockImplementation(async (url: string) =>
      url === '/api/auth/me' ? jsonResponse(SUPERVISOR) : jsonResponse({}, 401),
    )
    vi.stubGlobal('fetch', fetchMock)

    renderApp('/contacts')
    expect(await screen.findByRole('alert')).toHaveTextContent(/signed out/i)

    fetchMock.mockImplementation(async (url: string) =>
      url === '/api/auth/login'
        ? jsonResponse({ accessToken: 'new-token', expiresAt: '', user: SUPERVISOR, sessionId: null, extensions: null })
        : jsonResponse([]),
    )
    signIn('supervisor', 'NewPass!2026')

    // Back to the page they were thrown out of, with the new token.
    await waitFor(() => expect(sessionStorage.getItem('callcenter.token')).toBe('new-token'))
    await waitFor(() => expect(screen.queryByText(/signed out/i)).not.toBeInTheDocument())
    expect(screen.queryByRole('heading', { name: 'Sign in' })).not.toBeInTheDocument()
  })
})
