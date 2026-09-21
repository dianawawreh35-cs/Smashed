/**
 * The menu (A-66, S-59). Mirrors `CallCenter.Shared.Contracts.Menu` —
 * hand-written, so a change on the server has to be copied across.
 */
import { api, requestBlob } from './client'

export interface MenuCategory {
  id: string
  name: string
  sortOrder: number
  isActive: boolean
  itemCount: number
}

export interface MenuItem {
  id: string
  categoryId: string
  categoryName: string
  name: string
  description: string | null
  /** Null means the menu prints no price — which is not zero, a free extra. */
  price: number | null
  /** With fries and a drink. Null when there is no meal. */
  mealPrice: number | null
  /** The price is added to another item rather than being one of its own. */
  isSurcharge: boolean
  hasImage: boolean
  isActive: boolean
}

export interface UpsertMenuItemRequest {
  categoryId: string
  name: string
  description: string | null
  price: number | null
  mealPrice: number | null
  isSurcharge: boolean
  isActive: boolean
}

export const listMenuCategories = () =>
  api.get<MenuCategory[]>('/menu/categories', { query: { includeInactive: true } })

export const searchMenu = (query: string, categoryId?: string) =>
  api.get<MenuItem[]>('/menu', {
    query: { q: query || undefined, categoryId, includeInactive: true },
  })

export const createMenuItem = (request: UpsertMenuItemRequest) =>
  api.post<MenuItem>('/menu', request)

export const updateMenuItem = (id: string, request: UpsertMenuItemRequest) =>
  api.put<MenuItem>(`/menu/${id}`, request)

export const deleteMenuItem = (id: string) => api.delete<void>(`/menu/${id}`)

export interface UpsertMenuCategoryRequest {
  name: string
  sortOrder: number
  isActive: boolean
}

export const createMenuCategory = (request: UpsertMenuCategoryRequest) =>
  api.post<MenuCategory>('/menu/categories', request)

export const updateMenuCategory = (id: string, request: UpsertMenuCategoryRequest) =>
  api.put<MenuCategory>(`/menu/categories/${id}`, request)

export const deleteMenuCategory = (id: string) => api.delete<void>(`/menu/categories/${id}`)

/**
 * One item's picture, as bytes.
 *
 * Fetched rather than pointed at with an `<img src>`, because the endpoint
 * requires a signed-in user and an image tag cannot send the token — see
 * `requestBlob`. `MenuImage` turns the result into something a tag can show.
 *
 * `stamp` changes whenever an item is saved. The server marks pictures good for
 * a day, which is right for agents and wrong for the supervisor who has just
 * replaced one: without it they would upload a new photograph and go on seeing
 * the old one until tomorrow.
 */
export const fetchMenuImage = (id: string, stamp: number) =>
  requestBlob(`/menu/${id}/image`, { query: { v: stamp } })

/**
 * Replaces an item's picture, or removes it when `file` is null.
 *
 * Sent as a form rather than JSON: a 2 MB photograph base64-encoded into JSON
 * is a third larger and has to be decoded twice.
 */
export const setMenuImage = (id: string, file: File | null) => {
  const body = new FormData()
  if (file) body.append('file', file)

  return api.put<void>(`/menu/${id}/image`, body)
}
