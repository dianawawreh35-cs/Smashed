import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import DeliveryPage from './DeliveryPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'
import { jsonResponse, renderWithClient, routes } from '../test/http'

/** Delivery areas (S-58), against a stubbed `fetch`. */

const KAFR_AQAB = {
  id: 'd1', name: 'Kafr Aqab', branchId: 'b1', branchName: 'Ramallah', price: 7.5, isActive: true,
}

function stub(onDelete: () => Response = () => jsonResponse(null, 204)) {
  const fetchMock = vi.fn(routes([
    ['/branches', () => jsonResponse([{ id: 'b1', name: 'Ramallah', isActive: true }])],
    ['/delivery-areas/d1', (_, init) => (init?.method === 'DELETE' ? onDelete() : jsonResponse(KAFR_AQAB))],
    ['/delivery-areas', () => jsonResponse([KAFR_AQAB])],
  ]))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const deletes = (fetchMock: ReturnType<typeof vi.fn>) =>
  fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method === 'DELETE')

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('delivery page', () => {
  it('shows a price in the money format the reports use', async () => {
    // "7.5" beside "7.50" elsewhere read as two different prices.
    stub()
    renderWithClient(<DeliveryPage />)

    const row = (await screen.findByText('Kafr Aqab')).closest('tr')!
    expect(within(row).getByText('7.50')).toHaveAttribute('dir', 'ltr')
  })

  it('asks before removing, and removes on the second click (M-W07)', async () => {
    const fetchMock = stub()
    renderWithClient(<DeliveryPage />)

    const row = (await screen.findByText('Kafr Aqab')).closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Remove' }))

    // Nothing sent yet: the button now asks.
    expect(deletes(fetchMock)).toHaveLength(0)
    fireEvent.click(within(row).getByRole('button', { name: 'Confirm remove' }))

    await waitFor(() => expect(deletes(fetchMock)).toHaveLength(1))
  })

  it('does not take a hurried double-click as the confirmation', async () => {
    const fetchMock = stub()
    renderWithClient(<DeliveryPage />)

    const row = (await screen.findByText('Kafr Aqab')).closest('tr')!
    const button = within(row).getByRole('button', { name: 'Remove' })
    fireEvent.click(button, { detail: 1 })
    fireEvent.click(button, { detail: 2 })

    expect(within(row).getByRole('button', { name: 'Confirm remove' })).toBeInTheDocument()
    expect(deletes(fetchMock)).toHaveLength(0)
  })

  it('goes back to Remove on Escape, without removing', async () => {
    const fetchMock = stub()
    renderWithClient(<DeliveryPage />)

    const row = (await screen.findByText('Kafr Aqab')).closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Remove' }))
    fireEvent.keyDown(within(row).getByRole('button', { name: 'Confirm remove' }), { key: 'Escape' })

    expect(within(row).getByRole('button', { name: 'Remove' })).toBeInTheDocument()
    expect(deletes(fetchMock)).toHaveLength(0)
  })

  it('says why when the server refuses the removal', async () => {
    // It used to say nothing, and the row simply stayed.
    stub(() => jsonResponse({ code: 'area_not_found' }, 404))
    renderWithClient(<DeliveryPage />)

    const row = (await screen.findByText('Kafr Aqab')).closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Remove' }))
    fireEvent.click(within(row).getByRole('button', { name: 'Confirm remove' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('That area no longer exists.')
  })

  it('counts the areas in words that agree with the number (M-W10)', async () => {
    stub()
    renderWithClient(<DeliveryPage />)

    expect(await screen.findByText('1 area shown')).toBeInTheDocument()
  })
})
