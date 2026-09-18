import { createContext, useContext } from 'react'
import type { CurrentUser } from '../api/auth'

/**
 * The auth context and its hook, kept apart from `AuthProvider.tsx` because a
 * file that exports a component must export nothing else for React Fast Refresh
 * to work (and the lint rule that enforces it).
 */
export interface AuthState {
  /** Null until a token has been checked against the server. */
  user: CurrentUser | null
  /** True while the token kept from a previous page load is being verified. */
  isLoading: boolean
  signIn: (login: string, password: string) => Promise<void>
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthState | null>(null)

/** Reads the auth state. Throws if used outside `AuthProvider`. */
export function useAuth(): AuthState {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside an AuthProvider')
  return context
}
