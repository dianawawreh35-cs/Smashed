/**
 * The websites shown as tabs inside the Agent App (A-88). Mirrors
 * `CallCenter.Shared.Contracts.Websites` — hand-written, so a change on the
 * server has to be copied across.
 */
import { api } from './client'

/** `own`: each agent signs in themselves. `shared`: the app signs in with the login set here. */
export type WebsiteLogin = 'own' | 'shared'

export interface Website {
  id: string
  nameAr: string
  nameEn: string
  url: string
  login: WebsiteLogin
  username: string | null
  /** A password is stored. The page is never sent the password itself. */
  hasPassword: boolean
  alertsWithSound: boolean
  /** The caller's cart with `{number}`; set on the POS only (A-85). */
  cartUrl: string | null
  usernameSelector: string | null
  passwordSelector: string | null
  submitSelector: string | null
  sortOrder: number
  isActive: boolean
}

export interface UpsertWebsiteRequest {
  nameAr: string
  nameEn: string
  url: string
  login: WebsiteLogin
  username: string | null
  /** Null keeps the stored password; an empty string removes it. */
  password: string | null
  alertsWithSound: boolean
  cartUrl: string | null
  usernameSelector: string | null
  passwordSelector: string | null
  submitSelector: string | null
  sortOrder: number
  isActive: boolean
}

export const listWebsites = () => api.get<Website[]>('/websites')

export const createWebsite = (request: UpsertWebsiteRequest) => api.post<Website>('/websites', request)

export const updateWebsite = (id: string, request: UpsertWebsiteRequest) =>
  api.put<Website>(`/websites/${id}`, request)

export const deleteWebsite = (id: string) => api.delete<void>(`/websites/${id}`)

/** The request that saves a row as it is, keeping its password. */
export const asRequest = (w: Website): UpsertWebsiteRequest => ({
  nameAr: w.nameAr,
  nameEn: w.nameEn,
  url: w.url,
  login: w.login,
  username: w.username,
  password: null,
  alertsWithSound: w.alertsWithSound,
  cartUrl: w.cartUrl,
  usernameSelector: w.usernameSelector,
  passwordSelector: w.passwordSelector,
  submitSelector: w.submitSelector,
  sortOrder: w.sortOrder,
  isActive: w.isActive,
})
