import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import BreaksPage from './BreaksPage'
import type { BreakMonitor, BreakPage, BreakReport } from '../api/breaks'
import { setToken } from '../auth/token'
import i18n from '../i18n'
import { localDate } from '../lib/reportFilters'
import { jsonResponse, renderWithClient, routes } from '../test/http'

/** Who is on break now (S-66) and the break report (R-22), against a stubbed `fetch`. */

const AS_OF = '2026-10-01T12:00:00Z'

const MONITOR: BreakMonitor = {
  asOf: AS_OF,
  dailyLimitMinutes: 60,
  agents: [
    { agentId: 'a1', agentDisplayName: 'Ahmad', state: 'Working', breakStartedAt: null, todaySeconds: 70 * 60, todayBreaks: 3, doNotDisturb: false, doNotDisturbSince: '2026-10-01T08:00:00Z' },
    { agentId: 'a2', agentDisplayName: 'Sara', state: 'OnBreak', breakStartedAt: '2026-10-01T11:50:00Z', todaySeconds: 25 * 60, todayBreaks: 2, doNotDisturb: true, doNotDisturbSince: '2026-10-01T11:50:00Z' },
    { agentId: 'a3', agentDisplayName: 'Omar', state: 'SignedOut', breakStartedAt: null, todaySeconds: 0, todayBreaks: 0, doNotDisturb: null, doNotDisturbSince: null },
  ],
}

const REPORT: BreakReport = {
  dailyLimitMinutes: 60,
  agents: [
    { agentId: 'a1', agentDisplayName: 'Ahmad', breaks: 3, seconds: 4530, days: 1, daysOver: 1, overSeconds: 930 },
  ],
  days: [
    { day: '2026-10-01', agentId: 'a1', agentDisplayName: 'Ahmad', breaks: 3, seconds: 4530, overSeconds: 930 },
  ],
}

const LIST: BreakPage = {
  rows: [
    { id: 'b2', agentId: 'a2', agentDisplayName: 'Sara', startedAt: '2026-10-01T11:50:00Z', endedAt: null, endedBy: null, seconds: 600 },
    { id: 'b1', agentId: 'a1', agentDisplayName: 'Ahmad', startedAt: '2026-10-01T09:00:00Z', endedAt: '2026-10-01T09:20:00Z', endedBy: 'NotHeard', seconds: 1200 },
  ],
  total: 2, page: 1, pageSize: 50,
}

const USERS = [
  { id: 'a1', login: 'ahmad', displayName: 'Ahmad', role: 'Agent', isActive: true },
  { id: 's1', login: 'supervisor', displayName: 'Supervisor', role: 'Supervisor', isActive: true },
]

function server() {
  return vi.fn(routes([
    ['/api/breaks/monitor', () => jsonResponse(MONITOR)],
    ['/api/breaks/report', () => jsonResponse(REPORT)],
    ['/api/breaks/export', () => ({
      ok: true, status: 200, statusText: 'OK', headers: new Headers({ 'content-type': 'text/csv' }),
      blob: async () => new Blob(['Agent']),
    } as unknown as Response)],
    ['/api/breaks?', () => jsonResponse(LIST)],
    ['/api/users', () => jsonResponse(USERS)],
  ]))
}

