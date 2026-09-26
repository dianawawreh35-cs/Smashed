import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import CallReportsPage from './CallReportsPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/**
 * The call reports (R-01 to R-05, S-05 to S-07), against a stubbed `fetch`:
 * the server's figures reach the tables, the filters reach the server, only
 * the open tab is fetched, and an export holds what the table shows.
 */

const SUMMARY = [
  { bucket: '2026-09-24', communications: 14, calls: 12, messages: 2, inbound: 10, outbound: 2, answered: 7, missed: 2, blocked: 1, abandoned: 1 },
  { bucket: '2026-09-25', communications: 9, calls: 8, messages: 1, inbound: 6, outbound: 2, answered: 5, missed: 1, blocked: 0, abandoned: 0 },
]
const BY_TYPE = [
  { typeName: 'Order', labelAr: 'طلب', labelEn: 'Order', count: 6, share: 60 },
  { typeName: 'Complaint', labelAr: 'شكوى', labelEn: 'Complaint', count: 4, share: 40 },
]
const BREAKDOWN = [
  { key: 'a1', label: 'Sara', calls: 11, answered: 8, missed: 2, orders: 5, orderValue: 250.5,
    byType: [{ typeName: 'Order', labelAr: 'طلب', labelEn: 'Order', count: 5 }] },
]
const RECURRING = [
  { contactId: 'c1', name: 'Khaled', number: '0599000001', calls: 4, orders: 2, orderValue: 80, complaints: 0, lastAt: '2026-09-25T10:00:00Z' },
]
const COMPLAINTS = [
  { id: 'p1', startedAt: '2026-09-25T13:00:00Z', channel: 'Phone', contactId: 'c2', customer: 'Yousef', number: '0599000002',
    agent: 'Omar', branch: 'Rafat', notes: 'cold burger, again', followUp: true, resolved: false, resolvedAt: null },
]
const COMPLAINTS_BY = [
  { key: 'b1', label: 'Rafat', complaints: 2, followUp: 1, resolved: 1, open: 1, averageHoursToResolve: 2, orders: 4, perHundredOrders: 50 },
]
const BRANCHES = [{ id: 'b1', name: 'Rafat' }, { id: 'b2', name: 'Nablus' }]
const PEAK = Array.from({ length: 24 }, (_, hour) => ({
  hour,
  byWeekday: hour === 19 ? [3, 4, 5, 6, 7, 12, 9] : [0, 0, 0, 0, 0, 0, 0],
  total: hour === 19 ? 46 : 0,
}))
const ORDERS_BY_CHANNEL = [
  { channelId: 'ch-phone', channel: 'Phone', orders: 20, orderValue: 900, share: 66.7 },
  { channelId: 'ch-wa', channel: 'WhatsApp', orders: 10, orderValue: 450, share: 33.3 },
]
const CANCELLATIONS = [{ key: 'b1', label: 'Rafat', orders: 30, cancellations: 3, rate: 10 }]
const AGENTS = [{ agentId: 'a1', agent: 'Sara', handled: 40, inbound: 35, outbound: 6, averageDurationSec: 135,
  orders: 20, orderValue: 900, unclassified: 2, missed: 4 }]
const QUALITY = { unclassified: 7, unknownCalls: 31, unknownNumbers: 25, duplicateNames: 1056 }
const MISSED = [{ key: '2026-09-25', label: '2026-09-25', inbound: 40, missed: 3, rejected: 1, abandoned: 0, total: 4, rate: 10 }]

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

