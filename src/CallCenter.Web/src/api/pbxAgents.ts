/**
 * The agents' phones as the PBX reports them (S-61), and listening in on a
 * call (S-62). Mirrors `CallCenter.Shared.Contracts.Pbx.AgentPhoneDto` —
 * hand-written, so a change on the server has to be copied across.
 */
import { api, requestStream } from './client'

export type PhoneState = 'Unknown' | 'Offline' | 'Free' | 'Ringing' | 'InCall'

export interface AgentPhone {
  userId: string
  displayName: string
  extension: string
  state: PhoneState
  /** When it went into that state, as far as the server has seen. */
  since: string | null
}

export interface AgentPhones {
  /** The server is subscribed to the PBX and hearing back. */
  live: boolean
  /** Why not: `not_configured`, `no_answer` or `not_started`. */
  problem: string | null
  agents: AgentPhone[]
}

const BASE = '/pbx/agents'

export const getAgentPhones = () => api.get<AgentPhones>(BASE)

/** The header the server names the listen-in by, so Stop can end it. */
export const LISTEN_ID_HEADER = 'X-Listen-Id'

/**
 * Starts listening in on an agent's call: the server dials *222 and the
 * extension, and once the PBX has answered the response is the call's sound,
 * 16-bit little-endian mono at 8 kHz, until it ends or `signal` aborts.
 */
export const openListen = (agentId: string, signal: AbortSignal) =>
  requestStream(`${BASE}/${agentId}/listen`, signal)

/** Ends a listen-in. Aborting the stream ends it too; this makes sure. */
export const stopListen = (listenId: string) => api.delete<void>(`${BASE}/listen/${listenId}`)
