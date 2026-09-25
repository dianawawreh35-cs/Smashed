import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import ChannelsCard from './ChannelsCard'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/** Channel management (S-41), against a stubbed `fetch`. */

const CHANNELS = [
  { id: 'ch-phone', name: 'Phone', isSystem: true, sortOrder: 0, isActive: true, inUse: true },
  { id: 'ch-wa', name: 'WhatsApp', isSystem: false, sortOrder: 10, isActive: true, inUse: true },
  { id: 'ch-ig', name: 'Instagram', isSystem: false, sortOrder: 20, isActive: false, inUse: false },
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

function server(onWrite?: (url: string, init: RequestInit) => Response | undefined) {
  return vi.fn().mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method && init.method !== 'GET') {
      const answer = onWrite?.(url, init)
      if (answer) return answer
      return jsonResponse(CHANNELS[1])
    }
    if (url.startsWith('/api/channels')) return jsonResponse(CHANNELS)
    return jsonResponse([])
  })
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ChannelsCard />
    </QueryClientProvider>,
  )
}

const writes = (fetchMock: ReturnType<typeof vi.fn>) =>
  fetchMock.mock.calls
    .filter(([, init]) => (init as RequestInit | undefined)?.method && (init as RequestInit).method !== 'GET')
    .map(([url, init]) => ({ url: String(url), method: (init as RequestInit).method, body: JSON.parse(String((init as RequestInit).body)) }))

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('channels card', () => {
  it('lists every channel, hidden ones too, and says which are hidden', async () => {
    vi.stubGlobal('fetch', server())
    renderCard()

    expect(await screen.findByDisplayValue('WhatsApp')).toBeInTheDocument()
    const instagram = screen.getByDisplayValue('Instagram').closest('tr')!
    expect(within(instagram).getByText('Hidden')).toBeInTheDocument()
    expect(within(instagram).getByRole('checkbox')).not.toBeChecked()
  })

  it('does not let Phone be renamed or hidden, and says why', async () => {
    vi.stubGlobal('fetch', server())
    renderCard()

    await screen.findByDisplayValue('WhatsApp')
    const phone = screen.getByText('Phone').closest('tr')!
    expect(within(phone).queryByRole('textbox')).not.toBeInTheDocument()
    expect(within(phone).queryByRole('checkbox')).not.toBeInTheDocument()
    expect(within(phone).getByText(/cannot be renamed or hidden/)).toBeInTheDocument()
    // It can still be moved.
    expect(within(phone).getByRole('button', { name: 'Move down Phone' })).toBeEnabled()
  })

  it('renames a channel with a PUT to its id', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderCard()

    const input = await screen.findByDisplayValue('WhatsApp')
    fireEvent.change(input, { target: { value: 'WhatsApp Business' } })
    fireEvent.blur(input)

    await waitFor(() => expect(writes(fetchMock)).toHaveLength(1))
    const [put] = writes(fetchMock)
    expect(put.method).toBe('PUT')
    expect(put.url).toBe('/api/channels/ch-wa')
    expect(put.body).toEqual({ name: 'WhatsApp Business', sortOrder: 10, isActive: true })
  })

  it('hides a channel by unticking it, keeping its name and order', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderCard()

    await screen.findByDisplayValue('WhatsApp')
    fireEvent.click(screen.getByRole('checkbox', { name: 'Status WhatsApp' }))

    await waitFor(() => expect(writes(fetchMock)).toHaveLength(1))
    expect(writes(fetchMock)[0].body).toEqual({ name: 'WhatsApp', sortOrder: 10, isActive: false })
  })

  it('moves a channel by swapping its order with its neighbour', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderCard()

    await screen.findByDisplayValue('WhatsApp')
    fireEvent.click(screen.getByRole('button', { name: 'Move up Instagram' }))

    await waitFor(() => expect(writes(fetchMock)).toHaveLength(2))
    const sent = Object.fromEntries(writes(fetchMock).map((w) => [w.url, w.body.sortOrder]))
    expect(sent['/api/channels/ch-ig']).toBe(10)
    expect(sent['/api/channels/ch-wa']).toBe(20)
  })

  it('adds a channel at the end of the list', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderCard()

    await screen.findByDisplayValue('WhatsApp')
    fireEvent.change(screen.getByLabelText('New channel'), { target: { value: 'Wheels' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add channel' }))

    await waitFor(() => expect(writes(fetchMock)).toHaveLength(1))
    const [post] = writes(fetchMock)
    expect(post.method).toBe('POST')
    expect(post.url).toBe('/api/channels')
    expect(post.body).toEqual({ name: 'Wheels', sortOrder: 30, isActive: true })
  })

  it('shows the server’s refusal in words', async () => {
    vi.stubGlobal('fetch', server(() =>
      jsonResponse({ title: 'Channel not saved', status: 400, code: 'bad_name' }, 400)))
    renderCard()

    const input = await screen.findByDisplayValue('Instagram')
    fireEvent.change(input, { target: { value: 'WhatsApp' } })
    fireEvent.blur(input)

    expect(await screen.findByRole('alert')).toHaveTextContent('no other channel may already have it')
  })
})
