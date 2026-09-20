/**
 * Delivery areas: which branch covers a place and what it costs (A-65, S-58).
 * Mirrors `CallCenter.Shared.Contracts.Delivery` — hand-written, so a change on
 * the server has to be copied across.
 */
import { api } from './client'

export interface Branch {
  id: string
  name: string
  isActive: boolean
}

export interface DeliveryArea {
  id: string
  name: string
  branchId: string
  branchName: string
  /** Zero is a real price — areas beside a branch deliver free. */
  price: number
  isActive: boolean
}

export interface UpsertDeliveryAreaRequest {
  name: string
  branchId: string
  price: number
  isActive: boolean
}

/** One line of a paste that could not be used, with why. */
export interface ImportProblem {
  /** 1-based, counting blank lines. Zero when the problem is not about one line. */
  line: number
  text: string
  /** A code to translate: no_price, bad_price, no_name, duplicate_in_paste, other_branch. */
  reason: string
  detail: string | null
}

export interface ImportResult {
  added: number
  updated: number
  removed: number
  problems: ImportProblem[]
}

export const listBranches = () => api.get<Branch[]>('/branches')

export const searchDeliveryAreas = (query: string, branchId?: string) =>
  api.get<DeliveryArea[]>('/delivery-areas', {
    query: { q: query || undefined, branchId, includeInactive: true },
  })

export const createDeliveryArea = (request: UpsertDeliveryAreaRequest) =>
  api.post<DeliveryArea>('/delivery-areas', request)

export const updateDeliveryArea = (id: string, request: UpsertDeliveryAreaRequest) =>
  api.put<DeliveryArea>(`/delivery-areas/${id}`, request)

export const deleteDeliveryArea = (id: string) => api.delete<void>(`/delivery-areas/${id}`)

/**
 * Adds a whole branch's list at once (S-58).
 *
 * `lines` is one area per line — name then price, separated by a tab or a
 * comma. Tab is what a paste out of Excel gives.
 */
export const importDeliveryAreas = (branchId: string, lines: string, replace: boolean) =>
  api.post<ImportResult>('/delivery-areas/import', { branchId, lines, replace })
