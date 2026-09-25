import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import ClassificationEditor from './ClassificationEditor'
import type { CallClassification } from '../api/calls'
import type { ClassificationForm } from '../api/classifications'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/** A supervisor classifying or changing one call (S-04), against a stubbed `fetch`. */

const ORDER = { id: 't-order', name: 'Order', labelAr: 'طلب', labelEn: 'Order', colour: null, isSystem: true, sortOrder: 1, isActive: true, inUse: true }
const COMPLAINT = { ...ORDER, id: 't-complaint', name: 'Complaint', labelAr: 'شكوى', labelEn: 'Complaint' }
const RETIRED = { ...ORDER, id: 't-retired', name: 'Retired', labelEn: 'Retired', isActive: false }

const FORM: ClassificationForm = {
  version: 7,
  direction: 'In',
  types: [ORDER, COMPLAINT, RETIRED],
  branches: [{ id: 'b1', name: 'Ramallah' }, { id: 'b2', name: 'Nablus' }],
  definition: {
    fields: [
      { key: 'type', kind: 'type', required: true, label: { en: 'Type' } },
      { key: 'branch', kind: 'branch', required: true, label: { en: 'Branch' } },
      { key: 'order_value', kind: 'number', label: { en: 'Order value' }, showWhenType: ['Order'] },
      { key: 'notes', kind: 'textarea', label: { en: 'Notes' } },
      { key: 'follow_up', kind: 'checkbox', label: { en: 'Follow up' } },
      {
        key: 'payment', kind: 'select', label: { en: 'Payment' }, showWhenType: ['Order'],
        options: [{ value: 'cash', label: { en: 'Cash' } }, { value: 'card', label: { en: 'Card' } }],
      },
    ],
  },
}

const EXISTING: CallClassification = {
  typeId: 't-order', typeName: 'Order', typeLabelAr: 'طلب', typeLabelEn: 'Order',
  branchId: 'b1', branchName: 'Ramallah', orderValue: 45.5, notes: 'cold fries', followUp: false,
  resolved: null, formVersion: 3,
  // "delivery_slot" is a question the form no longer asks.
  customValues: { payment: 'card', delivery_slot: 'evening' },
  classifiedByName: 'Sara', classifiedAt: '2026-09-24T09:00:00Z', updatedByName: null, updatedAt: null,
}

function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300, status, statusText: String(status),
    headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => body, text: async () => JSON.stringify(body),
  } as unknown as Response
}

function renderEditor(existing: CallClassification | null, onSaved = vi.fn(), form: ClassificationForm = FORM) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <ClassificationEditor callId="c1" form={form} existing={existing} onSaved={onSaved} onCancel={vi.fn()} />
    </QueryClientProvider>,
  )
  return onSaved
}

