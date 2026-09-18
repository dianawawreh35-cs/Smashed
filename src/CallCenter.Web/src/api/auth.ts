/**
 * Sign-in for the supervisor app (S-01).
 *
 * The same endpoints the Agent App uses. The shapes here mirror
 * `CallCenter.Shared.Contracts.Auth` — they are hand-written, so a change on
 * the server side has to be copied across by hand.
 */
import { ApiError, api } from './client'

export const UserRoles = {
  Agent: 'Agent',
  Supervisor: 'Supervisor',
} as const

export interface CurrentUser {
  id: string
  login: string
  displayName: string
  role: string
}

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  user: CurrentUser
  sessionId: string | null
  /** Agents only, and never used here - the supervisor app has no softphone. */
  extensions: unknown | null
}

/**
 * Why a sign-in failed. The first two come from the API; the rest are decided
 * in the browser. Each one has a message under `login.errors.<code>` in both
 * language files (A-80).
 */
export const LoginErrorCodes = {
  InvalidCredentials: 'invalid_credentials',
  AccountDisabled: 'account_disabled',
  EmptyFields: 'empty_fields',
  ServerUnreachable: 'server_unreachable',
  ServerError: 'server_error',
  /** A real account, but an agent's. Agents belong in the desktop app. */
  NotASupervisor: 'not_a_supervisor',
} as const

export type LoginErrorCode = (typeof LoginErrorCodes)[keyof typeof LoginErrorCodes]

/** Thrown by {@link login} and carried straight to the error line on the form. */
export class LoginError extends Error {
  constructor(readonly code: LoginErrorCode) {
    super(code)
    this.name = 'LoginError'
  }
}

/**
 * Signs in and returns the account. Throws {@link LoginError} with a code the
 * caller translates.
 */
export async function login(loginName: string, password: string): Promise<LoginResponse> {
  if (!loginName.trim() || !password) {
    throw new LoginError(LoginErrorCodes.EmptyFields)
  }

  let response: LoginResponse
  try {
    response = await api.post<LoginResponse>('/auth/login', {
      login: loginName.trim(),
      password,
    })
  } catch (error) {
    throw new LoginError(toLoginErrorCode(error))
  }

  // The API authenticates anyone; this app is for supervisors (S-01, N-10).
  // An agent who lands here is told where to go rather than shown empty screens.
  if (response.user.role !== UserRoles.Supervisor) {
    throw new LoginError(LoginErrorCodes.NotASupervisor)
  }

  return response
}

/** The signed-in account, or null when the token is no longer accepted. */
export async function fetchCurrentUser(): Promise<CurrentUser | null> {
  try {
    return await api.get<CurrentUser>('/auth/me')
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null
    throw error
  }
}

/**
 * Closes the session. Best effort: the supervisor is signed out in the browser
 * either way, so a server that cannot be reached must not trap them.
 */
export async function logout(sessionId: string | null): Promise<void> {
  if (!sessionId) return
  try {
    await api.post('/auth/logout', { sessionId, reason: 'Manual' })
  } catch {
    // ignore - the token is dropped locally regardless
  }
}

function toLoginErrorCode(error: unknown): LoginErrorCode {
  if (error instanceof ApiError) {
    const code = error.code
    if (code === LoginErrorCodes.InvalidCredentials || code === LoginErrorCodes.AccountDisabled) {
      return code
    }
    return error.status === 401 ? LoginErrorCodes.InvalidCredentials : LoginErrorCodes.ServerError
  }

  // fetch() rejects rather than resolving when the server cannot be reached.
  return LoginErrorCodes.ServerUnreachable
}
