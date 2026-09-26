import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import DashboardPage from './DashboardPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/** The dashboard (S-20): today's figures as the server counted them, and the period sent for the charts. */

const TODAY = {
  communications: 42, calls: 37, messages: 5,
  byType: [{ typeName: 'Order', labelAr: 'طلب', labelEn: 'Order', count: 20 }],
  byChannel: [{ key: 'ch-phone', label: 'Phone', count: 37 }, { key: 'ch-wa', label: 'WhatsApp', count: 5 }],
  orders: 20, orderValue: 1234.5, complaints: 3, missed: 4, unclassified: 6, agentsOnline: 2, abandoned: 5,
}
const PERIOD = {
  perDay: [{ key: '2026-09-24', label: '2026-09-24', count: 30 }, { key: '2026-09-25', label: '2026-09-25', count: 42 }],
  perType: TODAY.byType,
  perChannel: TODAY.byChannel,
  perHour: Array.from({ length: 24 }, (_, h) => ({ key: String(h).padStart(2, '0'), label: `${String(h).padStart(2, '0')}:00`, count: h === 19 ? 9 : 0 })),
}

function jsonResponse(body: unknown) {
  return {
    ok: true, status: 200, statusText: '200',
    headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response
}

function server() {
  return vi.fn().mockImplementation(async (url: string) => {
    if (url.startsWith('/api/reports/dashboard/today')) return jsonResponse(TODAY)
    if (url.startsWith('/api/reports/dashboard/period')) return jsonResponse(PERIOD)
    return jsonResponse([])
  })
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <DashboardPage />
    </QueryClientProvider>,
  )
}

const tile = (name: string) => screen.getByRole('group', { name })

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('dashboard', () => {
  it('shows today’s figures as the server counted them', async () => {
    vi.stubGlobal('fetch', server())
    renderPage()

    expect(await within(tile('Communications')).findByText('42')).toBeInTheDocument()
    expect(within(tile('Communications')).getByText('37 calls, 5 messages')).toBeInTheDocument()
    expect(within(tile('Orders')).getByText('20')).toBeInTheDocument()
    expect(within(tile('Orders')).getByText('worth 1,234.50')).toBeInTheDocument()
    expect(within(tile('Missed calls')).getByText('4')).toBeInTheDocument()
    expect(within(tile('Unclassified calls')).getByText('6')).toBeInTheDocument()
    expect(within(tile('Agents online')).getByText('2')).toBeInTheDocument()

    const channels = screen.getByRole('region', { name: 'Today by channel' })
    expect(within(within(channels).getByText('WhatsApp').closest('tr')!).getByText('5')).toBeInTheDocument()

    // The four charts, once the period arrives.
    await waitFor(() => expect(screen.getAllByTestId('report-chart')).toHaveLength(4))
  })

  it('sends the chosen period for the charts, and nothing else', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderPage()
    await screen.findByText('37 calls, 5 messages')

    // Period only: no agent, branch or type filter on the dashboard.
    expect(screen.queryByLabelText('Branch')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Custom' }))
    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-09-01' } })
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-09-10' } })

    await waitFor(() => {
      const sent = fetchMock.mock.calls.map(([u]) => String(u)).filter((u) => u.startsWith('/api/reports/dashboard/period')).at(-1)!
      const url = new URL(sent, 'http://x')
      expect(new Date(url.searchParams.get('from')!)).toEqual(new Date(2026, 8, 1))
      expect(new Date(url.searchParams.get('to')!)).toEqual(new Date(2026, 8, 11))
    })
  })
})
