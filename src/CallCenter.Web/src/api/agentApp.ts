/**
 * The Agent App installer the server hands out (N-11, S-63). The shape mirrors
 * `AgentAppInstallerDto` in the server's AgentAppInstaller feature.
 */
import { ApiError, api, requestBlob } from './client'

export interface AgentAppInstaller {
  version: string
  /** What the browser saves it as: SmashedAgentApp-Setup-<version>.exe. */
  fileName: string
  sizeBytes: number
  uploadedAt: string
  /** The supervisor's login. */
  uploadedBy: string
}

/** Why an upload was refused. Each has a label under `agentApp.upload.errors`. */
export type UploadErrorCode = 'bad_version' | 'not_a_program' | 'too_large' | 'failed'

/** What is on offer, or null before the first upload (404 `no_installer`). */
export async function fetchAgentAppInstaller(): Promise<AgentAppInstaller | null> {
  try {
    return await api.get<AgentAppInstaller>('/agent-app')
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return null
    throw error
  }
}

/** The program itself. Fetched rather than linked, because a link cannot carry the token. */
export const downloadAgentAppInstaller = () => requestBlob('/agent-app/installer')

/**
 * Replaces the installer every laptop is offered. The file is sent as the
 * body, not as a form, so the server writes it straight to disk.
 */
export const uploadAgentAppInstaller = (file: File, version: string) =>
  api.put<AgentAppInstaller>('/agent-app/installer', file, {
    query: { version },
    headers: { 'Content-Type': 'application/octet-stream' },
  })

/** The refusal's code, when it is one the page has words for. */
export function uploadErrorCode(error: unknown): UploadErrorCode {
  const code = error instanceof ApiError ? error.code : null
  if (code === 'bad_version' || code === 'not_a_program' || code === 'too_large') return code
  // 413 comes from the server's size limit, before the endpoint runs.
  if (error instanceof ApiError && error.status === 413) return 'too_large'
  return 'failed'
}

/**
 * The version in a file name publish.ps1 wrote, e.g.
 * SmashedAgentApp-Setup-0.4.1.exe → 0.4.1. Empty when there is none, and the
 * supervisor types it.
 */
export function versionFromFileName(name: string): string {
  return name.match(/(\d+(?:\.\d+){2,3})\.exe$/i)?.[1] ?? ''
}
