import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import UsersPage from './UsersPage'
import { isOlderVersion } from '../api/agentApp'
import { setToken } from '../auth/token'
import { AuthContext } from '../auth/context'
import type { AuthState } from '../auth/context'
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
  appVersion: null,
  appSignedInAt: null,
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

const NO_PHONES = { live: false, problem: 'not_configured', agents: [] }

/**
 * Answers the phone-status poll (S-61) with `phones` and the Agent App page's
 * installer with `installer` (none uploaded by default), and hands every
 * other request to `rest`, so a test's own sequence of replies is not used up.
 */
function withPhones(
  rest: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>,
  phones: unknown = NO_PHONES,
  installer: unknown = null,
) {
  return (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.endsWith('/api/pbx/agents')) return Promise.resolve(jsonResponse(phones))
    if (url.endsWith('/api/agent-app')) {
      return Promise.resolve(installer ? jsonResponse(installer) : jsonResponse({ code: 'no_installer' }, 404))
    }
    return rest(input, init)
  }
}

/** The supervisor signed in while the page is open. */
const ME = { id: 's1', login: 'boss', displayName: 'Boss', role: 'Supervisor' }

const SIGNED_IN: AuthState = {
  user: ME,
  isLoading: false,
  signIn: async () => {},
  signOut: async () => {},
  signedOutByServer: false,
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthContext.Provider value={SIGNED_IN}>
        <MemoryRouter>
          <UsersPage />
        </MemoryRouter>
      </AuthContext.Provider>
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
    vi.stubGlobal('fetch', withPhones(vi.fn().mockResolvedValue(jsonResponse([AGENT]))))

    renderPage()

    expect(await screen.findByText('Sara')).toBeInTheDocument()
    expect(screen.getByText('Not set')).toBeInTheDocument()
  })

  it('never shows a stored SIP password, only that one is set', async () => {
    // N-05: there is no endpoint that returns a secret, and the screen must not
    // imply otherwise - a supervisor replaces it rather than reading it.
    const configured = { ...AGENT, extension: '2001', hasSipCredentials: true, canTakeCalls: true }
    vi.stubGlobal('fetch', withPhones(vi.fn().mockResolvedValue(jsonResponse([configured]))))

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
    vi.stubGlobal('fetch', withPhones(fetchMock))

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
    vi.stubGlobal('fetch', withPhones(fetchMock))

    renderPage()
    await screen.findByText('Sara')

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Omar' } })
    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'sara' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'longenough' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('That username is already in use.')
  })

  it('keeps what was typed when the server refuses a new account (M-W06)', async () => {
    // The form used to empty itself on Add, before the server had answered.
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([AGENT]))
      .mockResolvedValueOnce(jsonResponse({ code: 'login_taken' }, 409))
    vi.stubGlobal('fetch', withPhones(fetchMock))

    renderPage()
    await screen.findByText('Sara')

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Omar' } })
    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'sara' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'longenough' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))

    await screen.findByRole('alert')
    expect(screen.getByLabelText('Name')).toHaveValue('Omar')
    expect(screen.getByLabelText('Username')).toHaveValue('sara')
    expect(screen.getByLabelText('Password')).toHaveValue('longenough')
  })

  it('clears the form once the account exists', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([AGENT]))
      .mockResolvedValueOnce(jsonResponse({ ...AGENT, id: 'a9', login: 'omar', displayName: 'Omar' }))
      .mockResolvedValue(jsonResponse([AGENT]))
    vi.stubGlobal('fetch', withPhones(fetchMock))

    renderPage()
    await screen.findByText('Sara')

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Omar' } })
    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'omar' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'longenough' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))

    await waitFor(() => expect(screen.getByLabelText('Username')).toHaveValue(''))
  })

  it('opens a row’s form on the same dark panel as the other lists (M-W02)', async () => {
    vi.stubGlobal('fetch', withPhones(vi.fn().mockResolvedValue(jsonResponse([AGENT]))))

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Reset password' }))

    expect(screen.getByLabelText('New password').closest('td')).toHaveClass('row-panel')
  })
})

