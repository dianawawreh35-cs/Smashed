import { MutationCache, QueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/client'
import { showToast } from './toast'

/**
 * Whether a failed request is worth one more try: once for a dropped
 * connection or a 5xx, never for a refusal, since a 4xx will say the same
 * again. That includes 401, so a sign-in the server has ended (a logout
 * elsewhere, a password reset) costs one refused request, not a retry loop,
 * before AuthProvider signs the supervisor out.
 */
export function shouldRetry(failures: number, error: unknown): boolean {
  return failures < 1 && !(error instanceof ApiError && error.status < 500)
}

/**
 * The app's one query cache. A save that names a notice in its `meta` gets
 * it once the server has accepted it (S-72, lib/toast).
 */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    mutationCache: new MutationCache({
      onSuccess: (_data, _variables, _context, mutation) => {
        if (mutation.meta?.toast) showToast(mutation.meta.toast)
      },
    }),
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: shouldRetry,
        refetchOnWindowFocus: false,
      },
    },
  })
}
