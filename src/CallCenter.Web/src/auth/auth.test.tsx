import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from '../App'
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
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider>
          <App />
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

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

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()

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

  it('refuses an agent account and points them at the desktop app', async () => {
    // The API authenticates anyone; this app is for supervisors (S-01, N-10).
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({ accessToken: 'token-123', expiresAt: '', user: AGENT, sessionId: null, extensions: null }),
    ))

    renderApp()
    signIn('sara', 'whatever')

    expect(await screen.findByRole('alert')).toHaveTextContent(/desktop Agent App/i)
    expect(screen.queryByRole('heading', { name: 'Dashboard' })).not.toBeInTheDocument()
  })

  it('says the server is unreachable rather than blaming the password', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    renderApp()
    signIn('supervisor', 'TempPass!2026')

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be reached/i)
  })
})

describe('route guard', () => {
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

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()

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
  })
})
