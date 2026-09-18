import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import ContactsPage from './ContactsPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/** The shared contact list (A-60 to A-63), against a stubbed `fetch`. */

const AHMAD = {
  id: 'c1',
  name: 'Ahmad',
  address: 'Ramallah',
  isVip: false,
  isBlocked: false,
  phones: ['0599123456'],
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

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ContactsPage />
    </QueryClientProvider>,
  )
}

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('contacts page', () => {
  it('lists contacts and shows a VIP badge', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse([{ ...AHMAD, isVip: true }]),
    ))

    renderPage()

    expect(await screen.findByText('Ahmad')).toBeInTheDocument()
    expect(screen.getByText('VIP')).toBeInTheDocument()
  })

  it('passes the typed query to the server rather than filtering locally', async () => {
    // The server decides whether the text is a number or a name (A-61), so the
    // query has to reach it.
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    fireEvent.change(screen.getByLabelText('Search'), { target: { value: '0599' } })

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([url]) => String(url).includes('q=0599'))).toBe(true),
    )
  })

  it('shows a contact saved without a name as "No name"', async () => {
    // S-45 allows a bare number to be flagged before anyone knows who it is.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse([{ ...AHMAD, name: null }]),
    ))

    renderPage()

    expect(await screen.findByText('No name')).toBeInTheDocument()
  })

  it('sends every number typed, in order', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValue(jsonResponse({ id: 'c2' }))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Add contact' }))

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Sara' } })
    fireEvent.change(screen.getByLabelText('Phone number 1'), { target: { value: '0599111222' } })
    fireEvent.click(screen.getByRole('button', { name: '+ Add a number' }))
    fireEvent.change(screen.getByLabelText('Phone number 2'), { target: { value: '022987654' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')
      expect(post).toBeDefined()
      expect(JSON.parse(post![1].body).phones).toEqual(['0599111222', '022987654'])
    })
  })

  it('names the contact that already holds a duplicate number', async () => {
    // A-63: the answer has to be actionable, not just "already exists".
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValue(jsonResponse({
        code: 'duplicate_number',
        duplicate: { number: '970599123456', existingContactId: 'c1', existingContactName: 'Ahmad' },
      }, 409))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Add contact' }))
    fireEvent.change(screen.getByLabelText('Phone number 1'), { target: { value: '0599123456' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('already belongs to another contact')
    expect(alert).toHaveTextContent('Ahmad')
  })

  it('warns when the name already exists, without blocking the save', async () => {
    // A-63: a matching name is a prompt to look, never a refusal - two
    // customers may genuinely share a name.
    const fetchMock = vi.fn().mockImplementation((url: string) =>
      Promise.resolve(
        String(url).includes('by-name')
          ? jsonResponse([AHMAD])
          : jsonResponse([]),
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Add contact' }))
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Ahmad' } })
    fireEvent.change(screen.getByLabelText('Phone number 1'), { target: { value: '0599222333' } })

    expect(await screen.findByText(/already 1 contact with this name/i)).toBeInTheDocument()

    // The existing contact is shown with enough to recognise them by.
    expect(screen.getByText(/0599123456/)).toBeInTheDocument()

    // And saving is still allowed.
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('can add the number to the contact that already has the name', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string, init?: RequestInit) => {
      if (String(url).includes('by-name')) return Promise.resolve(jsonResponse([AHMAD]))
      if (init?.method === 'POST') return Promise.resolve(jsonResponse({ id: 'c1' }))
      return Promise.resolve(jsonResponse([]))
    })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Add contact' }))
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Ahmad' } })
    fireEvent.change(screen.getByLabelText('Phone number 1'), { target: { value: '0599222333' } })

    fireEvent.click(await screen.findByRole('button', { name: 'Add this number to them' }))

    await waitFor(() => {
      const post = fetchMock.mock.calls.find(
        ([url, init]) => String(url).includes('/phones') && init?.method === 'POST',
      )
      expect(post).toBeDefined()
      expect(String(post![0])).toBe('/api/contacts/c1/phones')
      expect(JSON.parse(post![1].body)).toEqual({ number: '0599222333' })
    })
  })

  it('will not save a contact with no number at all', async () => {
    // A contact with no number could never be matched to a caller.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])))

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Add contact' }))
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Sara' } })

    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })
})
