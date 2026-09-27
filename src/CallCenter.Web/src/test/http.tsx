import type { ReactNode } from 'react'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'

/** Stand-ins for the server, for tests that stub `fetch`. */

export function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: String(status),
    headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => body,
    text: async () => JSON.stringify(body),
    blob: async () => new Blob(['bytes']),
  } as unknown as Response
}

/** What `fetch` does when the server is stopped: it rejects. */
export const serverDown = (): Promise<Response> => Promise.reject(new TypeError('Failed to fetch'))

/** A route table for a stubbed `fetch`: the first pattern the URL contains answers; anything else is the server being down. */
export function routes(table: [string | RegExp, (url: string, init?: RequestInit) => Response | Promise<Response>][]) {
  return async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    for (const [pattern, answer] of table) {
      if (typeof pattern === 'string' ? url.includes(pattern) : pattern.test(url)) return answer(url, init)
    }
    return serverDown()
  }
}

/** Renders with a fresh query cache that does not retry, inside a router. */
export function renderWithClient(ui: ReactNode, path = '/') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const result = render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>{ui}</MemoryRouter>
    </QueryClientProvider>,
  )
  return { ...result, queryClient }
}
