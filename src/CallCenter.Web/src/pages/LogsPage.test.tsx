import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import LogsPage from './LogsPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'
import { jsonResponse, renderWithClient, routes } from '../test/http'
import type { AgentLogAcknowledgement, AgentLogLaptop, AgentLogPage } from '../api/agentLogs'

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

/** Nobody has acknowledged the errors yet, unless told otherwise. */
const NOT_ACKNOWLEDGED = () => jsonResponse(null, 204)

function stub(laptops: AgentLogLaptop[] = [QUIET, BROKEN], acknowledged: AgentLogAcknowledgement | null = null) {
  const fetchMock = vi.fn(routes([
    ['/agent-logs/_acknowledged', () => (acknowledged ? jsonResponse(acknowledged) : NOT_ACKNOWLEDGED())],
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

  it('opens on today, and says so for a laptop that sent nothing today', async () => {
    const OLD: AgentLogLaptop = {
      laptop: 'LAPTOP-OLD',
      lastWriteAt: '2026-01-01T10:00:00Z',
      days: [{ file: 'agent-20260101.log', date: '2026-01-01', bytes: 10, errors: 3, warnings: 0, lastWriteAt: '2026-01-01T10:00:00Z' }],
    }
    const fetchMock = stub([OLD, QUIET, BROKEN])
    renderWithClient(<LogsPage />)

    const laptops = within(await screen.findByRole('navigation', { name: 'Laptops' })).getAllByRole('button')
    expect(screen.getByLabelText('Date')).toHaveValue(date)
    // Its errors are from another day, so it goes last and does not count.
    expect(laptops[2]).toHaveTextContent('LAPTOP-OLD')
    expect(laptops[2]).toHaveTextContent('No log that day')
    expect(laptops[2]).not.toHaveTextContent('3 errors')

    fireEvent.click(laptops[2])
    expect(await screen.findByText(`This laptop sent nothing on ${date}.`)).toBeInTheDocument()
    expect(logRequests(fetchMock).some((url) => url.includes('/agent-logs/LAPTOP-OLD/'))).toBe(false)
  })

  it('reads another date, and goes back to today', async () => {
    const fetchMock = stub()
    renderWithClient(<LogsPage />)

    await screen.findByText(/The call could not be logged/)
    expect(screen.queryByRole('button', { name: 'Today' })).not.toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-01-01' } })
    await waitFor(() =>
      expect(logRequests(fetchMock).some((url) => url.includes('/agent-logs/LAPTOP-BROKEN/agent-20260101.log'))).toBe(true),
    )
    // The quiet laptop sent nothing that day.
    expect(screen.getByRole('button', { name: /LAPTOP-QUIET/ })).toHaveTextContent('No log that day')

    fireEvent.click(screen.getByRole('button', { name: 'Today' }))
    expect(screen.getByLabelText('Date')).toHaveValue(date)
  })

  it('does not keep the last day on screen for a date the laptop has none', async () => {
    stub()
    renderWithClient(<LogsPage />)

    await screen.findByText(/The call could not be logged/)
    fireEvent.click(screen.getByRole('button', { name: /LAPTOP-QUIET/ }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Next error' })).toBeEnabled())

    fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-01-01' } })
    expect(await screen.findByText('This laptop sent nothing on 2026-01-01.')).toBeInTheDocument()
    expect(screen.queryByText(/The call could not be logged/)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next error' })).toBeDisabled()
  })

  it('scrolls the entries inside their own box', async () => {
    stub()
    renderWithClient(<LogsPage />)

    const list = (await screen.findByText(/The call could not be logged/)).closest('ol')!
    expect(list.className).toContain('overflow-y-auto')
    expect(list.className).toContain('max-h-[70vh]')
  })

  it('shows a nickname in place of the Windows name, with the Windows name under it', async () => {
    stub([{ ...QUIET, nickname: 'Front desk' }, BROKEN])
    renderWithClient(<LogsPage />)

    const quiet = await screen.findByRole('button', { name: /Front desk/ })
    expect(quiet).toHaveTextContent('LAPTOP-QUIET')

    fireEvent.click(quiet)
    expect(await screen.findByRole('heading', { name: 'Front desk' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Rename' })).toBeInTheDocument()
  })

  it('names a laptop and asks for the list again', async () => {
    let named: string | null = null
    const fetchMock = vi.fn(routes([
      ['/agent-logs/_acknowledged', NOT_ACKNOWLEDGED],
      [/\/agent-logs\/LAPTOP-BROKEN\/nickname/, (_url, init) => {
        named = JSON.parse(String(init?.body)).nickname
        return jsonResponse({ nickname: named })
      }],
      [/\/agent-logs\/[^/]+\/[^/?]+/, () => jsonResponse(PAGE)],
      ['/agent-logs', () => jsonResponse([QUIET, { ...BROKEN, nickname: named }])],
    ]))
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<LogsPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Give it a name' }))
    fireEvent.change(screen.getByLabelText('Nickname for LAPTOP-BROKEN'), { target: { value: '  Kitchen  ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('heading', { name: 'Kitchen' })).toBeInTheDocument()
    expect(named).toBe('Kitchen')
    const put = fetchMock.mock.calls.find(([url]) => String(url).includes('/nickname'))!
    expect(put[1]?.method).toBe('PUT')
  })

  it('acknowledges the errors: the red line goes, and says who', async () => {
    let sent: { date: string; errors: Record<string, number> } | null = null
    const fetchMock = vi.fn(routes([
      ['/agent-logs/_acknowledged', (_url, init) => {
        if (init?.method !== 'PUT') return NOT_ACKNOWLEDGED()
        sent = JSON.parse(String(init.body))
        return jsonResponse({ ...sent!, by: 'Supervisor', at: new Date().toISOString() })
      }],
      [/\/agent-logs\/[^/]+\/[^/?]+/, () => jsonResponse(PAGE)],
      ['/agent-logs', () => jsonResponse([QUIET, BROKEN])],
    ]))
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<LogsPage />)

    const alert = await screen.findByRole('status')
    fireEvent.click(within(alert).getByRole('button', { name: 'Acknowledge' }))

    await waitFor(() => expect(screen.queryByRole('status')).not.toBeInTheDocument())
    expect(sent).toEqual({ date, errors: { 'LAPTOP-BROKEN': 2 } })
    expect(screen.getByText(/Today's errors acknowledged by Supervisor/)).toBeInTheDocument()
    // The laptop still shows its day's errors: only the line at the top goes.
    expect(screen.getByRole('button', { name: /LAPTOP-BROKEN/ })).toHaveTextContent('2 errors')
  })

  it('comes back with only the errors that arrived after the acknowledgement', async () => {
    stub([QUIET, BROKEN], { date, errors: { 'LAPTOP-BROKEN': 1 }, by: 'Supervisor', at: new Date().toISOString() })
    renderWithClient(<LogsPage />)

    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent('1 new error today since it was acknowledged. Laptops affected: 1.'),
    )
  })

  it("does not count yesterday's acknowledgement for today", async () => {
    stub([QUIET, BROKEN], { date: '2026-01-01', errors: { 'LAPTOP-BROKEN': 5 }, by: 'Supervisor', at: '2026-01-01T10:00:00Z' })
    renderWithClient(<LogsPage />)

    expect(await screen.findByRole('status')).toHaveTextContent('2 errors today. Laptops affected: 1.')
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
