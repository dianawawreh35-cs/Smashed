/**
 * Account management (S-42). Mirrors `CallCenter.Shared.Contracts.Users` —
 * hand-written, so a change on the server has to be copied across.
 */
import { ApiError, api } from './client'

export interface User {
  id: string
  login: string
  displayName: string
  role: string
  isActive: boolean
  customerExtension: string | null
  internalExtension: string | null
  /** Both extensions and both secrets are set. The secrets are never returned. */
  hasSipCredentials: boolean
  canTakeCalls: boolean
  createdAt: string
  lastLoginAt: string | null
}

export interface CreateUserRequest {
  login: string
  displayName: string
  role: string
  password: string
}

export interface SetExtensionsRequest {
  customerExtension: string
  internalExtension: string
  /** Null or empty keeps the stored secret, so a number can be corrected alone. */
  customerSecret?: string
  internalSecret?: string
}

export const listUsers = () => api.get<User[]>('/users')

export const createUser = (request: CreateUserRequest) => api.post<User>('/users', request)

export const updateUser = (id: string, displayName: string, isActive: boolean) =>
  api.put<User>(`/users/${id}`, { displayName, isActive })

export const setExtensions = (id: string, request: SetExtensionsRequest) =>
  api.put<User>(`/users/${id}/extensions`, request)

export const resetPassword = (id: string, newPassword: string) =>
  api.post<void>(`/users/${id}/password`, { newPassword })

/**
 * The `code` from a refusal, for translation. Falls back to a generic key so
 * the screen always has something to show (A-80).
 */
export function errorCodeOf(error: unknown): string {
  if (error instanceof ApiError) {
    return error.code ?? (error.status === 400 ? 'invalid_request' : 'server_error')
  }
  return 'server_unreachable'
}
