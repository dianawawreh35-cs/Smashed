import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import MenuPage from './MenuPage'
import { setToken } from '../auth/token'
import i18n from '../i18n'

/**
 * The supervisor's menu screen (S-59).
 *
 * What is worth testing here is what an agent would otherwise quote wrongly to a
 * customer: the three price shapes told apart, and the picture actually being
 * sent. The rest of the form is inputs bound to state.
 */

const BURGERS = { id: 'g1', name: 'Burgers', sortOrder: 0, isActive: true, itemCount: 2 }
const DRINKS = { id: 'g2', name: 'Drinks', sortOrder: 10, isActive: true, itemCount: 1 }

const SMASHED = {
  id: 'i1',
  categoryId: 'g1',
  categoryName: 'Burgers',
  name: 'Smashed',
  description: 'Beef, cheese, sauce',
  price: 26,
  mealPrice: 36,
  isSurcharge: false,
  hasImage: true,
  isActive: true,
}

const CHEESE = {
  id: 'i2',
  categoryId: 'g1',
  categoryName: 'Burgers',
  name: 'Extra cheese',
  description: null,
  price: 2,
  mealPrice: null,
  isSurcharge: true,
  hasImage: false,
  isActive: true,
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

/** Answers each endpoint the page asks for, whatever order it asks in. */
function stubApi(items: unknown[] = [SMASHED, CHEESE], categories: unknown[] = [BURGERS, DRINKS]) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)

    if (url.includes('/menu/categories')) return jsonResponse(categories)
    if (url.includes('/image')) return jsonResponse(null, 204)

    // A saved item comes back, because the picture upload that follows needs
    // the id off it.
    if (init?.method === 'POST' || init?.method === 'PUT') {
      return jsonResponse({ ...SMASHED, id: url.split('/menu/')[1] ?? 'new1' })
    }

    if (url.includes('/menu?') || url.endsWith('/menu')) return jsonResponse(items)

    return jsonResponse(null, 204)
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/**
 * The edit form. Scoped because the page has a category filter as well, and
 * "Category" on its own matches both.
 */
function form() {
  return screen.getByRole('button', { name: 'Save' }).closest('form')!
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MenuPage />
    </QueryClientProvider>,
  )
}

/** The request body of the first call matching a method and path fragment. */
function bodyOf(fetchMock: ReturnType<typeof vi.fn>, method: string, fragment: string) {
  const call = fetchMock.mock.calls.find(
    ([url, init]) =>
      String(url).includes(fragment) &&
      (init as RequestInit | undefined)?.method === method,
  )

  return call ? JSON.parse(String((call[1] as RequestInit).body)) : null
}

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')

  // jsdom has neither, and the picture preview uses both.
  URL.createObjectURL = vi.fn(() => 'blob:preview')
  URL.revokeObjectURL = vi.fn()
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('menu page', () => {
  it('tells a surcharge from a price', async () => {
    // "+2" is added to a burger; "2" would be quoted as a line of its own.
    stubApi()
    renderPage()

    const cheese = (await screen.findByText('Extra cheese')).closest('tr')!
    expect(within(cheese).getByText('+2')).toBeInTheDocument()

    const smashed = screen.getByText('Smashed').closest('tr')!
    expect(within(smashed).getByText('26')).toBeInTheDocument()
    expect(within(smashed).getByText('36')).toBeInTheDocument()
  })

  it('sends a blank price as null rather than zero', async () => {
    // Null is "the menu prints no price"; zero is a free extra. An agent has to
    // be able to tell "free" from "ask the branch".
    const fetchMock = stubApi()
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Add item' }))

    fireEvent.change(within(form()).getByLabelText('Item'), { target: { value: 'Offer' } })
    fireEvent.change(within(form()).getByLabelText('Category'), { target: { value: 'g1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(bodyOf(fetchMock, 'POST', '/menu')).not.toBeNull())
    expect(bodyOf(fetchMock, 'POST', '/menu')).toMatchObject({ price: null, mealPrice: null })
  })

  it('uploads a chosen picture after saving the item', async () => {
    // The picture needs the item's id, so a new item has none until it is saved.
    const fetchMock = stubApi()
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Add item' }))
    fireEvent.change(within(form()).getByLabelText('Item'), { target: { value: 'New burger' } })
    fireEvent.change(within(form()).getByLabelText('Category'), { target: { value: 'g1' } })

    const file = new File(['bytes'], 'burger.png', { type: 'image/png' })
    fireEvent.change(within(form()).getByLabelText('Picture'), { target: { files: [file] } })

    // The chosen file is shown before saving, so the wrong photograph is caught
    // by eye rather than after it is on the menu.
    expect(URL.createObjectURL).toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(
          ([url, init]) =>
            String(url).includes('/menu/new1/image') &&
            (init as RequestInit | undefined)?.method === 'PUT',
        ),
      ).toBe(true),
    )
  })

  it('can take a picture away again', async () => {
    // Without this the only way to correct a wrong photograph is to replace it,
    // and an item that should have none is stuck with one.
    const fetchMock = stubApi()
    renderPage()

    const row = (await screen.findByText('Smashed')).closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Edit' }))

    fireEvent.click(screen.getByLabelText('Remove the current picture'))
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(
          ([url, init]) =>
            String(url).includes('/menu/i1/image') &&
            (init as RequestInit | undefined)?.method === 'PUT',
        ),
      ).toBe(true),
    )
  })

  it('renames a category and refreshes the items that print its name', async () => {
    const fetchMock = stubApi()
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Manage categories' }))

    const name = (await screen.findAllByLabelText('Category name'))[0]
    fireEvent.change(name, { target: { value: 'Smashed burgers' } })
    fireEvent.blur(name)

    await waitFor(() => expect(bodyOf(fetchMock, 'PUT', '/menu/categories/g1')).not.toBeNull())
    expect(bodyOf(fetchMock, 'PUT', '/menu/categories/g1')).toMatchObject({
      name: 'Smashed burgers',
    })
  })

  it('adds a category at the end of the printed order', async () => {
    const fetchMock = stubApi()
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Manage categories' }))

    fireEvent.change(await screen.findByLabelText('New category'), {
      target: { value: 'Desserts' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Add category' }))

    await waitFor(() => expect(bodyOf(fetchMock, 'POST', '/menu/categories')).not.toBeNull())
    expect(bodyOf(fetchMock, 'POST', '/menu/categories')).toMatchObject({
      name: 'Desserts',
      isActive: true,
    })
  })
})
