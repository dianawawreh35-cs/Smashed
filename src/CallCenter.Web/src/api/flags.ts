/**
 * The VIP and Blocked flags (S-45). Mirrors
 * `CallCenter.Shared.Contracts.Contacts.ContactFlagsDto` — hand-written, so a
 * change on the server has to be copied across.
 */
import { api } from './client'

/** A contact as the flag endpoints return it after a change (S-45). */
export interface FlaggedContact {
  id: string
  name: string | null
  address: string | null
  isVip: boolean
  isBlocked: boolean
  flagReason: string | null
  /** Who last set the flag. */
  changedByDisplayName: string | null
  changedAt: string | null
  /** As they were typed. Matching uses the normalised form. */
  numbers: string[]
}

/** One flag change, read back out of the audit log (S-45, N-06). */
export interface ContactFlagChange {
  at: string
  byDisplayName: string | null
  isVip: boolean
  isBlocked: boolean
  reason: string | null
}

export interface SetContactFlagsRequest {
  isVip: boolean
  isBlocked: boolean
  /** Required whenever a flag is being set; ignored when both are false. */
  reason: string | null
}

export interface FlagNumberRequest extends SetContactFlagsRequest {
  number: string
}

/**
 * Sets, changes or removes a contact's flags. Both false removes them — there
 * is no separate delete, so every change lands in the log the same way.
 */
export const setContactFlags = (id: string, request: SetContactFlagsRequest) =>
  api.put<FlaggedContact>(`/contacts/${id}/flags`, request)

/**
 * Flags a bare number. The server matches it against existing contacts first,
 * so flagging 0599… flags the customer saved as +970599… rather than creating a
 * second record for the same person.
 */
export const flagNumber = (request: FlagNumberRequest) =>
  api.post<FlaggedContact>('/contacts/flags/by-number', request)

export const flagHistory = (id: string) =>
  api.get<ContactFlagChange[]>(`/contacts/${id}/flags/history`)
