import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import FlagsPage from './FlagsPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/** VIP and blocked numbers (S-45), against a stubbed `fetch`. */

const BLOCKED = {
  id: 'c1',
  name: 'Ahmad',
  address: 'Ramallah',
  isVip: false,
  isBlocked: true,
  flagReason: 'Abusive on the phone',
  changedByDisplayName: 'Supervisor',
  changedAt: '2026-09-18T10:00:00Z',
  numbers: ['0599123456'],
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
      <FlagsPage />
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

describe('flags page', () => {
  it('lists a flagged contact with its reason and who set it', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([BLOCKED])))

    renderPage()

    expect(await screen.findByText('Ahmad')).toBeInTheDocument()

    // Scoped to the table: "Blocked" is also one of the form's radio labels.
    const list = within(screen.getByRole('table'))
    expect(list.getByText('Blocked')).toBeInTheDocument()
    expect(list.getByText('Abusive on the phone')).toBeInTheDocument()
    expect(list.getByText('Supervisor')).toBeInTheDocument()
  })

  it('shows a flagged bare number as such rather than as a blank name', async () => {
    // S-45 allows a nuisance number to be blocked before anyone knows who it
    // is. A blank cell would read as a bug.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse([{ ...BLOCKED, name: null }]),
    ))

    renderPage()

    expect(await screen.findByText('Number only, no contact')).toBeInTheDocument()
  })

  it('will not submit a flag with no reason', async () => {
    // The server refuses it too; this stops the round trip and the error
    // message for something the screen already knows.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])))

    renderPage()
    fireEvent.change(screen.getByLabelText('Phone number'), { target: { value: '0599123456' } })

    expect(screen.getByRole('button', { name: 'Apply flag' })).toBeDisabled()
  })

  it('sends the number, the chosen flag and the reason', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    fireEvent.change(screen.getByLabelText('Phone number'), { target: { value: '0599123456' } })
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Regular customer' } })
    fireEvent.click(screen.getByLabelText('VIP'))
    fireEvent.click(screen.getByRole('button', { name: 'Apply flag' }))

    await waitFor(() => {
      const post = fetchMock.mock.calls.find(
        ([url]) => String(url).includes('/contacts/flags/by-number'),
      )
      expect(post).toBeDefined()
      expect(JSON.parse(post![1].body)).toEqual({
        number: '0599123456',
        isVip: true,
        isBlocked: false,
        reason: 'Regular customer',
      })
    })
  })

  it('removes a flag by clearing both, not by a delete of its own', async () => {
    // One write path means one audit vocabulary: every change, including a
    // removal, lands in the log the same way (S-45).
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([BLOCKED]))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Remove flag' }))

    await waitFor(() => {
      const put = fetchMock.mock.calls.find(([url]) => String(url).includes('/contacts/c1/flags'))
      expect(put).toBeDefined()
      expect(put![1].method).toBe('PUT')
      expect(JSON.parse(put![1].body)).toEqual({ isVip: false, isBlocked: false, reason: null })
    })
  })
})
