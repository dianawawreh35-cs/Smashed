import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import MistakesPage from './MistakesPage'
import type { Mistake } from '../api/mistakes'
import { setToken } from '../auth/token'
import i18n from '../i18n'
import { localDate } from '../lib/reportFilters'
import { jsonResponse, renderWithClient, routes } from '../test/http'

/** The mistakes made by the branches and the agents (S-65), against a stubbed `fetch`. */

const AGENT_MISTAKE: Mistake = {
  id: 'm1', occurredOn: '2026-10-01', branchId: 'b1', branchName: 'Ramallah', responsible: 'Agent',
  agentId: 'a1', agentDisplayName: 'Sara', value: 35.5, compensated: true, contactId: 'k1', contactName: 'Mahmoud',
  customerNumber: '0599123456', notes: 'Wrong burger sent', createdByDisplayName: 'Supervisor',
  createdAt: '2026-10-01T10:00:00Z', updatedAt: '2026-10-01T10:00:00Z',
}

const BRANCH_MISTAKE: Mistake = {
  ...AGENT_MISTAKE, id: 'm2', responsible: 'Branch', agentId: null, agentDisplayName: null, value: null, compensated: false,
  contactId: null, contactName: null, customerNumber: '0598000000', notes: 'Opened late',
}

const USERS = [
  { id: 'a1', login: 'sara', displayName: 'Sara', role: 'Agent', isActive: true },
  { id: 'a2', login: 'old', displayName: 'Gone', role: 'Agent', isActive: false },
  { id: 's1', login: 'supervisor', displayName: 'Supervisor', role: 'Supervisor', isActive: true },
]

const BRANCHES = [{ id: 'b1', name: 'Ramallah', isActive: true }, { id: 'b2', name: 'Nablus', isActive: true }]

function server(overrides: Parameters<typeof routes>[0] = []) {
  return vi.fn(routes([
    ...overrides,
    ['/api/mistakes/export', () => ({
      ok: true, status: 200, statusText: 'OK', headers: new Headers({ 'content-type': 'text/csv' }),
      blob: async () => new Blob(['Date']),
    } as unknown as Response)],
    ['/api/mistakes', () => jsonResponse({ rows: [AGENT_MISTAKE, BRANCH_MISTAKE], total: 2, totalValue: 35.5, page: 1, pageSize: 50 })],
    ['/api/users', () => jsonResponse(USERS)],
    ['/api/branches', () => jsonResponse(BRANCHES)],
    ['/api/contacts/by-phone', () => jsonResponse({ title: 'Not found' }, 404)],
    ['/api/contacts', () => jsonResponse([])],
  ]))
}

const calls = (fetchMock: ReturnType<typeof vi.fn>, prefix: string) =>
  fetchMock.mock.calls.filter(([url]) => String(url).startsWith(prefix))

