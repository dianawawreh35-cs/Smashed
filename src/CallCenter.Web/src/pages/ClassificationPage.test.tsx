import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import ClassificationPage from './ClassificationPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/**
 * The supervisor's classification form editor (S-40).
 *
 * What matters here is what would quietly corrupt the reports: a published form
 * that cannot be drawn, a type key that moves, or a "asked only for complaints"
 * rule written against a label instead of a name.
 */

const ORDER = {
  id: 't1',
  name: 'Order',
  labelAr: 'طلب',
  labelEn: 'Order',
  colour: null,
  isSystem: true,
  sortOrder: 0,
  isActive: true,
  inUse: true,
}

const COMPLAINT = { ...ORDER, id: 't2', name: 'Complaint', labelAr: 'شكوى', labelEn: 'Complaint', sortOrder: 10 }

const SPARE = {
  ...ORDER,
  id: 't3',
  name: 'Spare',
  labelAr: 'أخرى',
  labelEn: 'Spare',
  isSystem: false,
  inUse: false,
  sortOrder: 20,
}

const FORM = {
  version: 2,
  direction: 'In',
  definition: {
    fields: [
      { key: 'type', kind: 'type', required: true },
      { key: 'branch', kind: 'branch', required: true },
      { key: 'notes', kind: 'textarea', label: { ar: 'ملاحظات', en: 'Notes' } },
    ],
  },
  types: [ORDER, COMPLAINT, SPARE],
  branches: [{ id: 'b1', name: 'ايكون' }],
}

/** The outbound form: its own questions, the same types. */
const OUTBOUND_FORM = {
  ...FORM,
  version: 3,
  direction: 'Out',
  definition: {
    fields: [
      { key: 'type', kind: 'type', required: true },
      { key: 'notes', kind: 'textarea', label: { ar: 'ملاحظات', en: 'Notes' } },
    ],
  },
}

/** The messages form (A-70): a third set of questions, published under direction None. */
const MESSAGES_FORM = {
  ...FORM,
  version: 1,
  direction: 'None',
  definition: {
    fields: [
      { key: 'type', kind: 'type', required: true },
      { key: 'branch', kind: 'branch', required: true },
    ],
  },
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

function stubApi() {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/classifications/form') && init?.method === 'PUT') {
      return jsonResponse(FORM)
    }
    if (url.includes('/classifications/form')) {
      return jsonResponse(
        url.includes('direction=Out') ? OUTBOUND_FORM : url.includes('direction=None') ? MESSAGES_FORM : FORM,
      )
    }
    if (url.includes('/classifications/types')) return jsonResponse(FORM.types)
    return jsonResponse(null, 204)
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ClassificationPage />
    </QueryClientProvider>,
  )
}

