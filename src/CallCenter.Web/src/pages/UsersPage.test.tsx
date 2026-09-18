import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import UsersPage from './UsersPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/** Accounts and extensions (S-42), against a stubbed `fetch`. */

const AGENT = {
  id: 'a1',
  login: 'sara',
  displayName: 'Sara',
  role: 'Agent',
  isActive: true,
  extension: null,
  hasSipCredentials: false,
  canTakeCalls: false,
  createdAt: '2026-09-16T10:00:00Z',
  lastLoginAt: null,
}

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

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <UsersPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('users page', () => {
  it('lists accounts and shows when extensions are missing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([AGENT])))

    renderPage()

    expect(await screen.findByText('Sara')).toBeInTheDocument()
    expect(screen.getByText('Not set')).toBeInTheDocument()
  })

  it('never shows a stored SIP password, only that one is set', async () => {
    // N-05: there is no endpoint that returns a secret, and the screen must not
    // imply otherwise - a supervisor replaces it rather than reading it.
    const configured = { ...AGENT, extension: '2001', hasSipCredentials: true, canTakeCalls: true }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([configured])))

    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Set extension' }))

    const secret = screen.getByLabelText('SIP password') as HTMLInputElement
    expect(secret.value).toBe('')
    expect(secret.placeholder).toMatch(/leave blank to keep/i)
  })

  it('posts the extension and its secret to the single-extension endpoint', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([AGENT]))
      .mockResolvedValueOnce(jsonResponse({ ...AGENT, extension: '2001' }))
      .mockResolvedValue(jsonResponse([AGENT]))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Set extension' }))

    fireEvent.change(screen.getByLabelText('Extension'), { target: { value: '2001' } })
    fireEvent.change(screen.getByLabelText('SIP password'), { target: { value: 'sip-1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThan(1))

    const [url, init] = fetchMock.mock.calls[1]
    expect(url).toBe('/api/users/a1/extension')
    expect(JSON.parse(init.body)).toEqual({ extension: '2001', secret: 'sip-1' })
  })

  it('translates a refusal from the server', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([AGENT]))
      .mockResolvedValueOnce(jsonResponse({ code: 'login_taken' }, 409))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await screen.findByText('Sara')

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Omar' } })
    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'sara' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'longenough' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('That username is already in use.')
  })
})
