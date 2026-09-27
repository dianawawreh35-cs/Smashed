import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import LogsPage from './LogsPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'
import { jsonResponse, renderWithClient, routes } from '../test/http'
import type { AgentLogLaptop, AgentLogPage } from '../api/agentLogs'

/** The Agent Apps' logs (N-12), against a stubbed `fetch`. */

const today = new Date()
const date = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
const file = `agent-${date.replaceAll('-', '')}.log`

const QUIET: AgentLogLaptop = {
  laptop: 'LAPTOP-QUIET',
  lastWriteAt: new Date(Date.now() - 1000).toISOString(),
  days: [{ file, date, bytes: 100, errors: 0, warnings: 0, lastWriteAt: new Date().toISOString() }],
}

const BROKEN: AgentLogLaptop = {
  laptop: 'LAPTOP-BROKEN',
  lastWriteAt: new Date(Date.now() - 60_000).toISOString(),
  days: [
    { file, date, bytes: 900, errors: 2, warnings: 1, lastWriteAt: new Date().toISOString() },
    { file: 'agent-20260101.log', date: '2026-01-01', bytes: 10, errors: 0, warnings: 0, lastWriteAt: '2026-01-01T10:00:00Z' },
  ],
}

const PAGE: AgentLogPage = {
  entries: [
    { line: 9, time: `${date} 09:03:00.000 +03:00`, level: 'ERR', text: 'The call could not be logged\n   at ApiClient.SendAsync()' },
    { line: 5, time: `${date} 09:02:00.000 +03:00`, level: 'WRN', text: 'Extension 2001 not registered yet' },
    { line: 3, time: `${date} 09:01:00.000 +03:00`, level: 'ERR', text: 'Recording failed' },
    { line: 1, time: `${date} 09:00:00.000 +03:00`, level: 'INF', text: 'Agent App started\nsecond line' },
  ],
  matched: 4,
  total: 4,
  errors: 2,
  warnings: 1,
}

function stub(laptops: AgentLogLaptop[] = [QUIET, BROKEN]) {
  const fetchMock = vi.fn(routes([
    [/\/agent-logs\/[^/]+\/[^/?]+/, () => jsonResponse(PAGE)],
    ['/agent-logs', () => jsonResponse(laptops)],
  ]))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const logRequests = (fetchMock: ReturnType<typeof vi.fn>) =>
  fetchMock.mock.calls.map(([url]) => String(url)).filter((url) => /agent-logs\/[^/]+\//.test(url))

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('logs page', () => {
  it('puts the laptop with errors first, counts them, and says so at the top', async () => {
    stub()
    renderWithClient(<LogsPage />)

    const laptops = within(await screen.findByRole('navigation', { name: 'Laptops' })).getAllByRole('button')
    expect(laptops[0]).toHaveTextContent('LAPTOP-BROKEN')
    expect(laptops[0]).toHaveTextContent('2 errors')
    expect(laptops[0]).toHaveTextContent('1 warning')
    expect(laptops[1]).toHaveTextContent('No errors')

    expect(screen.getByRole('status')).toHaveTextContent('2 errors today. Laptops affected: 1.')
  })

  it('opens the laptop with errors and marks its errors and warnings', async () => {
    stub()
    renderWithClient(<LogsPage />)

    const error = (await screen.findByText(/The call could not be logged/)).closest('li')!
    expect(error).toHaveAttribute('data-level', 'error')
    expect(error.className).toContain('bg-red-500/10')
    // An error shows whole, stack trace and all.
    expect(error).toHaveTextContent('at ApiClient.SendAsync()')

    expect(screen.getByText('Extension 2001 not registered yet').closest('li')).toHaveAttribute('data-level', 'warning')

    // An ordinary entry is folded to its first line until asked.
    const info = screen.getByText(/Agent App started/).closest('li')!
    expect(info).toHaveAttribute('data-level', 'info')
    expect(info).not.toHaveTextContent('second line')
    fireEvent.click(within(info).getByRole('button', { name: '1 more line' }))
    expect(info).toHaveTextContent('second line')
  })

  it('asks the server for errors only', async () => {
    const fetchMock = stub()
    renderWithClient(<LogsPage />)

    await screen.findByText(/The call could not be logged/)
    fireEvent.click(screen.getByRole('button', { name: 'Errors only' }))

    await waitFor(() => expect(logRequests(fetchMock).some((url) => url.includes('levels=errors'))).toBe(true))
    expect(logRequests(fetchMock)[0]).toContain('/agent-logs/LAPTOP-BROKEN/')
  })

  it('walks from one error to the next and back to the first', async () => {
    stub()
    renderWithClient(<LogsPage />)

    const first = (await screen.findByText(/The call could not be logged/)).closest('li')!
    const second = screen.getByText('Recording failed').closest('li')!
    const next = screen.getByRole('button', { name: 'Next error' })

    fireEvent.click(next)
    expect(first).toHaveFocus()
    fireEvent.click(next)
    expect(second).toHaveFocus()
    fireEvent.click(next)
    expect(first).toHaveFocus()
  })

  it('switches laptop', async () => {
    const fetchMock = stub()
    renderWithClient(<LogsPage />)

    await screen.findByText(/The call could not be logged/)
    fireEvent.click(screen.getByRole('button', { name: /LAPTOP-QUIET/ }))

    await waitFor(() => expect(logRequests(fetchMock).some((url) => url.includes('/agent-logs/LAPTOP-QUIET/'))).toBe(true))
  })

  it('says when no laptop has sent anything yet', async () => {
    stub([])
    renderWithClient(<LogsPage />)

    expect(await screen.findByText('No laptop has sent its log yet.')).toBeInTheDocument()
  })

  it('says the list did not load rather than that there is none', async () => {
    vi.stubGlobal('fetch', vi.fn(routes([['/agent-logs', () => jsonResponse(null, 500)]])))
    renderWithClient(<LogsPage />)

    expect(await screen.findByText('The logs did not load.')).toBeInTheDocument()
  })
})
