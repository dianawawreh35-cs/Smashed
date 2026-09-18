/**
 * The shared contact list (A-60 to A-63). Mirrors
 * `CallCenter.Shared.Contracts.Contacts`.
 */
import { ApiError, api } from './client'

export interface ContactPhone {
  id: string
  /** As it was typed. */
  raw: string
  /** Digits only, E.164 without the '+'. What matching compares (A-13). */
  normalised: string
  isPrimary: boolean
}

export interface ContactSummary {
  id: string
  name: string | null
  address: string | null
  isVip: boolean
  isBlocked: boolean
  /** Why the contact is VIP or Blocked (S-45); null when neither. */
  flagReason: string | null
  phones: string[]
}

/** Narrows a search to the flagged contacts (S-45). */
export type ContactFilter = 'all' | 'vip' | 'blocked'

export interface Contact {
  id: string
  name: string | null
  address: string | null
  notes: string | null
  deliveryNotes: string | null
  isVip: boolean
  isBlocked: boolean
  flagReason: string | null
  phones: ContactPhone[]
  createdByDisplayName: string | null
  createdAt: string
  updatedAt: string
}

export interface UpsertContactRequest {
  name?: string | null
  address?: string | null
  notes?: string | null
  deliveryNotes?: string | null
  /** At least one. Any format — the server normalises them. */
  phones: string[]
}

/** Returned with a 409 when a number is already on another contact (A-63). */
export interface DuplicateNumber {
  number: string
  existingContactId: string
  existingContactName: string | null
}

/**
 * Searches, optionally narrowed to VIP or blocked contacts. The filter is part
 * of the search rather than a list of its own, so that the two compose and the
 * app has one contact list rather than two that can disagree (A-61, S-45).
 */
export const searchContacts = (query: string, filter: ContactFilter = 'all') =>
  api.get<ContactSummary[]>('/contacts', {
    query: { q: query || undefined, flag: filter === 'all' ? undefined : filter },
  })

export const getContact = (id: string) => api.get<Contact>(`/contacts/${id}`)

/**
 * Contacts that already carry this name (A-63). A warning before saving, never
 * a refusal — the agent decides whether it is the same person.
 */
export const findContactsByName = (name: string, excluding?: string) =>
  api.get<ContactSummary[]>('/contacts/by-name', {
    query: { name, excluding },
  })

/** Adds one number to a contact that already exists (A-63). */
export const addPhoneToContact = (id: string, number: string) =>
  api.post<Contact>(`/contacts/${id}/phones`, { number })

export const createContact = (request: UpsertContactRequest) =>
  api.post<Contact>('/contacts', request)

export const updateContact = (id: string, request: UpsertContactRequest) =>
  api.put<Contact>(`/contacts/${id}`, request)

/** The contact a number belongs to, or null. The lookup behind the pop-up (A-13). */
export async function findContactByPhone(number: string): Promise<Contact | null> {
  try {
    return await api.get<Contact>('/contacts/by-phone', { query: { number } })
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return null
    throw error
  }
}

/**
 * The contact that already holds the number, when a save was refused for that
 * reason — so the screen can offer to open it rather than only complaining.
 */
export function duplicateOf(error: unknown): DuplicateNumber | null {
  if (error instanceof ApiError && error.body && typeof error.body === 'object') {
    const duplicate = (error.body as { duplicate?: unknown }).duplicate
    if (duplicate && typeof duplicate === 'object') return duplicate as DuplicateNumber
  }
  return null
}
