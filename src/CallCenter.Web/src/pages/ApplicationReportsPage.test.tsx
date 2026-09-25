import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import ApplicationReportsPage from './ApplicationReportsPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/**
 * The application reports (A-72, S-05, S-06, S-07), against a stubbed `fetch`:
 * the figures reach the table, the filters reach the server, and the export
 * holds the rows on screen.
 */

const BY_CHANNEL = [
  { channelId: 'ch-wa', channel: 'WhatsApp', messages: 12, unclassified: 2,
    byType: [{ typeName: 'Order', labelAr: 'طلب', labelEn: 'Order', count: 9 }, { typeName: 'Complaint', labelAr: 'شكوى', labelEn: 'Complaint', count: 1 }] },
  { channelId: 'ch-ig', channel: 'Instagram', messages: 5, unclassified: 0,
    byType: [{ typeName: 'Order', labelAr: 'طلب', labelEn: 'Order', count: 5 }] },
]
const ORDERS = [{ key: 'ch-wa', label: 'WhatsApp', orders: 9, orderValue: 540.5, average: 60.06 }]
const TREND = [{ bucket: '2026-09-24', messages: 7, orders: 4, orderValue: 200 }, { bucket: '2026-09-25', messages: 10, orders: 5, orderValue: 340.5 }]
const BY_AGENT = [{ agentId: 'a1', agent: 'Sara', messages: 17, orders: 9, orderValue: 540.5, unclassified: 2 }]
const ISSUES = [{ channelId: 'ch-wa', channel: 'WhatsApp', messages: 12, cancellations: 1, complaints: 1 }]
const CHANNELS = [
  { id: 'ch-phone', name: 'Phone', isSystem: true, sortOrder: 0, isActive: true, inUse: true },
  { id: 'ch-wa', name: 'WhatsApp', isSystem: false, sortOrder: 10, isActive: true, inUse: true },
]

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
    if (url.startsWith('/api/reports/applications/by-channel')) return jsonResponse(BY_CHANNEL)
    if (url.startsWith('/api/reports/applications/orders')) return jsonResponse(ORDERS)
    if (url.startsWith('/api/reports/applications/trend')) return jsonResponse(TREND)
    if (url.startsWith('/api/reports/applications/by-agent')) return jsonResponse(BY_AGENT)
    if (url.startsWith('/api/reports/applications/issues')) return jsonResponse(ISSUES)
    if (url.startsWith('/api/channels')) return jsonResponse(CHANNELS)
    return jsonResponse([])
  })
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ApplicationReportsPage />
    </QueryClientProvider>,
  )
}

const requests = (fetchMock: ReturnType<typeof vi.fn>, report: string) =>
  fetchMock.mock.calls.map(([url]) => String(url)).filter((url) => url.startsWith(`/api/reports/applications/${report}`))

const card = (name: string) => screen.getByRole('region', { name })

/** The test browser's Blob has no text(); FileReader reads it the old way. */
const readBlob = (blob: Blob) =>
  new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error)
    reader.readAsText(blob)
  })

