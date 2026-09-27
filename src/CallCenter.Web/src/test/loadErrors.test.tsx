import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import ContactsPage from '../pages/ContactsPage'
import UsersPage from '../pages/UsersPage'
import DeliveryPage from '../pages/DeliveryPage'
import MenuPage from '../pages/MenuPage'
import CallsPage from '../pages/CallsPage'
import SettingsPage from '../pages/SettingsPage'
import DashboardPage from '../pages/DashboardPage'
import ClassificationPage from '../pages/ClassificationPage'
import ApplicationReportsPage from '../pages/ApplicationReportsPage'
import ContactHistory from '../components/ContactHistory'
import ChannelsCard from '../components/ChannelsCard'
import QueueSwitchCard from '../components/QueueSwitchCard'
import CallDetails from '../components/CallDetails'
import FlagDialog from '../components/FlagDialog'
import type { CallRow } from '../api/calls'
import { setToken } from '../auth/token'
import i18n from '../i18n'
import { jsonResponse, renderWithClient, routes, serverDown } from './http'

/**
 * M-W03: a request that failed must never look like "there is nothing".
 *
 * One test per screen, with the server stopped (fetch rejects, as it does when
 * nothing answers): the failure is said, with a Retry, and the screen's empty
 * state is not shown. Before 27 Sep most of these fell through to the empty
 * state, and the contact search offered to flag a number "nobody has".
 */

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const down = () => vi.stubGlobal('fetch', vi.fn(serverDown))
const retryButtons = () => screen.getAllByRole('button', { name: 'Try again' })

const ANSWERED: CallRow = {
  id: 'k1', kind: 'Call', startedAt: '2026-09-27T08:00:00Z', direction: 'In', status: 'Answered',
  agentId: 'a1', agentDisplayName: 'Sara', contactId: 'c1', contactName: 'Ahmad', remoteNumberRaw: '0599123456',
  branchId: null, branchName: null, typeName: 'Order', typeLabelAr: 'طلب', typeLabelEn: 'Order', orderValue: 50,
  durationSec: 90, notes: null, isClassified: true, hasRecording: false, recordingExpired: false,
  channelId: null, channelName: null,
}

