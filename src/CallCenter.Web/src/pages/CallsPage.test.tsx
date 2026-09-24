import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import CallsPage from './CallsPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/** The supervisor's call search and a call opened (S-02, S-03, S-04), against a stubbed `fetch`. */

const ROW = {
  id: 'c1',
  kind: 'Call',
  startedAt: '2026-09-24T09:15:00Z',
  direction: 'In',
  status: 'Answered',
  agentId: 'a1',
  agentDisplayName: 'Sara',
  contactId: 'k1',
  contactName: 'Mahmoud',
  remoteNumberRaw: '0599123456',
  branchId: 'b1',
  branchName: 'Ramallah',
  typeName: 'Order',
  typeLabelAr: 'طلب',
  typeLabelEn: 'Order',
  orderValue: 45.5,
  durationSec: 115,
  notes: 'cold fries',
  isClassified: false,
  hasRecording: true,
  recordingExpired: false,
}

const EXPIRED = { ...ROW, id: 'c2', contactName: 'Old caller', hasRecording: false, recordingExpired: true }

const DETAILS = (row: typeof ROW) => ({
  summary: row,
  remoteName: null,
  answeredAt: '2026-09-24T09:15:05Z',
  endedAt: '2026-09-24T09:17:00Z',
  waitSec: null,
  queueName: 'smashed-002',
  extension: '2001',
  callNotes: null,
})

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

/** A 3-second recording, on hold from 1 s to 2 s, as the Agent App's recorder writes one. */
function recordingWithHold(): ArrayBuffer {
  const rate = 8000
  const audio = rate * 2 * 3
  const buffer = new ArrayBuffer(44 + audio + 16)
  const view = new DataView(buffer)
  const ascii = (at: number, text: string) => [...text].forEach((c, i) => view.setUint8(at + i, c.charCodeAt(0)))
  ascii(0, 'RIFF'); view.setUint32(4, buffer.byteLength - 8, true); ascii(8, 'WAVE')
  ascii(12, 'fmt '); view.setUint32(16, 16, true); view.setUint16(20, 7, true); view.setUint16(22, 2, true)
  view.setUint32(24, rate, true); view.setUint32(28, rate * 2, true); view.setUint16(32, 2, true); view.setUint16(34, 8, true)
  ascii(36, 'data'); view.setUint32(40, audio, true)
  new Uint8Array(buffer, 44, audio).fill(0xff)
  ascii(44 + audio, 'hold'); view.setUint32(48 + audio, 8, true)
  view.setUint32(52 + audio, rate * 1, true); view.setUint32(56 + audio, rate * 1, true)
  return buffer
}

/** The server, by URL. */
function server() {
  return vi.fn().mockImplementation(async (url: string) => {
    if (url.startsWith('/api/communications/search')) return jsonResponse({ rows: [ROW, EXPIRED], total: 2, page: 1, pageSize: 50 })
    if (url === '/api/communications/c1') return jsonResponse(DETAILS(ROW))
    if (url === '/api/communications/c2') return jsonResponse(DETAILS(EXPIRED))
    if (url.startsWith('/api/recordings/c1')) {
      const bytes = recordingWithHold()
      return { ok: true, status: 200, statusText: 'OK', headers: new Headers(), blob: async () => new Blob([bytes]) } as unknown as Response
    }
    return jsonResponse([])
  })
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <CallsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const searches = (fetchMock: ReturnType<typeof vi.fn>) =>
  fetchMock.mock.calls.map(([url]) => String(url)).filter((url) => url.startsWith('/api/communications/search'))

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
  URL.createObjectURL = vi.fn(() => 'blob:recording')
  URL.revokeObjectURL = vi.fn()
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('calls page', () => {
  it('lists every call the server returns, with who, what and how much', async () => {
    vi.stubGlobal('fetch', server())

    renderPage()

    const row = (await screen.findByText('Mahmoud')).closest('tr')!
    expect(within(row).getByText('Sara')).toBeInTheDocument()
    expect(within(row).getByText('Ramallah')).toBeInTheDocument()
    expect(within(row).getByText('Order')).toBeInTheDocument()
    expect(within(row).getByText('1:55')).toBeInTheDocument()
    expect(within(row).getByText('Recorded')).toBeInTheDocument()
    expect(screen.getByText('2 calls')).toBeInTheDocument()
  })

  it('sends the filters to the server on Search, not on every keystroke', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await screen.findByText('Mahmoud')
    const before = searches(fetchMock).length

    fireEvent.change(screen.getByLabelText('Number or name'), { target: { value: '0599' } })
    fireEvent.change(screen.getByLabelText('Result'), { target: { value: 'Missed' } })
    expect(searches(fetchMock)).toHaveLength(before)

    fireEvent.click(screen.getByRole('button', { name: 'Search' }))

    await waitFor(() => expect(searches(fetchMock).length).toBe(before + 1))
    const sent = new URL(searches(fetchMock).at(-1)!, 'http://x')
    expect(sent.searchParams.get('q')).toBe('0599')
    expect(sent.searchParams.get('status')).toBe('Missed')
    expect(sent.searchParams.get('page')).toBe('1')
  })

  it('sends a day range as the start of the first day and the start of the day after the last', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await screen.findByText('Mahmoud')
    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-09-20' } })
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-09-24' } })
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))

    await waitFor(() => expect(searches(fetchMock).some((u) => u.includes('from='))).toBe(true))
    const sent = new URL(searches(fetchMock).at(-1)!, 'http://x')
    expect(new Date(sent.searchParams.get('from')!)).toEqual(new Date(2026, 8, 20))
    expect(new Date(sent.searchParams.get('to')!)).toEqual(new Date(2026, 8, 25))
  })

  it('opens a call under its own row on double-click, not at the top of the page', async () => {
    vi.stubGlobal('fetch', server())

    renderPage()
    const row = (await screen.findByText('Old caller')).closest('tr')!
    fireEvent.doubleClick(row)

    const details = await screen.findByRole('region', { name: 'Call details' })
    // The row straight after the one double-clicked holds the details.
    expect(row.nextElementSibling).toContainElement(details)
    expect(within(row).getByRole('button', { name: 'Close' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('opens a call with its recording, and marks where it was on hold', async () => {
    vi.stubGlobal('fetch', server())

    renderPage()
    const row = (await screen.findByText('Mahmoud')).closest('tr')!
    // The button, as a keyboard reaches it: double-click is only a shortcut.
    fireEvent.click(within(row).getByRole('button', { name: 'Open' }))

    const details = await screen.findByRole('region', { name: 'Call details' })
    expect(within(details).getByText('smashed-002')).toBeInTheDocument()

    // The hold the recorder wrote, as times and as a mark under the bar.
    expect(await within(details).findByText('0:01–0:02')).toBeInTheDocument()
    expect(within(details).getByText(/On hold:/)).toBeInTheDocument()
    expect(within(details).getByTestId('hold-band').children).toHaveLength(1)
    expect(within(details).getByRole('button', { name: 'Play' })).toBeInTheDocument()
  })

  it('says a recording has expired rather than fetching audio that is gone', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    fireEvent.doubleClick((await screen.findByText('Old caller')).closest('tr')!)

    expect(await screen.findByText(/deleted when its retention period ended/)).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([url]) => String(url).startsWith('/api/recordings/c2'))).toBe(false)
  })
})
