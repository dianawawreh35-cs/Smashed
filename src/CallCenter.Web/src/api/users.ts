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
  extension: string | null
  /** The extension and its secret are set. The secret is never returned. */
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

export interface SetExtensionRequest {
  extension: string
  /** Null or empty keeps the stored secret, so the number can be corrected alone. */
  secret?: string
}

export const listUsers = () => api.get<User[]>('/users')

export const createUser = (request: CreateUserRequest) => api.post<User>('/users', request)

export const updateUser = (id: string, displayName: string, isActive: boolean) =>
  api.put<User>(`/users/${id}`, { displayName, isActive })

export const setExtension = (id: string, request: SetExtensionRequest) =>
  api.put<User>(`/users/${id}/extension`, request)

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
