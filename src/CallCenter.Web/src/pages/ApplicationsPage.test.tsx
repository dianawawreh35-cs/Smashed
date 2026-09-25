import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import ApplicationsPage from './ApplicationsPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/** The supervisor's page of messages, and one opened (A-70), against a stubbed `fetch`. */

const MESSAGE = {
  id: 'm1',
  kind: 'App',
  startedAt: '2026-09-25T10:15:00Z',
  direction: 'None',
  status: 'Logged',
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
  orderValue: 62,
  durationSec: null,
  notes: 'extra sauce',
  isClassified: true,
  hasRecording: false,
  recordingExpired: false,
  channelId: 'ch-wa',
  channelName: 'WhatsApp',
}

const UNCLASSIFIED = {
  ...MESSAGE, id: 'm2', contactName: 'Lina', typeName: null, typeLabelAr: null, typeLabelEn: null,
  orderValue: null, notes: null, isClassified: false, channelId: 'ch-ig', channelName: 'Instagram',
}

const CHANNELS = [
  { id: 'ch-phone', name: 'Phone', isSystem: true, sortOrder: 0, isActive: true, inUse: true },
  { id: 'ch-wa', name: 'WhatsApp', isSystem: false, sortOrder: 10, isActive: true, inUse: true },
  { id: 'ch-ig', name: 'Instagram', isSystem: false, sortOrder: 20, isActive: true, inUse: false },
]

const FORM = {
  version: 1, direction: 'None', branches: [{ id: 'b1', name: 'Ramallah' }],
  types: [{ id: 't1', name: 'Order', labelAr: 'طلب', labelEn: 'Order', colour: null,
    isSystem: true, sortOrder: 1, isActive: true, inUse: true }],
  definition: { fields: [{ key: 'type', kind: 'type', required: true, label: { en: 'Type' } }] },
}

const CLASSIFICATION = {
  typeId: 't1', typeName: 'Order', typeLabelAr: 'طلب', typeLabelEn: 'Order',
  branchId: 'b1', branchName: 'Ramallah', orderValue: 62, notes: 'extra sauce', followUp: false,
  resolved: null, formVersion: 1, customValues: {}, classifiedByName: 'Sara',
  classifiedAt: '2026-09-25T10:16:00Z', updatedByName: null, updatedAt: null,
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

/** The server, by URL. */
function server() {
  return vi.fn().mockImplementation(async (url: string) => {
    if (url.startsWith('/api/communications/search')) return jsonResponse({ rows: [MESSAGE, UNCLASSIFIED], total: 2, page: 1, pageSize: 50 })
    if (url === '/api/communications/m1') return jsonResponse({ summary: MESSAGE, remoteName: null, answeredAt: null, endedAt: null, waitSec: null, queueName: null, extension: null, callNotes: null })
    if (url === '/api/communications/m2') return jsonResponse({ summary: UNCLASSIFIED, remoteName: null, answeredAt: null, endedAt: null, waitSec: null, queueName: null, extension: null, callNotes: null })
    if (url.startsWith('/api/channels')) return jsonResponse(CHANNELS)
    if (url.startsWith('/api/classifications/form')) return jsonResponse(FORM)
    if (url === '/api/classifications/m1') return jsonResponse(CLASSIFICATION)
    if (url === '/api/classifications/m1/history') return jsonResponse([])
    return jsonResponse([])
  })
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <ApplicationsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const searches = (fetchMock: ReturnType<typeof vi.fn>) =>
  fetchMock.mock.calls.map(([url]) => String(url)).filter((url) => url.startsWith('/api/communications/search'))

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('applications page', () => {
  it('lists every message the server returns, with its channel, who, what and how much', async () => {
    vi.stubGlobal('fetch', server())

    renderPage()

    const row = (await screen.findByText('Mahmoud')).closest('tr')!
    expect(within(row).getByText('WhatsApp')).toBeInTheDocument()
    expect(within(row).getByText('Sara')).toBeInTheDocument()
    expect(within(row).getByText('Order')).toBeInTheDocument()
    expect(within(row).getByText('Ramallah')).toBeInTheDocument()
    expect(within(row).getByText('62')).toBeInTheDocument()
    expect(screen.getByText('2 messages')).toBeInTheDocument()
    // Nothing a phone call has and a message does not.
    expect(screen.queryByText('Recording')).not.toBeInTheDocument()
    expect(screen.queryByText('Duration')).not.toBeInTheDocument()
  })

  it('asks the server for messages only, from the first load', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await screen.findByText('Mahmoud')

    const first = new URL(searches(fetchMock)[0], 'http://x')
    expect(first.searchParams.get('kind')).toBe('App')
  })

  it('sends kind=App and the chosen channel on Search', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await screen.findByText('Mahmoud')
    // The channel list has to arrive before it can be chosen.
    await waitFor(() => expect(screen.getByRole('option', { name: 'Instagram' })).toBeInTheDocument())
    const before = searches(fetchMock).length

    fireEvent.change(screen.getByLabelText('Channel'), { target: { value: 'ch-ig' } })
    fireEvent.change(screen.getByLabelText('Number or name'), { target: { value: 'Lina' } })
    expect(searches(fetchMock)).toHaveLength(before)

    fireEvent.click(screen.getByRole('button', { name: 'Search' }))

    await waitFor(() => expect(searches(fetchMock).length).toBe(before + 1))
    const sent = new URL(searches(fetchMock).at(-1)!, 'http://x')
    expect(sent.searchParams.get('kind')).toBe('App')
    expect(sent.searchParams.get('channelId')).toBe('ch-ig')
    expect(sent.searchParams.get('q')).toBe('Lina')
    expect(sent.searchParams.get('page')).toBe('1')
  })

  it('does not offer Phone as a channel: a phone conversation is a call', async () => {
    vi.stubGlobal('fetch', server())

    renderPage()
    await waitFor(() => expect(screen.getByRole('option', { name: 'WhatsApp' })).toBeInTheDocument())
    expect(screen.queryByRole('option', { name: 'Phone' })).not.toBeInTheDocument()
  })

  it('opens a message under its own row, with its channel and classification, and Edit', async () => {
    vi.stubGlobal('fetch', server())

    renderPage()
    const row = (await screen.findByText('Mahmoud')).closest('tr')!
    fireEvent.doubleClick(row)

    const details = await screen.findByRole('region', { name: 'Message details' })
    expect(row.nextElementSibling).toContainElement(details)
    // Facts a message has; none of a call's phone facts.
    expect(within(details).getAllByText('WhatsApp').length).toBeGreaterThan(0)
    expect(within(details).getByText('Message')).toBeInTheDocument()
    expect(within(details).queryByText('Extension')).not.toBeInTheDocument()
    expect(within(details).queryByText('Queue')).not.toBeInTheDocument()
    expect(within(details).queryByText('Recording')).not.toBeInTheDocument()
    // Its classification, and the button to change it.
    expect(await within(details).findByText('extra sauce')).toBeInTheDocument()
    expect(within(details).getByRole('button', { name: 'Edit' })).toBeInTheDocument()
  })

  it('offers Classify on an unclassified message, using the messages form (direction None)', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    const row = (await screen.findByText('Lina')).closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Open' }))

    const details = await screen.findByRole('region', { name: 'Message details' })
    expect(within(details).getByText('Not classified')).toBeInTheDocument()
    fireEvent.click(within(details).getByRole('button', { name: 'Classify' }))

    expect(await within(details).findByLabelText('Type *')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([url]) => String(url) === '/api/classifications/form?direction=None')).toBe(true)
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('direction=In'))).toBe(false)
  })
})
