import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import WebsitesPage from './WebsitesPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/** The websites inside the Agent App (A-88), against a stubbed `fetch`. */

const POS = {
  id: 'w-pos',
  nameAr: 'نقطة البيع',
  nameEn: 'POS',
  url: 'https://smashed-ps.com/app',
  login: 'own',
  username: null,
  hasPassword: false,
  alertsWithSound: false,
  cartUrl: 'https://smashed-ps.com/app/cart/{number}',
  usernameSelector: null,
  passwordSelector: null,
  submitSelector: null,
  sortOrder: 0,
  isActive: true,
}

const ORDERS = {
  id: 'w-orders',
  nameAr: 'الطلبات ١',
  nameEn: 'Orders 1',
  url: 'https://partners.example.com/login',
  login: 'shared',
  username: 'branch1@example.com',
  hasPassword: true,
  alertsWithSound: true,
  cartUrl: null,
  usernameSelector: null,
  passwordSelector: null,
  submitSelector: null,
  sortOrder: 10,
  isActive: true,
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

function server(onWrite?: (url: string, init: RequestInit) => Response | undefined) {
  return vi.fn().mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method && init.method !== 'GET') {
      const answer = onWrite?.(url, init)
      if (answer) return answer
      return jsonResponse(ORDERS)
    }
    if (url.startsWith('/api/websites')) return jsonResponse([POS, ORDERS])
    return jsonResponse([])
  })
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <WebsitesPage />
    </QueryClientProvider>,
  )
}

const writes = (fetchMock: ReturnType<typeof vi.fn>) =>
  fetchMock.mock.calls
    .filter(([, init]) => (init as RequestInit | undefined)?.method && (init as RequestInit).method !== 'GET')
    .map(([url, init]) => ({
      url: String(url),
      method: (init as RequestInit).method,
      body: (init as RequestInit).body ? JSON.parse(String((init as RequestInit).body)) : undefined,
    }))

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('websites page', () => {
  it('lists each website with its login, and says a password is stored without showing one', async () => {
    vi.stubGlobal('fetch', server())
    renderPage()

    const orders = (await screen.findByText('Orders 1')).closest('tr')!
    expect(within(orders).getByText('branch1@example.com')).toBeInTheDocument()
    expect(within(orders).getByText('Password stored')).toBeInTheDocument()
    expect(within(orders).getByText('Alerts with sound')).toBeInTheDocument()

    const pos = screen.getByText('POS').closest('tr')!
    expect(within(pos).getByText("Each agent's own login")).toBeInTheDocument()
    expect(within(pos).getByText("Opens the caller's cart")).toBeInTheDocument()
  })

  it('shows the Arabic name first in Arabic', async () => {
    await i18n.changeLanguage('ar')
    vi.stubGlobal('fetch', server())
    renderPage()

    const row = (await screen.findByText('الطلبات ١')).closest('tr')!
    expect(within(row).getByText('Orders 1')).toHaveClass('text-xs')
  })

  it('adds a website with a shared login, sending both names and the password', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderPage()

    await screen.findByText('Orders 1')
    fireEvent.click(screen.getByRole('button', { name: 'Add website' }))

    const form = screen.getByRole('form', { name: 'New website' })
    fireEvent.change(within(form).getByLabelText('Name in Arabic'), { target: { value: 'الطلبات ٢' } })
    fireEvent.change(within(form).getByLabelText('Name in English'), { target: { value: 'Orders 2' } })
    fireEvent.change(within(form).getByLabelText('Address'), { target: { value: 'https://partners.example.com' } })
    fireEvent.change(within(form).getByLabelText('Username'), { target: { value: 'branch2@example.com' } })
    fireEvent.change(within(form).getByLabelText('Password'), { target: { value: 'pa55' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(writes(fetchMock)).toHaveLength(1))
    const [write] = writes(fetchMock)
    expect(write.method).toBe('POST')
    expect(write.body).toMatchObject({
      nameAr: 'الطلبات ٢',
      nameEn: 'Orders 2',
      login: 'shared',
      username: 'branch2@example.com',
      password: 'pa55',
      alertsWithSound: true,
      isActive: true,
    })
  })

  it('keeps the stored password when an edit leaves the box empty, and removes it only when ticked', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderPage()

    const row = (await screen.findByText('Orders 1')).closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Edit' }))

    let form = screen.getByRole('form', { name: 'Edit website' })
    expect(within(form).getByLabelText('Password')).toHaveValue('')
    fireEvent.change(within(form).getByLabelText('Name in English'), { target: { value: 'Orders One' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(writes(fetchMock)).toHaveLength(1))
    expect(writes(fetchMock)[0]).toMatchObject({ method: 'PUT', url: '/api/websites/w-orders' })
    expect(writes(fetchMock)[0].body).toMatchObject({ nameEn: 'Orders One', password: null })

    fireEvent.click(within((await screen.findByText('Orders 1')).closest('tr')!).getByRole('button', { name: 'Edit' }))
    form = screen.getByRole('form', { name: 'Edit website' })
    fireEvent.click(within(form).getByLabelText('Remove the stored password'))
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(writes(fetchMock)).toHaveLength(2))
    expect(writes(fetchMock)[1].body).toMatchObject({ password: '' })
  })

  it("says why a save was refused, in the reader's language", async () => {
    vi.stubGlobal(
      'fetch',
      server(() => jsonResponse({ title: 'Website not saved', status: 409, code: 'cart_taken' }, 409)),
    )
    renderPage()

    const row = (await screen.findByText('Orders 1')).closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Edit' }))
    const form = screen.getByRole('form', { name: 'Edit website' })
    fireEvent.change(within(form).getByLabelText("The caller's cart (POS only)"), {
      target: { value: 'https://x.example.com/{number}' },
    })
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))

    expect(await within(form).findByRole('alert')).toHaveTextContent('Another website already opens the caller')
  })

  it('removes a website only on the second press', async () => {
    const fetchMock = server((_, init) => (init.method === 'DELETE' ? jsonResponse(null, 204) : undefined))
    vi.stubGlobal('fetch', fetchMock)
    renderPage()

    const row = (await screen.findByText('Orders 1')).closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Edit' }))
    const form = screen.getByRole('form', { name: 'Edit website' })

    fireEvent.click(within(form).getByRole('button', { name: 'Remove' }))
    expect(writes(fetchMock)).toHaveLength(0)
    fireEvent.click(within(form).getByRole('button', { name: /confirm/i }))

    await waitFor(() => expect(writes(fetchMock)).toEqual([{ url: '/api/websites/w-orders', method: 'DELETE', body: undefined }]))
  })
})