const calls = (fetchMock: ReturnType<typeof vi.fn>, prefix: string) =>
  fetchMock.mock.calls.filter(([url]) => String(url).startsWith(prefix))

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
  URL.createObjectURL = vi.fn(() => 'blob:breaks')
  URL.revokeObjectURL = vi.fn()
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('breaks page', () => {
  it('puts the agents on break first, with how long, and shows a day over the allowance in red', async () => {
    vi.stubGlobal('fetch', server())
    renderWithClient(<BreaksPage />)

    const now = await screen.findByRole('region', { name: 'Now' })
    const rows = (await within(now).findAllByRole('row')).slice(1)
    expect(rows.map((r) => within(r).getAllByRole('cell')[0].textContent)).toEqual(['Sara', 'Ahmad', 'Omar'])

    const sara = rows[0]
    expect(within(sara).getByText('On break')).toBeInTheDocument()
    // Ten minutes into this break at the server's "as of", and 25 minutes today.
    expect(within(sara).getByText('10:00')).toBeInTheDocument()
    expect(within(sara).getByText('25:00')).toBeInTheDocument()

    const ahmad = rows[1]
    expect(within(ahmad).getByText('Working')).toBeInTheDocument()
    const today = within(ahmad).getByText('1:10:00')
    expect(today).toHaveClass('text-red-300')
    expect(within(ahmad).getByText('10:00')).toBeInTheDocument() // ten minutes over the hour

    expect(within(rows[2]).getByText('Signed out')).toBeInTheDocument()
    expect(within(now).getByText(/On break now: 1\. The daily allowance is 60 minutes/)).toBeInTheDocument()
  })

  it('shows do not disturb for each agent, on since when, off, or nothing when the app does not say', async () => {
    vi.stubGlobal('fetch', server())
    renderWithClient(<BreaksPage />)

    const now = await screen.findByRole('region', { name: 'Now' })
    const rows = (await within(now).findAllByRole('row')).slice(1)
    expect(within(now).getByRole('columnheader', { name: 'Do not disturb' })).toBeInTheDocument()
    const dnd = (row: HTMLElement) => within(row).getAllByRole('cell')[2]
    const time = new Date('2026-10-01T11:50:00Z').toLocaleTimeString('en', { hour: '2-digit', minute: '2-digit' })

    expect(within(dnd(rows[0])).getByText('On')).toHaveClass('badge-warn')
    expect(dnd(rows[0])).toHaveTextContent(`since ${time}`)
    expect(dnd(rows[1])).toHaveTextContent(/^Off$/)
    expect(dnd(rows[2])).toBeEmptyDOMElement()
  })

  it('opens the report on today, by agent and by day, in minutes', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<BreaksPage />)

    const perAgent = await screen.findByRole('region', { name: 'Break time per agent' })
    const row = (await within(perAgent).findByText('Ahmad')).closest('tr')!
    expect(within(row).getByText('75.5')).toBeInTheDocument()
    expect(within(row).getByText('15.5')).toBeInTheDocument()

    const today = localDate(new Date())
    await waitFor(() => expect(calls(fetchMock, '/api/breaks/report').length).toBeGreaterThan(0))
    expect(String(calls(fetchMock, '/api/breaks/report')[0][0])).toContain(`from=${today}&to=${today}`)

    expect(screen.getByRole('region', { name: 'Break time per agent per day' })).toHaveTextContent('Ahmad')
  })

  it('asks for a period and an agent only: breaks have no branch, channel or type', async () => {
    vi.stubGlobal('fetch', server())
    renderWithClient(<BreaksPage />)

    await screen.findByRole('region', { name: 'Break time per agent' })
    expect(screen.getByLabelText('Agent')).toBeInTheDocument()
    expect(screen.queryByLabelText('Branch')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Type')).not.toBeInTheDocument()
  })

  it('lists every break with how it ended, and exports them all from the server', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<BreaksPage />)

    const list = await screen.findByRole('region', { name: 'Every break' })
    expect(await within(list).findByText('Still on break')).toBeInTheDocument()
    expect(within(list).getByText('App stopped')).toBeInTheDocument()
    expect(within(list).getByText('2 breaks')).toBeInTheDocument()

    fireEvent.click(within(list).getByRole('button', { name: 'Export all 2 (CSV)' }))
    await waitFor(() => expect(calls(fetchMock, '/api/breaks/export')).toHaveLength(1))
    expect(String(calls(fetchMock, '/api/breaks/export')[0][0])).toContain('lang=en')
  })
})
