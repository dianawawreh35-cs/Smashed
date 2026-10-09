import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import AppLayout from './AppLayout'
import Toaster from './Toaster'
import ContactsPage from '../pages/ContactsPage'
import { AuthContext } from '../auth/context'
import { setToken, tokenExpiresAt } from '../auth/token'
import { createQueryClient } from '../lib/queryClient'
import { showToast } from '../lib/toast'
import i18n from '../i18n'
import { SIGNED_IN, jsonResponse, renderWithClient, routes } from '../test/http'

/**
 * The shell around every page: the grouped menu (S-70), the header's search
 * and status (S-71), the sign-in warning and the Saved notice (S-72).
 */

const QUEUE = { isOpen: true, changedAt: null, changedBy: null, changedAutomatically: false, configured: true, autoOpenAt: null, autoOpenProblem: null }
const PHONES = {
  live: true,
  problem: null,
  agents: [
    { userId: 'a1', displayName: 'Sara', extension: '101', state: 'InCall', since: null },
    { userId: 'a2', displayName: 'Lina', extension: '102', state: 'Free', since: null },
  ],
}
const BREAKS = {
  asOf: '2026-10-09T10:00:00Z',
  dailyLimitMinutes: 60,
  agents: [
    { agentId: 'a1', agentDisplayName: 'Sara', state: 'Working', breakStartedAt: null, todaySeconds: 0, todayBreaks: 0, doNotDisturb: null, doNotDisturbSince: null },
    { agentId: 'a2', agentDisplayName: 'Lina', state: 'OnBreak', breakStartedAt: '2026-10-09T09:55:00Z', todaySeconds: 300, todayBreaks: 1, doNotDisturb: null, doNotDisturbSince: null },
  ],
}
const AHMAD = { id: 'c1', name: 'Ahmad', address: null, isVip: false, isBlocked: false, flagReason: null, phones: ['0599123456'] }

const server = () =>
  routes([
    ['/pbx/queue', () => jsonResponse(QUEUE)],
    ['/pbx/agents', () => jsonResponse(PHONES)],
    ['/breaks/monitor', () => jsonResponse(BREAKS)],
    ['/contacts', () => jsonResponse([AHMAD])],
  ])

function shell(path = '/dashboard') {
  return renderWithClient(
    <Routes>
      <Route element={<AppLayout />}>
        <Route path="/dashboard" element={<h1>Dashboard page</h1>} />
        <Route path="/contacts" element={<ContactsPage />} />
      </Route>
    </Routes>,
    path,
  )
}

/** A token that runs out at `ms`, as the server signs it (only `exp` is read). */
function tokenEnding(ms: number): string {
  const payload = btoa(JSON.stringify({ sub: 'supervisor-1', exp: Math.floor(ms / 1000) }))
  return `header.${payload.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')}.signature`
}

