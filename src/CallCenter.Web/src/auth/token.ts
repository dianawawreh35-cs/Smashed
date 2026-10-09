/**
 * Where the supervisor's bearer token lives for the life of a browser tab.
 *
 * `sessionStorage`, not `localStorage`: closing the tab signs the supervisor
 * out, which is the behaviour S-01 asks for on a machine that may not be theirs
 * alone. The in-memory copy is what every request actually reads, so a blocked
 * or full storage degrades to "signed out when the tab closes" rather than
 * failing outright.
 */

const STORAGE_KEY = 'callcenter.token'

let current: string | null = read()

function read(): string | null {
  try {
    return sessionStorage.getItem(STORAGE_KEY)
  } catch {
    // Private mode, or site data blocked - the in-memory copy still works.
    return null
  }
}

export function getToken(): string | null {
  return current
}

/**
 * When the token stops being accepted, read from its own `exp` claim: the
 * server signs it with a fixed lifetime from sign-in (12 h, TokenService), and
 * after a page reload the token is all the browser still has. Null for a token
 * that cannot be read; nothing is decided on it alone, since the server is the
 * judge (S-72).
 */
export function tokenExpiresAt(token: string | null = current): number | null {
  const payload = token?.split('.')[1]
  if (!payload) return null
  try {
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'))
    const exp = (JSON.parse(json) as { exp?: unknown }).exp
    return typeof exp === 'number' ? exp * 1000 : null
  } catch {
    return null
  }
}

export function setToken(token: string | null): void {
  current = token
  try {
    if (token) sessionStorage.setItem(STORAGE_KEY, token)
    else sessionStorage.removeItem(STORAGE_KEY)
  } catch {
    // ignore - the session simply will not survive a refresh
  }
}