describe('with the server stopped, every screen says it could not load', () => {
  it('contacts: the search fails, and nothing is offered to be flagged', async () => {
    down()
    renderWithClient(<ContactsPage />)
    fireEvent.change(screen.getByLabelText('Search'), { target: { value: '0599123456' } })

    expect(await screen.findByText(/The search did not reach the server/)).toBeInTheDocument()
    expect(retryButtons().length).toBeGreaterThan(0)
    expect(screen.queryByText('Nothing matched that search.')).not.toBeInTheDocument()
    expect(screen.queryByText('No contacts yet.')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /flag it anyway/ })).not.toBeInTheDocument()
  })

  it('contacts: Try again asks again, and the list arrives', async () => {
    const fetchMock = vi.fn(serverDown)
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<ContactsPage />)
    await screen.findByText(/The search did not reach the server/)

    fetchMock.mockImplementation(async () =>
      jsonResponse([{ id: 'c1', name: 'Ahmad', address: null, isVip: false, isBlocked: false, flagReason: null, phones: ['0599'] }]))
    fireEvent.click(retryButtons()[0])

    expect(await screen.findByText('Ahmad')).toBeInTheDocument()
    expect(screen.queryByText(/The search did not reach the server/)).not.toBeInTheDocument()
  })

  it('a contact’s history', async () => {
    down()
    renderWithClient(<ContactHistory contactId="c1" />)

    expect(await screen.findByText('The history could not be loaded.')).toBeInTheDocument()
    expect(screen.queryByText('No calls with this customer yet.')).not.toBeInTheDocument()
  })

  it('users: no table at all, rather than an empty one', async () => {
    down()
    renderWithClient(<UsersPage />)

    expect(await screen.findByText('The users could not be loaded.')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('delivery areas, and the branch drop-down', async () => {
    down()
    renderWithClient(<DeliveryPage />)

    expect(await screen.findByText('The delivery areas could not be loaded.')).toBeInTheDocument()
    expect(screen.queryByText('No delivery areas yet.')).not.toBeInTheDocument()
    expect(await screen.findByText('The list did not load.')).toBeInTheDocument()
  })

  it('the menu', async () => {
    down()
    renderWithClient(<MenuPage />)

    expect(await screen.findByText('The menu could not be loaded.')).toBeInTheDocument()
    expect(screen.queryByText('Nothing on the menu yet.')).not.toBeInTheDocument()
  })

  it('the channels card', async () => {
    down()
    renderWithClient(<ChannelsCard />)

    expect(await screen.findByText('The channels could not be loaded.')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('the queue switch stays on the dashboard and says why it cannot switch', async () => {
    // It used to render nothing, and the S-60 switch simply vanished.
    down()
    renderWithClient(<QueueSwitchCard />)

    expect(await screen.findByRole('region', { name: 'Call queue' })).toBeInTheDocument()
    expect(screen.getByText(/Whether the queue is open could not be loaded/)).toBeInTheDocument()
  })

  it('a call’s details: the classification fails instead of pulsing for ever', async () => {
    down()
    renderWithClient(<CallDetails row={ANSWERED} onClose={() => undefined} />)

    expect(await screen.findByText('The classification could not be loaded.')).toBeInTheDocument()
    expect(screen.getByText('The call could not be opened.')).toBeInTheDocument()
    expect(screen.getByText('The changes to the classification could not be loaded.')).toBeInTheDocument()
  })

  it('the flag dialog’s history', async () => {
    down()
    renderWithClient(
      <FlagDialog
        target={{ kind: 'contact', id: 'c1', name: 'Ahmad', isVip: false, isBlocked: true, flagReason: 'Prank calls' }}
        onClose={() => undefined}
      />,
    )

    expect(await screen.findByText('The changes to this flag could not be loaded.')).toBeInTheDocument()
  })

  it('the Calls page’s filter drop-downs say their lists did not load', async () => {
    // The search itself answers; the agents, branches and types do not.
    vi.stubGlobal('fetch', vi.fn(routes([
      ['/communications/search', () => jsonResponse({ rows: [], total: 0, page: 1, pageSize: 50 })],
    ])))
    renderWithClient(<CallsPage />)

    await waitFor(() => expect(screen.getAllByText('The list did not load.')).toHaveLength(3))
  })

  it('the report filter bar, and each report', async () => {
    down()
    renderWithClient(<ApplicationReportsPage />)

    await waitFor(() => expect(screen.getAllByText('The list did not load.').length).toBeGreaterThan(0))
    expect((await screen.findAllByText('This report could not be loaded.')).length).toBe(5)
    expect(screen.queryByText('Nothing in this period.')).not.toBeInTheDocument()
  })

  it('settings: no form, so blanks cannot be saved over the real values (M-W04)', async () => {
    down()
    renderWithClient(<SettingsPage />)

    expect(await screen.findByText(/The settings could not be loaded/)).toBeInTheDocument()
    expect(await screen.findByText(/The PBX settings could not be loaded/)).toBeInTheDocument()
    expect(await screen.findByText(/The blacklist settings could not be loaded/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save settings' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save PBX settings' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save blacklist settings' })).not.toBeInTheDocument()
  })

  it('the dashboard', async () => {
    down()
    renderWithClient(<DashboardPage />)

    expect((await screen.findAllByText('The figures could not be loaded. Check the server is running.')).length).toBe(2)
    expect(retryButtons().length).toBeGreaterThanOrEqual(2)
  })

  it('the classification form', async () => {
    down()
    renderWithClient(<ClassificationPage />)

    expect(await screen.findByText('The form could not be loaded.')).toBeInTheDocument()
    expect(retryButtons().length).toBeGreaterThan(0)
  })
})