describe('Agent App versions', () => {
  const OFFERED = {
    version: '0.8.2',
    fileName: 'SmashedAgentApp-Setup-0.8.2.exe',
    sizeBytes: 1,
    uploadedAt: '2026-10-01T20:00:00Z',
    uploadedBy: 'boss',
    zipFileName: null,
    zipSizeBytes: null,
  }
  const OLD = { ...AGENT, id: 'a1', displayName: 'Sara', appVersion: '0.8.1', appSignedInAt: '2026-10-01T17:00:00Z' }
  const CURRENT = { ...AGENT, id: 'a2', login: 'omar', displayName: 'Omar', appVersion: '0.8.2', appSignedInAt: '2026-10-01T18:00:00Z' }

  it('shows each version and marks the one behind the Agent App page', async () => {
    vi.stubGlobal('fetch', withPhones(vi.fn().mockResolvedValue(jsonResponse([OLD, CURRENT])), NO_PHONES, OFFERED))

    renderPage()

    expect(await screen.findByText('0.8.1')).toBeInTheDocument()
    expect(screen.getAllByText('0.8.2')).toHaveLength(1)
    expect(await screen.findByText('Update to 0.8.2')).toBeInTheDocument()
    expect(screen.getAllByText(/^Update to/)).toHaveLength(1)
  })

  it('marks nobody when no version has been uploaded', async () => {
    vi.stubGlobal('fetch', withPhones(vi.fn().mockResolvedValue(jsonResponse([OLD]))))

    renderPage()

    expect(await screen.findByText('0.8.1')).toBeInTheDocument()
    expect(screen.queryByText(/^Update to/)).not.toBeInTheDocument()
  })

  it('compares number by number', () => {
    expect(isOlderVersion('0.8.9', '0.8.10')).toBe(true)
    expect(isOlderVersion('0.8.2', '0.8.2')).toBe(false)
    expect(isOlderVersion('0.9.0', '0.8.10')).toBe(false)
    expect(isOlderVersion('test', '0.8.2')).toBe(false)
  })
})