async function openNewForm() {
  fireEvent.click(await screen.findByRole('button', { name: 'Record a mistake' }))
  return screen.getByRole('heading', { name: 'New mistake' }).closest('form')!
}

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
  URL.createObjectURL = vi.fn(() => 'blob:mistakes')
  URL.revokeObjectURL = vi.fn()
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('mistakes page', () => {
  it('lists the mistakes with the branch, who is responsible, the customer and the total value', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<MistakesPage />)

    const agentRow = (await screen.findByText('Wrong burger sent')).closest('tr')!
    expect(within(agentRow).getByText('An agent')).toBeInTheDocument()
    expect(within(agentRow).getByText('Sara')).toBeInTheDocument()
    expect(within(agentRow).getByText('Mahmoud')).toBeInTheDocument()
    expect(within(agentRow).getByText('35.50')).toBeInTheDocument()
    expect(within(agentRow).getByText('Compensated')).toBeInTheDocument()

    const branchRow = screen.getByText('Opened late').closest('tr')!
    expect(within(branchRow).getByText('The branch')).toBeInTheDocument()
    expect(within(branchRow).getByText('Not a saved customer')).toBeInTheDocument()
    expect(within(branchRow).getByText('Not compensated')).toBeInTheDocument()

    expect(screen.getByText(/2 mistakes/)).toBeInTheDocument()
    expect(screen.getByText(/Total value/)).toHaveTextContent('35.50')

    // Opens on today, as every date filter does.
    const today = localDate(new Date())
    expect(String(calls(fetchMock, '/api/mistakes')[0][0])).toContain(`from=${today}&to=${today}`)
  })

  it('names no agent for a branch mistake, and sends none', async () => {
    const fetchMock = server([['/api/mistakes', (_url, init) =>
      init?.method === 'POST' ? jsonResponse(BRANCH_MISTAKE) : jsonResponse({ rows: [], total: 0, totalValue: 0, page: 1, pageSize: 50 })]])
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<MistakesPage />)

    const form = await openNewForm()
    const agent = within(form).getByLabelText('Agent')
    await within(agent).findByRole('option', { name: 'Sara' })
    expect(within(agent).queryByRole('option', { name: /Gone/ })).toBeNull()

    fireEvent.change(within(form).getByLabelText('Branch'), { target: { value: 'b1' } })
    fireEvent.change(agent, { target: { value: 'a1' } })
    fireEvent.click(within(form).getByLabelText('The branch'))
    expect(agent).toBeDisabled()
    expect(agent).toHaveValue('')

    const save = within(form).getByRole('button', { name: 'Save' })
    expect(save).toBeDisabled()
    fireEvent.change(within(form).getByLabelText(/^Notes/), { target: { value: 'Opened late' } })
    fireEvent.click(save)

    await waitFor(() => expect(calls(fetchMock, '/api/mistakes').some(([, init]) => init?.method === 'POST')).toBe(true))
    const [, init] = calls(fetchMock, '/api/mistakes').find(([, i]) => i?.method === 'POST')!
    expect(JSON.parse(String(init!.body))).toMatchObject({
      branchId: 'b1', responsible: 'Branch', agentId: null, value: null, customerNumber: null, notes: 'Opened late',
      compensated: false,
    })
  })

  it('sends the compensated tick, and filters on it', async () => {
    const fetchMock = server([['/api/mistakes', (_url, init) =>
      init?.method === 'POST' ? jsonResponse(BRANCH_MISTAKE) : jsonResponse({ rows: [], total: 0, totalValue: 0, page: 1, pageSize: 50 })]])
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<MistakesPage />)

    const form = await openNewForm()
    fireEvent.change(within(form).getByLabelText('Branch'), { target: { value: 'b1' } })
    fireEvent.click(within(form).getByLabelText('The branch'))
    fireEvent.change(within(form).getByLabelText(/^Notes/), { target: { value: 'Cold fries' } })
    fireEvent.click(within(form).getByLabelText('The customer has been compensated'))
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(calls(fetchMock, '/api/mistakes').some(([, init]) => init?.method === 'POST')).toBe(true))
    const [, init] = calls(fetchMock, '/api/mistakes').find(([, i]) => i?.method === 'POST')!
    expect(JSON.parse(String(init!.body))).toMatchObject({ compensated: true })

    const filters = screen.getByRole('form', { name: 'Filters' })
    fireEvent.change(within(filters).getByLabelText('Compensated'), { target: { value: 'true' } })
    fireEvent.click(within(filters).getByRole('button', { name: 'Search' }))
    await waitFor(() => expect(calls(fetchMock, '/api/mistakes?').some(([url]) => String(url).includes('compensated=true'))).toBe(true))
  })

  it('finds a customer by name, and picking one puts their number in the box', async () => {
    const fetchMock = server([['/api/contacts?', () => jsonResponse([
      { id: 'k1', name: 'Mahmoud Saleh', address: null, isVip: false, isBlocked: false, flagReason: null, phones: ['0599123456'] },
    ])]])
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<MistakesPage />)

    const form = await openNewForm()
    fireEvent.change(within(form).getByLabelText('Branch'), { target: { value: 'b1' } })
    fireEvent.click(within(form).getByLabelText('The branch'))
    fireEvent.change(within(form).getByLabelText(/^Notes/), { target: { value: 'Cold fries' } })

    const customer = within(form).getByLabelText('Customer: phone number or name')
    fireEvent.change(customer, { target: { value: 'Mahm' } })

    // A name is not yet a customer: nothing to save it by until one is picked.
    expect(within(form).getByRole('button', { name: 'Save' })).toBeDisabled()

    fireEvent.click(await within(form).findByRole('button', { name: /Mahmoud Saleh/ }))
    expect(customer).toHaveValue('0599123456')
    expect(within(form).getByRole('button', { name: 'Save' })).toBeEnabled()
    expect(String(calls(fetchMock, '/api/contacts?')[0][0])).toContain('q=Mahm')
  })

  it('exports every match from the server, in the language on screen', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<MistakesPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Export all 2 (Excel)' }))

    await waitFor(() => expect(calls(fetchMock, '/api/mistakes/export')).toHaveLength(1))
    expect(String(calls(fetchMock, '/api/mistakes/export')[0][0])).toContain('lang=en')
    await waitFor(() => expect(URL.createObjectURL).toHaveBeenCalled())
  })
})
