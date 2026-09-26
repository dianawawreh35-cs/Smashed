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

const NO_PHONES = { live: false, problem: 'not_configured', agents: [] }

/**
 * Answers the phone-status poll (S-61) with `phones`, and hands every other
 * request to `rest`, so a test's own sequence of replies is not used up by it.
 */
function withPhones(rest: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>, phones: unknown = NO_PHONES) {
  return (input: RequestInfo | URL, init?: RequestInit) =>
    String(input).endsWith('/api/pbx/agents') ? Promise.resolve(jsonResponse(phones)) : rest(input, init)
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

  it('says why phone status is missing', async () => {
    vi.stubGlobal('fetch', withPhones(vi.fn().mockResolvedValue(jsonResponse([TALKING]))))

    renderPage()

    expect(await screen.findByText(/needs the server's PBX extension/)).toBeInTheDocument()
  })
})