describe('phones and listening in (S-61, S-62)', () => {
  const TALKING = { ...AGENT, id: 'a1', displayName: 'Sara', extension: '2001', hasSipCredentials: true, canTakeCalls: true }
  const OFF = { ...AGENT, id: 'a2', login: 'omar', displayName: 'Omar', extension: '2002', hasSipCredentials: true, canTakeCalls: true }
  const PHONES = {
    live: true,
    problem: null,
    agents: [
      { userId: 'a1', displayName: 'Sara', extension: '2001', state: 'InCall', since: new Date().toISOString() },
      { userId: 'a2', displayName: 'Omar', extension: '2002', state: 'Offline', since: null },
    ],
  }

  /** Web Audio is not in jsdom; the page only needs something to hand the sound to. */
  class SilentAudio {
    currentTime = 0
    destination = {}
    createBuffer(_channels: number, length: number) {
      return { duration: length / 8000, getChannelData: () => new Float32Array(length) }
    }
    createBufferSource() {
      return { buffer: null, connect: () => undefined, start: () => undefined }
    }
    close() {
      return Promise.resolve()
    }
  }

  /** A listen-in the server keeps open: one chunk of sound, then nothing until it is stopped. */
  function listening() {
    let sent = false
    return {
      ok: true,
      status: 200,
      statusText: 'OK',
      headers: new Headers({ 'content-type': 'application/octet-stream', 'X-Listen-Id': 'L1' }),
      body: {
        getReader: () => ({
          read: () =>
            sent ? new Promise(() => undefined) : ((sent = true), Promise.resolve({ done: false, value: new Uint8Array(320) })),
        }),
      },
    } as unknown as Response
  }

  it('shows each agent’s phone, and Listen only for one in a call', async () => {
    vi.stubGlobal('fetch', withPhones(vi.fn().mockResolvedValue(jsonResponse([TALKING, OFF])), PHONES))

    renderPage()

    expect(await screen.findByText(/In a call/)).toBeInTheDocument()
    expect(screen.getByText('Offline')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Listen' })).toHaveLength(1)
  })

  it('listens through the server and Stop ends it', async () => {
    vi.stubGlobal('AudioContext', SilentAudio)
    const rest = vi.fn((input: RequestInfo | URL) =>
      Promise.resolve(String(input).endsWith('/api/pbx/agents/a1/listen') ? listening() : jsonResponse([TALKING, OFF])),
    )
    vi.stubGlobal('fetch', withPhones(rest, PHONES))

    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Listen' }))

    expect(await screen.findByText('Listening to Sara')).toBeInTheDocument()
    expect(rest).toHaveBeenCalledWith('/api/pbx/agents/a1/listen', expect.objectContaining({ method: 'GET' }))

    fireEvent.click(screen.getAllByRole('button', { name: 'Stop listening' })[0])

    await waitFor(() =>
      expect(rest).toHaveBeenCalledWith('/api/pbx/agents/listen/L1', expect.objectContaining({ method: 'DELETE' })),
    )
    expect(screen.queryByText('Listening to Sara')).not.toBeInTheDocument()
  })

  it('asks for nothing but the new password on somebody else’s account', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([AGENT]))
      .mockResolvedValueOnce(jsonResponse(null, 204))
      .mockResolvedValue(jsonResponse([AGENT]))
    vi.stubGlobal('fetch', withPhones(fetchMock))

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Reset password' }))

    expect(screen.queryByLabelText('Your current password')).not.toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('New password'), { target: { value: 'a new password' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThan(1))
    const [url, init] = fetchMock.mock.calls[1]
    expect(url).toBe('/api/users/a1/password')
    expect(JSON.parse(init.body)).toEqual({ newPassword: 'a new password' })
  })

  it('asks for the current password on your own account, and sends it', async () => {
    // The server refuses your own change without it (current_password_required).
    const mine = { ...AGENT, id: ME.id, login: ME.login, displayName: ME.displayName, role: 'Supervisor' }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([mine]))
      .mockResolvedValueOnce(jsonResponse(null, 204))
      .mockResolvedValue(jsonResponse([mine]))
    vi.stubGlobal('fetch', withPhones(fetchMock))

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Reset password' }))

    fireEvent.change(screen.getByLabelText('New password'), { target: { value: 'a new password' } })
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
    expect(screen.getByText(/You will be signed out/)).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Your current password'), { target: { value: 'the old one' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThan(1))
    const [url, init] = fetchMock.mock.calls[1]
    expect(url).toBe('/api/users/s1/password')
    expect(JSON.parse(init.body)).toEqual({ newPassword: 'a new password', currentPassword: 'the old one' })
  })

  it('says so when your current password is wrong', async () => {
    const mine = { ...AGENT, id: ME.id, login: ME.login, displayName: ME.displayName, role: 'Supervisor' }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([mine]))
      .mockResolvedValueOnce(jsonResponse({ code: 'current_password_wrong' }, 403))
    vi.stubGlobal('fetch', withPhones(fetchMock))

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Reset password' }))
    fireEvent.change(screen.getByLabelText('Your current password'), { target: { value: 'not it' } })
    fireEvent.change(screen.getByLabelText('New password'), { target: { value: 'a new password' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Your current password is not correct.')
  })

  it('says why phone status is missing', async () => {
    vi.stubGlobal('fetch', withPhones(vi.fn().mockResolvedValue(jsonResponse([TALKING]))))

    renderPage()

    expect(await screen.findByText(/needs the server's PBX extension/)).toBeInTheDocument()
  })
})
