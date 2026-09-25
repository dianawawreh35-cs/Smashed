/**
 * The channels a message can arrive on (S-41, A-70): Phone, and the apps the
 * supervisor lists. Mirrors `CallCenter.Shared.Contracts.Communications.ChannelDto`
 * — hand-written, so a change on the server has to be copied across.
 */
import { api } from './client'

export interface Channel {
  id: string
  name: string
  /** Phone. Every call is filed under it, so it can be neither renamed nor hidden. */
  isSystem: boolean
  sortOrder: number
  isActive: boolean
  /** Some communication is filed under it: it can be hidden but never removed. */
  inUse: boolean
}

export interface UpsertChannelRequest {
  name: string
  sortOrder: number
  isActive: boolean
}

/** Hidden channels too, for the management screen and for filters over old messages. */
export const listChannels = (includeInactive = true) =>
  api.get<Channel[]>('/channels', { query: { includeInactive } })

export const createChannel = (request: UpsertChannelRequest) =>
  api.post<Channel>('/channels', request)

/** Renames, reorders or hides a channel. Rows hold the id, so renaming is safe. */
export const updateChannel = (id: string, request: UpsertChannelRequest) =>
  api.put<Channel>(`/channels/${id}`, request)
