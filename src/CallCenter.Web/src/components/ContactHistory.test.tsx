import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import ContactHistory from './ContactHistory'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/** A contact's history: calls and messages together, each message with its channel (A-72). */

const CALL = {
  id: 'c1', kind: 'Call', direction: 'In', status: 'Missed', contactId: 'k1', contactName: 'Mahmoud',
  remoteNumberRaw: '0599123456', remoteName: null, startedAt: '2026-09-25T09:00:00Z', answeredAt: null,
  endedAt: null, durationSec: null, queueName: 'smashed-002', extension: null, agentDisplayName: null,
  isClassified: false, notes: null, hasRecording: false, recordingExpired: false, channelName: 'Phone',
}

const MESSAGE = {
  ...CALL, id: 'm1', kind: 'App', direction: 'None', status: 'Logged', startedAt: '2026-09-25T10:00:00Z',
  queueName: null, agentDisplayName: 'Sara', isClassified: false, channelName: 'WhatsApp',
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

function server() {
  return vi.fn().mockImplementation(async (url: string) => {
    if (url.startsWith('/api/communications/by-contact/k1')) return jsonResponse([MESSAGE, CALL])
    if (url === '/api/communications/m1') return jsonResponse({ summary: MESSAGE, remoteName: null, answeredAt: null, endedAt: null, waitSec: null, queueName: null, extension: null, callNotes: null })
    return jsonResponse([])
  })
}

function renderHistory() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ContactHistory contactId="k1" />
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

describe('contact history', () => {
  it('lists messages beside calls, each with its channel, and a message as "Message"', async () => {
    vi.stubGlobal('fetch', server())
    renderHistory()

    expect(await screen.findByRole('columnheader', { name: 'Channel' })).toBeInTheDocument()
    const message = screen.getByText('Message').closest('tr')!
    expect(within(message).getByText('WhatsApp')).toBeInTheDocument()
    expect(within(message).getByText('Sara')).toBeInTheDocument()
    // A message recorded without its form is a debt; a missed call is not.
    expect(within(message).getByText('Not classified')).toBeInTheDocument()
    const call = screen.getByText('Missed').closest('tr')!
    expect(within(call).getByText('Phone')).toBeInTheDocument()
    expect(within(call).queryByText('Not classified')).not.toBeInTheDocument()
  })

  it('opens a message under its row, as a call opens', async () => {
    vi.stubGlobal('fetch', server())
    renderHistory()

    const message = (await screen.findByText('Message')).closest('tr')!
    fireEvent.click(within(message).getByRole('button', { name: 'Open' }))

    const details = await screen.findByRole('region', { name: 'Message details' })
    expect(message.nextElementSibling).toContainElement(details)
    expect(within(details).getByRole('button', { name: 'Classify' })).toBeInTheDocument()
    expect(within(details).queryByText('Recording')).not.toBeInTheDocument()
  })
})
