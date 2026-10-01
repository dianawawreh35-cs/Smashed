import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import MistakeReportsPage from './MistakeReportsPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'
import { localDate } from '../lib/reportFilters'
import { jsonResponse, renderWithClient, routes } from '../test/http'

/** The mistakes report (R-23), against a stubbed `fetch`. */

const BY_BRANCH = [
  { branchId: 'b1', branch: 'Ramallah', mistakes: 3, branchOwn: 1, byAgents: 2, value: 15 },
  { branchId: 'b2', branch: 'Nablus', mistakes: 1, branchOwn: 1, byAgents: 0, value: 7 },
]
const BY_AGENT = [{ agentId: 'a1', agent: 'Sara', mistakes: 2, value: 15 }]
const TREND = [{ bucket: '2026-10-01', mistakes: 4, branchOwn: 2, byAgents: 2, value: 22 }]
const CUSTOMERS = [
  { contactId: 'k1', customer: 'Mahmoud', number: '0599123456', mistakes: 2, value: 13, last: '2026-10-01' },
  { contactId: null, customer: null, number: '0598000000', mistakes: 2, value: 0, last: '2026-09-30' },
]

function server() {
  return vi.fn(routes([
    ['/api/mistakes/reports/by-branch', () => jsonResponse(BY_BRANCH)],
    ['/api/mistakes/reports/by-agent', () => jsonResponse(BY_AGENT)],
    ['/api/mistakes/reports/trend', () => jsonResponse(TREND)],
    ['/api/mistakes/reports/repeat-customers', () => jsonResponse(CUSTOMERS)],
    ['/api/users', () => jsonResponse([{ id: 'a1', displayName: 'Sara', role: 'Agent', isActive: true }])],
    ['/api/branches', () => jsonResponse([{ id: 'b1', name: 'Ramallah', isActive: true }])],
  ]))
}

const card = (title: string) => screen.getByRole('region', { name: title })

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('mistakes report', () => {
  it('shows each branch with its own and its agents\' mistakes, and a total row', async () => {
    vi.stubGlobal('fetch', server())
    renderWithClient(<MistakeReportsPage />)

    const row = (await within(card('Per branch')).findByText('Ramallah')).closest('tr')!
    expect(within(row).getAllByText('3')).toHaveLength(1)
    expect(within(row).getByText('15.00')).toBeInTheDocument()

    const total = within(card('Per branch')).getByText('Total').closest('tr')!
    expect(within(total).getByText('4')).toBeInTheDocument()
  })

  it('lists the repeat customers, a number nobody has on file included', async () => {
    vi.stubGlobal('fetch', server())
    renderWithClient(<MistakeReportsPage />)

    const customers = card('Repeat customers')
    expect(await within(customers).findByText('Mahmoud')).toBeInTheDocument()
    expect(within(customers).getByText('Not a saved customer')).toBeInTheDocument()
    expect(within(customers).getByText('0598000000')).toBeInTheDocument()
  })

  it('opens on today, sends days, and asks again for a branch and another grouping', async () => {
    const fetchMock = server()
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<MistakeReportsPage />)
    await within(card('Per agent')).findByText('Sara')

    const today = localDate(new Date())
    const urls = () => fetchMock.mock.calls.map(([url]) => String(url))
    expect(urls().find((u) => u.includes('/by-branch'))).toContain(`from=${today}&to=${today}`)

    fireEvent.change(screen.getByLabelText('Branch'), { target: { value: 'b1' } })
    await waitFor(() => expect(urls().some((u) => u.includes('/by-agent') && u.includes('branchId=b1'))).toBe(true))

    fireEvent.change(within(card('Over time')).getByLabelText('Per'), { target: { value: 'month' } })
    await waitFor(() => expect(urls().some((u) => u.includes('/trend') && u.includes('groupBy=month'))).toBe(true))
  })
})