function server() {
  return vi.fn().mockImplementation(async (url: string) => {
    const path = url.split('?')[0]
    if (path === '/api/reports/calls/summary') return jsonResponse(SUMMARY)
    if (path === '/api/reports/calls/by-type') return jsonResponse(BY_TYPE)
    if (path === '/api/reports/calls/breakdown') return jsonResponse(BREAKDOWN)
    if (path === '/api/reports/calls/recurring-customers') return jsonResponse(RECURRING)
    if (path === '/api/reports/calls/complaints') return jsonResponse(COMPLAINTS)
    if (path === '/api/reports/calls/complaints/by') return jsonResponse(COMPLAINTS_BY)
    if (path === '/api/reports/calls/repeat-complainers') return jsonResponse([])
    if (path === '/api/reports/calls/peak-hours') return jsonResponse(PEAK)
    if (path === '/api/reports/calls/orders-by-channel') return jsonResponse(ORDERS_BY_CHANNEL)
    if (path === '/api/reports/calls/cancellations') return jsonResponse(CANCELLATIONS)
    if (path === '/api/reports/calls/agents') return jsonResponse(AGENTS)
    if (path === '/api/reports/calls/data-quality') return jsonResponse(QUALITY)
    if (path === '/api/reports/calls/missed') return jsonResponse(MISSED)
    if (path === '/api/branches') return jsonResponse(BRANCHES)
    return jsonResponse([])
  })
}

function renderPage(at = '/call-reports') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[at]}>
        <CallReportsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const requests = (fetchMock: ReturnType<typeof vi.fn>, report: string) =>
  fetchMock.mock.calls.map(([url]) => String(url)).filter((url) => url.split('?')[0] === `/api/reports/calls/${report}`)

const card = (name: string) => screen.getByRole('region', { name })

