import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { onTokenRefused } from '../api/client'
import { fetchCurrentUser, login as loginRequest, logout as logoutRequest } from '../api/auth'
import type { CurrentUser } from '../api/auth'
import { AuthContext } from './context'
import type { AuthState } from './context'
import { getToken, setToken } from './token'

/**
 * Holds who is signed in, for the whole supervisor app (S-01).
 *
 * A token in `sessionStorage` is not trusted on its own — it may have expired,
 * or the account may have been disabled since — so on the first render it is
 * checked against `/auth/me`, and the app shows nothing decisive until that
 * answers. Otherwise a refresh would flash the dashboard before bouncing the
 * supervisor to the login page.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [isLoading, setIsLoading] = useState(getToken() !== null)
  const [signedOutByServer, setSignedOutByServer] = useState(false)

  // The session row the API opened, needed to close it cleanly on sign-out.
  const sessionId = useRef<string | null>(null)

  // Read by the token-refused handler, which is registered once.
  const userRef = useRef<CurrentUser | null>(null)
  useEffect(() => {
    userRef.current = user
  }, [user])

  // The server can end the sign-in from its side: a password reset, a disabled
  // account, a role change, or a token that ran out (N-05). Drop everything at
  // the first refused request; the routes then send the supervisor to the login
  // page, which says why.
  useEffect(() => {
    onTokenRefused(() => {
      // A stale token found on page load is not news; only say so when a
      // supervisor who was working is thrown out.
      const wasSignedIn = userRef.current !== null
      sessionId.current = null
      setToken(null)
      setUser(null)
      if (wasSignedIn) setSignedOutByServer(true)
    })
    return () => onTokenRefused(null)
  }, [])

  useEffect(() => {
    if (getToken() === null) return

    let cancelled = false

    void (async () => {
      try {
        const current = await fetchCurrentUser()
        if (cancelled) return

        if (current) {
          setUser(current)
        } else {
          // Expired, or the account is gone. Drop it rather than retry.
          setToken(null)
        }
      } catch {
        // The server is unreachable. Keep the token — it may still be good once
        // the server is back — but treat the supervisor as signed out for now.
        if (!cancelled) setUser(null)
      } finally {
        if (!cancelled) setIsLoading(false)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [])

  const signIn = useCallback(async (login: string, password: string) => {
    const response = await loginRequest(login, password)
    setToken(response.accessToken)
    sessionId.current = response.sessionId
    setSignedOutByServer(false)
    setUser(response.user)
  }, [])

  const signOut = useCallback(async () => {
    await logoutRequest(sessionId.current)
    sessionId.current = null
    setToken(null)
    setUser(null)
  }, [])

  const value = useMemo<AuthState>(
    () => ({ user, isLoading, signIn, signOut, signedOutByServer }),
    [user, isLoading, signIn, signOut, signedOutByServer],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
