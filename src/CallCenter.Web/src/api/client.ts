/**
 * Thin fetch wrapper for the Call Center API.
 *
 * In development Vite proxies `/api` and `/hubs` to the ASP.NET host
 * (see vite.config.ts), so the default base URL is relative and the same code
 * works unchanged in production, where the SPA is served from the API's own
 * wwwroot.
 */

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api'

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly statusText: string,
    readonly body: unknown,
  ) {
    super(`${status} ${statusText}`)
    this.name = 'ApiError'
  }
}

export interface RequestOptions extends Omit<RequestInit, 'body'> {
  /** Serialised as JSON unless it is already a BodyInit. */
  body?: unknown
  /** Appended to the URL as a query string, skipping null/undefined values. */
  query?: Record<string, string | number | boolean | null | undefined>
}

function buildUrl(path: string, query?: RequestOptions['query']): string {
  const url = path.startsWith('http') ? path : `${API_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`
  if (!query) return url

  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined) params.append(key, String(value))
  }
  const qs = params.toString()
  return qs ? `${url}${url.includes('?') ? '&' : '?'}${qs}` : url
}

function isBodyInit(value: unknown): value is BodyInit {
  return (
    typeof value === 'string' ||
    value instanceof FormData ||
    value instanceof Blob ||
    value instanceof URLSearchParams ||
    value instanceof ArrayBuffer
  )
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { body, query, headers, ...rest } = options

  const init: RequestInit = {
    // Cookie-based auth: always send credentials, including to the dev proxy.
    credentials: 'include',
    ...rest,
    headers: {
      Accept: 'application/json',
      ...(body !== undefined && !isBodyInit(body) ? { 'Content-Type': 'application/json' } : {}),
      ...headers,
    },
  }

  if (body !== undefined) {
    init.body = isBodyInit(body) ? body : JSON.stringify(body)
  }

  const response = await fetch(buildUrl(path, query), init)

  const contentType = response.headers.get('content-type') ?? ''
  const payload = response.status === 204
    ? null
    : contentType.includes('application/json')
      ? await response.json().catch(() => null)
      : await response.text()

  if (!response.ok) {
    throw new ApiError(response.status, response.statusText, payload)
  }

  return payload as T
}

export const api = {
  get: <T>(path: string, options?: RequestOptions) => request<T>(path, { ...options, method: 'GET' }),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>(path, { ...options, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>(path, { ...options, method: 'PUT', body }),
  patch: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>(path, { ...options, method: 'PATCH', body }),
  delete: <T>(path: string, options?: RequestOptions) => request<T>(path, { ...options, method: 'DELETE' }),
}

/** Liveness probe. `/health` sits outside the `/api` prefix. */
export async function checkHealth(): Promise<boolean> {
  try {
    const response = await fetch('/health', { credentials: 'include' })
    return response.ok
  } catch {
    return false
  }
}