/** The test browser's Blob has no text(); FileReader reads it the old way. */
const readBlob = (blob: Blob) =>
  new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error)
    reader.readAsText(blob)
  })

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('call reports page', () => {
  it('draws R-01, R-03 and R-04 as the server counted them, with totals', async () => {
    vi.stubGlobal('fetch', server())
    renderPage()

    const summary = card('Communications')
    const day = (await within(summary).findByText('2026-09-24')).closest('tr')!
    expect(within(day).getByText('14')).toBeInTheDocument()
    const total = within(summary).getByText('Total').closest('tr')!
    expect(within(total).getByText('23')).toBeInTheDocument()
    // A number column is centred, heading and figures alike, so each figure
    // sits under its heading; the table's own rule would put headings at the
    // start (26 Sep).
    expect(within(summary).getByRole('columnheader', { name: 'Calls' })).toHaveClass('!text-center')
    expect(within(day).getByText('14').closest('td')).toHaveClass('text-center')
    // Only the figure is held left-to-right.
    const figure = within(day).getByText('14')
    expect(figure.closest('td')).not.toHaveAttribute('dir')
    expect(figure).toHaveAttribute('dir', 'ltr')
    // What "missed" means is on the card, where a supervisor would wonder.
    expect(within(summary).getByText(/never an outgoing call nobody picked up/)).toBeInTheDocument()

    const byType = card('Calls per type')
    const order = (await within(byType).findByText('Order')).closest('tr')!
    expect(within(order).getByText('60%')).toBeInTheDocument()

    const breakdown = card('Calls per day, agent or branch')
    const sara = (await within(breakdown).findByText('Sara')).closest('tr')!
    expect(within(sara).getByText('250.50')).toBeInTheDocument()

    // R-02 is the Calls page, not a second list here.
    expect(screen.getByRole('link', { name: 'Open Calls' })).toHaveAttribute('href', '/calls')
  })

  it('sends the chosen branch and the period to every report on the tab', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderPage()
    await screen.findByText('Sara')

    const first = new URL(requests(fetchMock, 'summary')[0], 'http://x')
    expect(new Date(first.searchParams.get('to')!).getTime()).toBeGreaterThan(new Date(first.searchParams.get('from')!).getTime())

    await waitFor(() => expect(screen.getByRole('option', { name: 'Nablus' })).toBeInTheDocument())
    fireEvent.change(screen.getByLabelText('Branch'), { target: { value: 'b2' } })

    await waitFor(() => expect(requests(fetchMock, 'summary').at(-1)).toContain('branchId=b2'))
    for (const report of ['by-type', 'breakdown']) {
      await waitFor(() => expect(requests(fetchMock, report).at(-1)).toContain('branchId=b2'))
    }
  })

  it('regroups on the server, and fetches only the open tab', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderPage()
    await screen.findByText('Sara')

    fireEvent.change(within(card('Communications')).getByLabelText('Group by'), { target: { value: 'week' } })
    await waitFor(() => expect(requests(fetchMock, 'summary').at(-1)).toContain('groupBy=week'))

    expect(requests(fetchMock, 'complaints')).toHaveLength(0)
    fireEvent.click(screen.getByRole('tab', { name: 'Problems' }))
    expect(await within(card('Complaints')).findByText('cold burger, again')).toBeInTheDocument()
    expect(requests(fetchMock, 'complaints')).toHaveLength(1)

    fireEvent.change(within(card('Complaints per branch and agent')).getByLabelText('Group by'), { target: { value: 'agent' } })
    await waitFor(() => expect(requests(fetchMock, 'complaints/by').at(-1)).toContain('groupBy=agent'))
  })

  it('shows a complaint with its follow-up status, and exports the rows the table shows', async () => {
    vi.stubGlobal('fetch', server())
    let saved: Blob | null = null
    URL.createObjectURL = vi.fn((blob: Blob) => {
      saved = blob
      return 'blob:report'
    })
    URL.revokeObjectURL = vi.fn()
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    renderPage('/call-reports?tab=problems')
    const complaints = card('Complaints')
    const row = (await within(complaints).findByText('Yousef')).closest('tr')!
    expect(within(row).getByText('Yes')).toBeInTheDocument()
    expect(within(row).getByText('Open')).toBeInTheDocument()

    fireEvent.click(within(complaints).getByRole('button', { name: 'Export CSV' }))
    const lines = (await readBlob(saved!)).trim().split('\r\n')
    expect(lines[0]).toBe('When,Customer,Number,Agent,Branch,Notes,Follow-up,Status')
    // The time as the restaurant reads it, which Excel takes as a date.
    expect(lines[1]).toMatch(/^2026-09-2\d \d\d:00,Yousef,0599000002,Omar,Rafat,"cold burger, again",Yes,Open$/)
    expect(lines).toHaveLength(2)
  })

  it('draws R-10 as a heat table: every cell printed, the busiest shaded strongest', async () => {
    vi.stubGlobal('fetch', server())
    renderPage()

    const peak = card('Peak hours')
    const seven = (await within(peak).findByText('19:00')).closest('tr')!
    const cells = within(seven).getAllByRole('cell')
    // Hour, Monday … Sunday, total.
    expect(cells.map((c) => c.textContent)).toEqual(['19:00', '3', '4', '5', '6', '7', '12', '9', '46'])
    const shade = (cell: HTMLElement) => Number(/rgba\(79, 140, 255, ([\d.]+)\)/.exec(cell.getAttribute('style') ?? '')?.[1] ?? 0)
    expect(shade(cells[6])).toBeGreaterThan(shade(cells[1]))
    // An empty hour is unshaded, and reads as empty.
    const three = within(peak).getByText('03:00').closest('tr')!
    expect(within(three).getAllByRole('cell')[1].getAttribute('style')).toBeNull()
  })

  it('shows the orders, agents and data quality tabs as the server counted them', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderPage('/call-reports?tab=orders')

    const channels = card('Orders by channel')
    const wa = (await within(channels).findByText('WhatsApp')).closest('tr')!
    expect(within(wa).getByText('33.3%')).toBeInTheDocument()
    fireEvent.change(within(card('Cancellation rate')).getByLabelText('Group by'), { target: { value: 'channel' } })
    await waitFor(() => expect(requests(fetchMock, 'cancellations').at(-1)).toContain('groupBy=channel'))

    fireEvent.click(screen.getByRole('tab', { name: 'Agents' }))
    const sara = (await within(card('Agent productivity')).findByText('Sara')).closest('tr')!
    expect(within(sara).getByText('2:15')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: 'Data quality' }))
    const quality = card('Data quality')
    const unknown = (await within(quality).findByText('Calls from numbers nobody saved')).closest('tr')!
    expect(within(unknown).getByText('31')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: 'Problems' }))
    const missed = card('Missed calls')
    expect(await within(missed).findByText('10%')).toBeInTheDocument()
    expect(within(missed).getByText(/An outgoing call nobody picked up is not a missed call/)).toBeInTheDocument()
  })

  it('shows the abandoned calls already fetched, and fetches the chosen period from the PBX on demand', async () => {
    const ABANDONED = [{ key: '2026-09-24', label: '2026-09-24', inbound: 20, abandoned: 3, rate: 15, averageWaitSec: 75,
      maxWaitSec: 115, calledBack: 1, averageMinutesToCallBack: 12 }]
    const LIST = [
      { id: 'x1', startedAt: '2026-09-24T13:47:09Z', endedAt: '2026-09-24T13:49:04Z', waitSec: 115, queue: '002',
        number: '0569000002', contactId: null, customer: null, rings: 6, calledBackAt: null, calledBackBy: null, minutesToCallBack: null },
      { id: 'x2', startedAt: '2026-09-24T12:00:00Z', endedAt: '2026-09-24T12:00:40Z', waitSec: 40, queue: '002',
        number: '0569000003', contactId: 'c9', customer: 'Rami', rings: 2, calledBackAt: '2026-09-24T12:12:00Z',
        calledBackBy: 'Sara', minutesToCallBack: 12 },
    ]
    const STATUS = { url: 'https://10.8.0.1', username: 'reports', passwordSet: true, intervalMinutes: 1, configured: true,
      lastCheckedAt: '2026-09-24T14:00:00Z', lastSucceededAt: '2026-09-24T14:00:00Z', lastError: null, lastAdded: 0,
      syncedThrough: '2026-09-24' }
    const base = server()
    const fetchMock = vi.fn().mockImplementation(async (url: string, init?: RequestInit) => {
      const path = url.split('?')[0]
      if (path === '/api/reports/calls/abandoned') return jsonResponse(ABANDONED)
      if (path === '/api/reports/calls/abandoned/list') return jsonResponse(LIST)
      if (path === '/api/pbx/abandoned-import') return jsonResponse(STATUS)
      if (path === '/api/pbx/abandoned-import/fetch' && init?.method === 'POST') {
        return jsonResponse({ ok: true, error: null, from: '2026-09-18', to: '2026-09-24', calls: 67, abandoned: 36, added: 2,
          ringsLinked: 5, status: STATUS })
      }
      return base(url)
    })
    vi.stubGlobal('fetch', fetchMock)
    renderPage('/call-reports?tab=abandoned')

    const figures = card('Abandoned calls')
    const day = (await within(figures).findByText('2026-09-24')).closest('tr')!
    expect(within(day).getByText('15%')).toBeInTheDocument()
    expect(within(day).getByText('1:15')).toBeInTheDocument()
    expect(within(day).getByText('1:55')).toBeInTheDocument()

    const list = card('Every abandoned call')
    // No contact: the number stands in for the customer's name too.
    const waiting = (await within(list).findAllByText('0569000002'))[0].closest('tr')!
    expect(within(waiting).getByText('Not called back')).toBeInTheDocument()
    expect(within(waiting).getByText('6')).toBeInTheDocument()
    const rami = within(list).getByText('Rami').closest('tr')!
    expect(within(rami).getByText('Sara')).toBeInTheDocument()

    fireEvent.click(await screen.findByRole('button', { name: 'Fetch from PBX' }))
    expect(await screen.findByText('Downloaded 2026-09-18 to 2026-09-24: 36 abandoned, 2 new.')).toBeInTheDocument()
    const post = fetchMock.mock.calls.find(([url, init]) => String(url).startsWith('/api/pbx/abandoned-import/fetch') && init?.method === 'POST')!
    const sent = new URL(String(post[0]), 'http://x')
    expect(sent.searchParams.get('from')).toBeTruthy()
    expect(sent.searchParams.get('to')).toBeTruthy()
  })

  it('reads in Arabic', async () => {
    await i18n.changeLanguage('ar')
    vi.stubGlobal('fetch', server())
    renderPage('/call-reports?tab=customers')

    expect(await screen.findByText('Khaled')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'تقارير المكالمات' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'الزبائن المتكرّرون' })).toBeInTheDocument()
  })
})
