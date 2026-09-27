import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import SettingsPage from './SettingsPage'
import PbxBlacklistCard from '../components/PbxBlacklistCard'
import AbandonedImportCard from '../components/AbandonedImportCard'
import { setToken } from '../auth/token'
import i18n from '../i18n'
import { jsonResponse, renderWithClient, routes } from '../test/http'

/** The settings screen (S-47, S-55, S-46): what it does when saving goes wrong, and while it refreshes. */

const HOST = {
  key: 'pbx.host', value: '10.8.0.1', kind: 'Text', options: null,
  updatedAt: '2026-09-20T08:00:00Z', updatedByDisplayName: null,
}

const BLACKLIST = {
  extension: '2099', secretSet: true, configured: true, onPbx: 4, waiting: 2, failures: [], lastSucceededAt: null,
}

const IMPORT = {
  url: 'https://10.8.0.1', username: 'callcenter', passwordSet: true, intervalMinutes: 1, configured: true,
  lastCheckedAt: '2026-09-27T08:00:00Z', lastSucceededAt: '2026-09-27T08:00:00Z', lastError: null,
  lastAdded: 2, syncedThrough: null,
}

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('settings', () => {
  it('says "not saved" when a save fails without naming a field (M-W04)', async () => {
    // A 500, or no network: no per-field problems to mark, so it used to say nothing at all.
    vi.stubGlobal('fetch', vi.fn(routes([
      ['/settings', (_, init) => (init?.method === 'PUT' ? jsonResponse({ title: 'boom' }, 500) : jsonResponse([HOST]))],
    ])))
    renderWithClient(<SettingsPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Save settings' }))

    expect(await screen.findByText(/Nothing was saved: the server did not answer/)).toBeInTheDocument()
    // What was typed is kept.
    expect(screen.getByDisplayValue('10.8.0.1')).toBeInTheDocument()
  })

  it('keeps what the supervisor is typing when the blacklist card refreshes (M-W05)', async () => {
    // The card refreshes every 15 seconds while numbers are waiting, and used to
    // refill the form each time, wiping a half-typed extension.
    vi.stubGlobal('fetch', vi.fn(routes([['/pbx/blacklist', () => jsonResponse(BLACKLIST)]])))
    const { queryClient } = renderWithClient(<PbxBlacklistCard />)

    const box = await screen.findByDisplayValue('2099')
    fireEvent.change(box, { target: { value: '21' } })

    // What a refresh does: new figures from the server.
    queryClient.setQueryData(['pbx', 'blacklist'], { ...BLACKLIST, waiting: 1 })

    await screen.findByText(/Waiting: 1/)
    expect(screen.getByDisplayValue('21')).toBeInTheDocument()
  })

  it('keeps what is typed on the PBX import card when Check now brings a new status', async () => {
    vi.stubGlobal('fetch', vi.fn(routes([
      ['/pbx/abandoned-import/fetch', () => jsonResponse({
        ok: true, from: '2026-09-27', to: '2026-09-27', calls: 5, abandoned: 1, added: 1, error: null,
        status: { ...IMPORT, lastAdded: 1 },
      })],
      ['/pbx/abandoned-import', () => jsonResponse(IMPORT)],
    ])))
    renderWithClient(<AbandonedImportCard />)

    const user = await screen.findByDisplayValue('callcenter')
    fireEvent.change(user, { target: { value: 'callcenter-new' } })
    fireEvent.click(screen.getByRole('button', { name: 'Check now' }))

    await screen.findByText(/1 new abandoned call\./)
    expect(screen.getByDisplayValue('callcenter-new')).toBeInTheDocument()
  })

  it('counts in words that agree with the number', async () => {
    // "2 new abandoned calls", never "1 new abandoned calls" (M-W10).
    vi.stubGlobal('fetch', vi.fn(routes([['/pbx/abandoned-import', () => jsonResponse(IMPORT)]])))
    renderWithClient(<AbandonedImportCard />)

    await waitFor(() => expect(screen.getByText(/: 2 new abandoned calls\./)).toBeInTheDocument())
  })
})
