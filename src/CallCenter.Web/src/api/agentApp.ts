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
  /** The same version as a zip, for when the installer will not run. Null when none was uploaded. */
  zipFileName: string | null
  zipSizeBytes: number | null
}

/** Why an upload was refused. Each has a label under `agentApp.upload.errors`. */
export type UploadErrorCode =
  | 'bad_version'
  | 'not_a_program'
  | 'not_a_zip'
  | 'version_mismatch'
  | 'no_installer'
  | 'too_large'
  | 'failed'

const UPLOAD_ERRORS: readonly string[] = ['bad_version', 'not_a_program', 'not_a_zip', 'version_mismatch', 'no_installer', 'too_large']

/** What is on offer, or null before the first upload (404 `no_installer`). */
export async function fetchAgentAppInstaller(): Promise<AgentAppInstaller | null> {
  try {
    return await api.get<AgentAppInstaller>('/agent-app')
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return null
    throw error
  }
}

/**
 * True when `own` is an older version than `offered`, compared number by
 * number as the Agent App does (A-82): 0.8.10 is newer than 0.8.9. False when
 * either cannot be read, so nobody is called out of date on a guess.
 */
export function isOlderVersion(own: string, offered: string): boolean {
  const parse = (v: string) => (/^\d+(\.\d+)*$/.test(v) ? v.split('.').map(Number) : null)
  const a = parse(own)
  const b = parse(offered)
  if (!a || !b) return false
  for (let i = 0; i < Math.max(a.length, b.length); i++) {
    const d = (a[i] ?? 0) - (b[i] ?? 0)
    if (d !== 0) return d < 0
  }
  return false
}

/** The program itself. Fetched rather than linked, because a link cannot carry the token. */
export const downloadAgentAppInstaller = () => requestBlob('/agent-app/installer')

/** The zip of the same version, the fallback when the installer will not run (S-63). */
export const downloadAgentAppZip = () => requestBlob('/agent-app/zip')

/**
 * Replaces the installer every laptop is offered. The file is sent as the
 * body, not as a form, so the server writes it straight to disk.
 */
export const uploadAgentAppInstaller = (file: File, version: string) =>
  api.put<AgentAppInstaller>('/agent-app/installer', file, {
    query: { version },
    headers: { 'Content-Type': 'application/octet-stream' },
  })

/** Adds the zip of the installer's version. Refused for any other version. */
export const uploadAgentAppZip = (file: File, version: string) =>
  api.put<AgentAppInstaller>('/agent-app/zip', file, {
    query: { version },
    headers: { 'Content-Type': 'application/octet-stream' },
  })

/** The refusal's code, when it is one the page has words for. */
export function uploadErrorCode(error: unknown): UploadErrorCode {
  const code = error instanceof ApiError ? error.code : null
  if (code && UPLOAD_ERRORS.includes(code)) return code as UploadErrorCode
  // 413 comes from the server's size limit, before the endpoint runs.
  if (error instanceof ApiError && error.status === 413) return 'too_large'
  return 'failed'
}

/**
 * The version in a file name publish.ps1 wrote, e.g.
 * SmashedAgentApp-Setup-0.4.1.exe or SmashedAgentApp-0.4.1.zip → 0.4.1.
 * Empty when there is none, and the supervisor types it.
 */
export function versionFromFileName(name: string): string {
  return name.match(/-(\d+(?:\.\d+){2,3})\.(?:exe|zip)$/i)?.[1] ?? ''
}