function bodyOf(fetchMock: ReturnType<typeof vi.fn>, method: string, fragment: string) {
  const call = fetchMock.mock.calls.find(
    ([url, init]) =>
      String(url).includes(fragment) && (init as RequestInit | undefined)?.method === method,
  )
  return call ? JSON.parse(String((call[1] as RequestInit).body)) : null
}

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('classification form editor', () => {
  it('shows the stable key and does not let it be edited', async () => {
    // Reports and the show-when rules are written against the key. A screen
    // that let it be renamed would break them silently, months later.
    stubApi()
    renderPage()

    const row = (await screen.findByDisplayValue('Order')).closest('tr')!
    expect(within(row).getByText('Order', { selector: 'td' })).toBeInTheDocument()
  })

  it('offers to hide a type that is in use, not to delete it', async () => {
    // Deleting a type calls already carry would leave them describing nothing.
    stubApi()
    renderPage()

    const inUse = (await screen.findByDisplayValue('Complaint')).closest('tr')!
    expect(within(inUse).queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument()

    const spare = screen.getByDisplayValue('Spare').closest('tr')!
    expect(within(spare).getByRole('button', { name: 'Remove' })).toBeInTheDocument()
  })

  it('writes the show-when rule against the type name, not its label', async () => {
    // A supervisor renaming "Complaint" to "Issue" must not make the field
    // vanish from every agent's form.
    const fetchMock = stubApi()
    renderPage()

    const inbound = (await screen.findByText('Questions for incoming calls')).closest(
      '.card',
    )! as HTMLElement

    // The notes row's "asked for" ticks, one per type.
    const asked = within(inbound).getAllByRole('checkbox', { name: 'Complaint' })
    fireEvent.click(asked[asked.length - 1])

    fireEvent.click(within(inbound).getByRole('button', { name: 'Publish form' }))

    await waitFor(() =>
      expect(bodyOf(fetchMock, 'PUT', '/classifications/form')).not.toBeNull(),
    )

    const published = bodyOf(fetchMock, 'PUT', '/classifications/form')
    const notes = published.definition.fields.find((f: { key: string }) => f.key === 'notes')
    expect(notes.showWhenType).toEqual(['Complaint'])
    expect(published.direction).toBe('In')
  })

  it('lets each form offer its own types, by name, and keeps at least one', async () => {
    // S-40: the supervisor chooses which call types each form offers. The
    // outbound form drops Order; the inbound form is not touched.
    const fetchMock = stubApi()
    renderPage()

    const outbound = (await screen.findByText('Questions for outgoing calls')).closest(
      '.card',
    )! as HTMLElement

    // The type question's row comes first, so its boxes are the first of each name.
    const offered = (name: string) => within(outbound).getAllByRole('checkbox', { name })[0]
    expect(within(outbound).getByText('Types this form offers:')).toBeInTheDocument()
    expect(offered('Order')).toBeChecked()

    fireEvent.click(offered('Order'))
    fireEvent.click(offered('Spare'))
    // Complaint is the last one left, so it cannot be unticked.
    expect(offered('Complaint')).toBeDisabled()

    fireEvent.click(within(outbound).getByRole('button', { name: 'Publish form' }))
    await waitFor(() =>
      expect(bodyOf(fetchMock, 'PUT', '/classifications/form')).not.toBeNull(),
    )

    const published = bodyOf(fetchMock, 'PUT', '/classifications/form')
    expect(published.direction).toBe('Out')
    const type = published.definition.fields.find((f: { kind: string }) => f.kind === 'type')
    expect(type.types).toEqual(['Complaint'])
  })

  it('stores no list when every type is ticked, so a new type joins the form', async () => {
    stubApi()
    renderPage()

    const outbound = (await screen.findByText('Questions for outgoing calls')).closest(
      '.card',
    )! as HTMLElement
    const offered = (name: string) => within(outbound).getAllByRole('checkbox', { name })[0]

    fireEvent.click(offered('Order'))
    fireEvent.click(offered('Order'))

    // Back to every type: nothing changed, so there is nothing to publish.
    expect(within(outbound).getByRole('button', { name: 'Publish form' })).toBeDisabled()
  })

  it('publishes the outgoing questions as their own form', async () => {
    // A call the agent placed asks different questions. Publishing the
    // outbound card must not touch the inbound form.
    const fetchMock = stubApi()
    renderPage()

    const outbound = (await screen.findByText('Questions for outgoing calls')).closest(
      '.card',
    )! as HTMLElement

    fireEvent.click(within(outbound).getByRole('button', { name: 'Add question' }))
    fireEvent.click(within(outbound).getByRole('button', { name: 'Publish form' }))

    await waitFor(() =>
      expect(bodyOf(fetchMock, 'PUT', '/classifications/form')).not.toBeNull(),
    )

    const published = bodyOf(fetchMock, 'PUT', '/classifications/form')
    expect(published.direction).toBe('Out')
    expect(published.definition.fields.map((f: { key: string }) => f.key)).toContain('notes')
    expect(published.definition.fields.some((f: { key: string }) => f.key === 'branch')).toBe(false)
  })

  it('publishes the messages questions as a third form, under direction None', async () => {
    // A message has no direction (A-70). Its card must publish its own form
    // and leave the two call forms alone.
    const fetchMock = stubApi()
    renderPage()

    const messages = (await screen.findByText('Questions for messages (applications)')).closest(
      '.card',
    )! as HTMLElement
    expect(within(messages).getByText('Version 1')).toBeInTheDocument()

    fireEvent.click(within(messages).getByRole('button', { name: 'Add question' }))
    fireEvent.click(within(messages).getByRole('button', { name: 'Publish form' }))

    await waitFor(() =>
      expect(bodyOf(fetchMock, 'PUT', '/classifications/form')).not.toBeNull(),
    )

    const published = bodyOf(fetchMock, 'PUT', '/classifications/form')
    expect(published.direction).toBe('None')
    expect(published.definition.fields.map((f: { key: string }) => f.key)).toEqual(
      expect.arrayContaining(['type', 'branch']),
    )
    expect(published.definition.fields).toHaveLength(3)
  })

  it('cannot publish until something changes', async () => {
    stubApi()
    renderPage()

    const inbound = (await screen.findByText('Questions for incoming calls')).closest(
      '.card',
    )! as HTMLElement
    const publish = within(inbound).getByRole('button', { name: 'Publish form' })
    expect(publish).toBeDisabled()

    fireEvent.click(within(inbound).getByRole('button', { name: 'Add question' }))
    expect(within(inbound).getByRole('button', { name: 'Publish form' })).toBeEnabled()
  })

  it('does not offer to remove the built-in type and branch questions', async () => {
    // They have their own columns and the reports group by them; a form without
    // them would be refused by the server anyway.
    stubApi()
    renderPage()

    const inbound = (await screen.findByText('Questions for incoming calls')).closest(
      '.card',
    )! as HTMLElement

    const rows = within(inbound).getAllByRole('listitem')
    expect(within(rows[0]).queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument()
    expect(within(rows[1]).queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument()
    expect(within(rows[2]).getByRole('button', { name: 'Remove' })).toBeInTheDocument()
  })
})