const readBytes = (blob: Blob) =>
  new Promise<ArrayBuffer>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as ArrayBuffer)
    reader.onerror = () => reject(reader.error)
    reader.readAsArrayBuffer(blob)
  })

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('application reports page', () => {
  it('draws every report as a table of the server’s figures, with a total', async () => {
    vi.stubGlobal('fetch', server())
    renderPage()

    const byChannel = card('Messages per channel')
    const wa = (await within(byChannel).findByText('WhatsApp')).closest('tr')!
    expect(within(wa).getByText('12')).toBeInTheDocument()
    expect(within(wa).getByText('2')).toBeInTheDocument()
    // Per type within the channel, as columns.
    expect(within(byChannel).getByRole('columnheader', { name: 'Complaint' })).toBeInTheDocument()
    expect(within(wa).getByText('9')).toBeInTheDocument()
    const total = within(byChannel).getByText('Total').closest('tr')!
    expect(within(total).getByText('17')).toBeInTheDocument()

    const agents = card('Per agent')
    const sara = (await within(agents).findByText('Sara')).closest('tr')!
    expect(within(sara).getByText('540.50')).toBeInTheDocument()

    const trend = card('Messages over time')
    expect(await within(trend).findByText('2026-09-25')).toBeInTheDocument()

    const issues = card('Cancellations and complaints')
    expect(await within(issues).findByText('WhatsApp')).toBeInTheDocument()
  })

  it('sends the period as instants and the chosen channel to every report', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderPage()

    await screen.findByText('Sara')
    // The default period is a range, sent from the start of its first day to
    // the start of the day after its last.
    const first = new URL(requests(fetchMock, 'by-agent')[0], 'http://x')
    expect(first.searchParams.get('from')).not.toBeNull()
    expect(first.searchParams.get('to')).not.toBeNull()
    expect(new Date(first.searchParams.get('to')!).getTime()).toBeGreaterThan(new Date(first.searchParams.get('from')!).getTime())

    await waitFor(() => expect(screen.getByRole('option', { name: 'WhatsApp' })).toBeInTheDocument())
    fireEvent.change(screen.getByLabelText('Channel'), { target: { value: 'ch-wa' } })

    await waitFor(() => expect(requests(fetchMock, 'issues').at(-1)).toContain('channelId=ch-wa'))
    for (const report of ['by-channel', 'orders', 'trend', 'by-agent']) {
      expect(requests(fetchMock, report).at(-1)).toContain('channelId=ch-wa')
    }
  })

  it('sends a custom range as chosen, and a preset as its days', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderPage()
    await screen.findByText('Sara')

    fireEvent.click(screen.getByRole('button', { name: 'Custom' }))
    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-09-01' } })
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-09-10' } })

    await waitFor(() => {
      const sent = new URL(requests(fetchMock, 'by-channel').at(-1)!, 'http://x')
      expect(new Date(sent.searchParams.get('from')!)).toEqual(new Date(2026, 8, 1))
      expect(new Date(sent.searchParams.get('to')!)).toEqual(new Date(2026, 8, 11))
    })

    fireEvent.click(screen.getByRole('button', { name: 'Today' }))
    await waitFor(() => {
      const sent = new URL(requests(fetchMock, 'by-channel').at(-1)!, 'http://x')
      const today = new Date()
      expect(new Date(sent.searchParams.get('from')!)).toEqual(new Date(today.getFullYear(), today.getMonth(), today.getDate()))
    })
  })

  it('regroups the orders report and the trend on the server, not in the browser', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderPage()
    await screen.findByText('Sara')

    fireEvent.change(within(card('Orders and order value')).getByLabelText('Group by'), { target: { value: 'agent' } })
    await waitFor(() => expect(requests(fetchMock, 'orders').at(-1)).toContain('groupBy=agent'))

    fireEvent.change(within(card('Messages over time')).getByLabelText('Group by'), { target: { value: 'hour' } })
    await waitFor(() => expect(requests(fetchMock, 'trend').at(-1)).toContain('groupBy=hour'))
  })

  it('exports the rows on screen as a CSV file, with a byte-order mark for Excel', async () => {
    vi.stubGlobal('fetch', server())
    let saved: Blob | null = null
    URL.createObjectURL = vi.fn((blob: Blob) => {
      saved = blob
      return 'blob:report'
    })
    URL.revokeObjectURL = vi.fn()
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    renderPage()
    const byChannel = card('Messages per channel')
    await within(byChannel).findByText('WhatsApp')

    fireEvent.click(within(byChannel).getByRole('button', { name: 'Export CSV' }))

    expect(click).toHaveBeenCalled()
    // The BOM as bytes: reading the blob as text would decode and drop it.
    const bytes = new Uint8Array(await readBytes(saved!))
    expect([...bytes.slice(0, 3)]).toEqual([0xef, 0xbb, 0xbf])
    const text = await readBlob(saved!)
    const lines = text.trim().split('\r\n')
    expect(lines[0]).toBe('Channel,Messages,Not classified,Order,Complaint')
    expect(lines[1]).toBe('WhatsApp,12,2,9,1')
    expect(lines[2]).toBe('Instagram,5,0,5,0')
    expect(lines).toHaveLength(3)
  })
})