beforeEach(async () => {
  setToken(null)
  await i18n.changeLanguage('en')
  vi.stubGlobal('fetch', vi.fn(server()))
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('the menu', () => {
  it('puts the sections in headed groups', () => {
    shell()
    const reports = screen.getByRole('group', { name: 'Reports' })
    expect(within(reports).getAllByRole('link').map((l) => l.textContent)).toEqual([
      'Call reports',
      'Application reports',
      'Mistakes report',
    ])
    expect(within(screen.getByRole('group', { name: 'Day to day' })).getByRole('link', { name: 'Calls' })).toBeInTheDocument()
  })

  it('shows an agent the one section, with no search and no status', () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const agent = { ...SIGNED_IN, user: { id: 'a1', login: 'sara', displayName: 'Sara', role: 'Agent' } }
    render(
      <QueryClientProvider client={queryClient}>
        <AuthContext.Provider value={agent}>
          <MemoryRouter initialEntries={['/agent-app']}>
            <Routes>
              <Route element={<AppLayout />}>
                <Route path="/agent-app" element={<h1>Agent App page</h1>} />
              </Route>
            </Routes>
          </MemoryRouter>
        </AuthContext.Provider>
      </QueryClientProvider>,
    )
    expect(within(screen.getByRole('navigation')).getAllByRole('link').map((l) => l.textContent)).toEqual(['Agent App'])
    expect(screen.queryByRole('search')).not.toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })
})

describe('the header', () => {
  it('shows the queue, the agents on a call and on a break, each linked to its page', async () => {
    shell()
    const status = await screen.findByRole('list', { name: 'Right now' })
    expect(await within(status).findByRole('link', { name: 'Queue open' })).toHaveAttribute('href', '/dashboard')
    expect(await within(status).findByRole('link', { name: 'On a call 1' })).toHaveAttribute('href', '/users')
    expect(await within(status).findByRole('link', { name: 'On break 1' })).toHaveAttribute('href', '/breaks')
  })

  it('leaves out the calls figure while the server is not hearing from the PBX', async () => {
    vi.stubGlobal('fetch', vi.fn(routes([
      ['/pbx/queue', () => jsonResponse(QUEUE)],
      ['/pbx/agents', () => jsonResponse({ live: false, problem: 'no_answer', agents: [] })],
      ['/breaks/monitor', () => jsonResponse(BREAKS)],
    ])))
    shell()
    expect(await screen.findByRole('link', { name: 'On break 1' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /On a call/ })).not.toBeInTheDocument()
  })

  it('finds a customer from any page on the Contacts page', async () => {
    shell()
    fireEvent.change(screen.getByRole('searchbox', { name: 'Find a customer' }), { target: { value: '0599123456' } })
    fireEvent.submit(screen.getByRole('search'))

    expect(await screen.findByText('Ahmad')).toBeInTheDocument()
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('0599123456')
    await waitFor(() =>
      expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url).includes('/contacts?q=0599123456'))).toBe(true),
    )
  })

  it('puts the cursor in the search box on /, unless a field is being typed in', () => {
    shell()
    const box = screen.getByRole('searchbox', { name: 'Find a customer' })
    fireEvent.keyDown(document.body, { key: '/' })
    expect(box).toHaveFocus()

    box.blur()
    const other = document.createElement('input')
    document.body.appendChild(other)
    other.focus()
    fireEvent.keyDown(other, { key: '/' })
    expect(other).toHaveFocus()
    other.remove()
  })
})

describe('the sign-in warning', () => {
  it('reads when the token runs out', () => {
    expect(tokenExpiresAt(tokenEnding(1_800_000_000_000))).toBe(1_800_000_000_000)
    expect(tokenExpiresAt('not-a-token')).toBeNull()
  })

  it('warns in the last ten minutes, and not before', () => {
    setToken(tokenEnding(Date.now() + 5 * 60_000))
    const { unmount } = shell()
    expect(screen.getByRole('alert')).toHaveTextContent(/Your sign-in ends at/)
    expect(screen.getByRole('button', { name: 'Sign in again now' })).toBeInTheDocument()
    unmount()

    setToken(tokenEnding(Date.now() + 2 * 3_600_000))
    shell()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('the Saved notice', () => {
  it('shows when a save that asks for it succeeds, and goes by itself', async () => {
    vi.useFakeTimers()
    render(<Toaster />)
    const queryClient = createQueryClient()

    await act(async () => {
      await queryClient.getMutationCache().build(queryClient, { mutationFn: async () => 'ok', meta: { toast: 'saved' } }).execute(undefined)
      await queryClient.getMutationCache().build(queryClient, { mutationFn: async () => 'ok' }).execute(undefined)
    })
    expect(screen.getAllByText('Saved.')).toHaveLength(1)

    act(() => vi.advanceTimersByTime(5_000))
    expect(screen.queryByText('Saved.')).not.toBeInTheDocument()
  })

  it('can be closed', () => {
    render(<Toaster />)
    act(() => showToast('deleted'))
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }))
    expect(screen.queryByText('Deleted.')).not.toBeInTheDocument()
  })
})