const sentBody = (fetchMock: ReturnType<typeof vi.fn>) => {
  const put = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT')
  expect(put).toBeDefined()
  expect(String(put![0])).toBe('/api/classifications/c1')
  return JSON.parse(put![1].body)
}

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('classification editor', () => {
  it('opens with the call\'s current answers', () => {
    renderEditor(EXISTING)

    expect(screen.getByLabelText('Type *')).toHaveValue('t-order')
    expect(screen.getByLabelText('Branch *')).toHaveValue('b1')
    expect(screen.getByLabelText('Order value')).toHaveValue('45.5')
    expect(screen.getByLabelText('Notes')).toHaveValue('cold fries')
    expect(screen.getByLabelText('Payment')).toHaveValue('card')
  })

  it('shows a question only for the types it is asked for', () => {
    renderEditor(EXISTING)

    fireEvent.change(screen.getByLabelText('Type *'), { target: { value: 't-complaint' } })

    expect(screen.queryByLabelText('Order value')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Payment')).not.toBeInTheDocument()
    // A complaint can be marked resolved: the supervisor following it up does that.
    expect(screen.getByLabelText('Resolved')).toBeInTheDocument()
  })

  /** The same form, offering only the types named (S-40). */
  const offering = (...names: string[]): ClassificationForm => ({
    ...FORM,
    definition: {
      fields: FORM.definition.fields.map((f) => (f.kind === 'type' ? { ...f, types: names } : f)),
    },
  })

  it('offers only the types this form offers', () => {
    // The supervisor ticked which types each form lists (S-40).
    renderEditor(null, vi.fn(), offering('Complaint'))

    const options = [...(screen.getByLabelText('Type *') as HTMLSelectElement).options].map((o) => o.value)
    expect(options).toEqual(['', 't-complaint'])
  })

  it('shows a call\'s type the form no longer offers, and asks for another before saving', () => {
    // Classified as an order before the form stopped offering orders: it still
    // reads as an order, but cannot be saved as one (the server refuses it).
    renderEditor(EXISTING, vi.fn(), offering('Complaint'))

    expect(screen.getByLabelText('Type *')).toHaveValue('t-order')
    expect(screen.getByText("This form no longer offers the call's type. Choose another type to save.")).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save classification' })).toBeDisabled()

    fireEvent.change(screen.getByLabelText('Type *'), { target: { value: 't-complaint' } })
    expect(screen.getByRole('button', { name: 'Save classification' })).toBeEnabled()
  })

  it('does not offer a hidden type for a new classification', () => {
    renderEditor(null)

    const options = [...(screen.getByLabelText('Type *') as HTMLSelectElement).options].map((o) => o.value)
    expect(options).toEqual(['', 't-order', 't-complaint'])
  })

  it('says what is missing rather than leaving Save dead', () => {
    renderEditor(null)

    expect(screen.getByRole('button', { name: 'Save classification' })).toBeDisabled()
    expect(screen.getByText('Still needed: Type, Branch')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Type *'), { target: { value: 't-order' } })
    fireEvent.change(screen.getByLabelText('Branch *'), { target: { value: 'b2' } })

    expect(screen.getByRole('button', { name: 'Save classification' })).toBeEnabled()
  })

  it('refuses an order value that is not a number', () => {
    renderEditor(EXISTING)

    fireEvent.change(screen.getByLabelText('Order value'), { target: { value: 'forty' } })

    expect(screen.getByText('Not a number: Order value')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save classification' })).toBeDisabled()
  })

  it('saves what the Agent App saves, and keeps answers to questions no longer asked', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(EXISTING))
    vi.stubGlobal('fetch', fetchMock)
    const onSaved = renderEditor(EXISTING)

    fireEvent.change(screen.getByLabelText('Order value'), { target: { value: '52' } })
    fireEvent.change(screen.getByLabelText('Payment'), { target: { value: 'cash' } })
    fireEvent.click(screen.getByLabelText('Follow up'))
    fireEvent.click(screen.getByRole('button', { name: 'Save classification' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalled())
    expect(sentBody(fetchMock)).toEqual({
      typeId: 't-order',
      branchId: 'b1',
      orderValue: 52,
      notes: 'cold fries',
      followUp: true,
      resolved: null,
      formVersion: 7,
      customValues: { payment: 'cash', delivery_slot: 'evening' },
    })
  })

  it('drops the answers to questions the new type does not ask', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(EXISTING))
    vi.stubGlobal('fetch', fetchMock)
    const onSaved = renderEditor(EXISTING)

    fireEvent.change(screen.getByLabelText('Type *'), { target: { value: 't-complaint' } })
    fireEvent.click(screen.getByLabelText('Resolved'))
    fireEvent.click(screen.getByRole('button', { name: 'Save classification' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalled())
    const body = sentBody(fetchMock)
    expect(body.typeId).toBe('t-complaint')
    expect(body.orderValue).toBeNull()
    expect(body.resolved).toBe(true)
    // The payment question is not asked of a complaint; the removed one is kept.
    expect(body.customValues).toEqual({ delivery_slot: 'evening' })
  })

  it('says why a save was refused', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'unknown_type' }, 400)))
    renderEditor(EXISTING)

    fireEvent.click(screen.getByRole('button', { name: 'Save classification' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('That type is no longer offered.')
  })
})
