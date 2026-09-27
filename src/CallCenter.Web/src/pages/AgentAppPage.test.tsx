import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen } from '@testing-library/react'
import AgentAppPage from './AgentAppPage'
import { versionFromFileName } from '../api/agentApp'
import { setToken } from '../auth/token'
import i18n from '../i18n'
import { jsonResponse, renderWithClient, routes } from '../test/http'

/** The Agent App installer page (S-63): what an agent downloads, and a supervisor's upload. */

const INSTALLER = {
  version: '0.4.1',
  fileName: 'SmashedAgentApp-Setup-0.4.1.exe',
  sizeBytes: 68 * 1024 * 1024,
  uploadedAt: '2026-09-27T10:00:00Z',
  uploadedBy: 'supervisor',
  zipFileName: null,
  zipSizeBytes: null,
}

const WITH_ZIP = { ...INSTALLER, zipFileName: 'SmashedAgentApp-0.4.1.zip', zipSizeBytes: 77 * 1024 * 1024 }

beforeEach(async () => {
  setToken('supervisor-token')
  await i18n.changeLanguage('en')
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('the Agent App page', () => {
  it('shows the version on offer and a download', async () => {
    vi.stubGlobal('fetch', vi.fn(routes([['/agent-app', () => jsonResponse(INSTALLER)]])))
    renderWithClient(<AgentAppPage />)

    expect(await screen.findByText(/Version .*0\.4\.1/)).toBeInTheDocument()
    expect(screen.getByText(/^68 MB/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Download the installer' })).toBeInTheDocument()
  })

  it('offers the zip, with its steps, only when one was uploaded', async () => {
    vi.stubGlobal('fetch', vi.fn(routes([['/agent-app', () => jsonResponse(WITH_ZIP)]])))
    const { unmount } = renderWithClient(<AgentAppPage />)

    expect(await screen.findByRole('heading', { name: 'If the installer does not work' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Download the zip' })).toBeInTheDocument()
    expect(screen.getByText(/^77 MB/)).toBeInTheDocument()
    expect(screen.getByText(/^Only ever into C:.SmashedAgentApp/)).toBeInTheDocument()
    unmount()

    vi.stubGlobal('fetch', vi.fn(routes([['/agent-app', () => jsonResponse(INSTALLER)]])))
    renderWithClient(<AgentAppPage />)

    expect(await screen.findByRole('button', { name: 'Download the installer' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Download the zip' })).not.toBeInTheDocument()
  })

  it('uploads the installer and then the zip in one go', async () => {
    const fetchMock = vi.fn(routes([
      ['/agent-app/installer', () => jsonResponse(INSTALLER)],
      ['/agent-app/zip', () => jsonResponse(WITH_ZIP)],
      ['/agent-app', () => jsonResponse({ code: 'no_installer' }, 404)],
    ]))
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<AgentAppPage />)

    const zip = new File(['PK'], 'SmashedAgentApp-0.4.1.zip')
    fireEvent.change(await screen.findByLabelText('Installer (.exe)'), {
      target: { files: [new File(['MZ'], 'SmashedAgentApp-Setup-0.4.1.exe')] },
    })
    fireEvent.change(screen.getByLabelText(/^Zip/), { target: { files: [zip] } })
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    expect(await screen.findByRole('button', { name: 'Download the zip' })).toBeInTheDocument()
    const puts = fetchMock.mock.calls.filter(([, i]) => i?.method === 'PUT').map(([url]) => String(url))
    expect(puts).toEqual(['/api/agent-app/installer?version=0.4.1', '/api/agent-app/zip?version=0.4.1'])
  })

  it('says the installer went up when only the zip was refused', async () => {
    vi.stubGlobal('fetch', vi.fn(routes([
      ['/agent-app/installer', () => jsonResponse(INSTALLER)],
      ['/agent-app/zip', () => jsonResponse({ code: 'not_a_zip' }, 400)],
      ['/agent-app', () => jsonResponse({ code: 'no_installer' }, 404)],
    ])))
    renderWithClient(<AgentAppPage />)

    fireEvent.change(await screen.findByLabelText('Installer (.exe)'), {
      target: { files: [new File(['MZ'], 'SmashedAgentApp-Setup-0.4.1.exe')] },
    })
    fireEvent.change(screen.getByLabelText(/^Zip/), { target: { files: [new File(['MZ'], 'SmashedAgentApp-0.4.1.zip')] } })
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The installer was uploaded, but the zip was not.')
    expect(alert).toHaveTextContent('That file is not a zip.')
  })

  it('says it could not check, rather than that there is nothing, when the server is down', async () => {
    vi.stubGlobal('fetch', vi.fn(routes([])))
    renderWithClient(<AgentAppPage />)

    expect(await screen.findByText(/could not be checked/)).toBeInTheDocument()
    expect(screen.queryByText(/No installer has been uploaded/)).not.toBeInTheDocument()
  })

  it('uploads the chosen file under the version in its name', async () => {
    const fetchMock = vi.fn(routes([
      ['/agent-app/installer', () => jsonResponse(INSTALLER)],
      ['/agent-app', () => jsonResponse({ code: 'no_installer' }, 404)],
    ]))
    vi.stubGlobal('fetch', fetchMock)
    renderWithClient(<AgentAppPage />)

    const file = new File(['MZ'], 'SmashedAgentApp-Setup-0.4.1.exe')
    fireEvent.change(await screen.findByLabelText('Installer (.exe)'), { target: { files: [file] } })
    expect(screen.getByLabelText(/^Version/)).toHaveValue('0.4.1')

    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    expect(await screen.findByText(/Agents now download version .*0\.4\.1/)).toBeInTheDocument()
    const [url, init] = fetchMock.mock.calls.find(([, i]) => i?.method === 'PUT')!
    expect(String(url)).toBe('/api/agent-app/installer?version=0.4.1')
    expect(init!.body).toBe(file)
  })

  it('says why the server refused an upload', async () => {
    vi.stubGlobal('fetch', vi.fn(routes([
      ['/agent-app/installer', () => jsonResponse({ code: 'not_a_program' }, 400)],
      ['/agent-app', () => jsonResponse(INSTALLER)],
    ])))
    renderWithClient(<AgentAppPage />)

    fireEvent.change(await screen.findByLabelText('Installer (.exe)'), {
      target: { files: [new File(['PK'], 'SmashedAgentApp-0.4.1.exe')] },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('That file is not a Windows program.')
  })
})

describe('versionFromFileName', () => {
  it.each([
    ['SmashedAgentApp-Setup-0.4.1.exe', '0.4.1'],
    ['SmashedAgentApp-Setup-0.3.2.1.exe', '0.3.2.1'],
    ['SmashedAgentApp-0.4.1.zip', '0.4.1'],
    ['SmashedAgentApp-Setup-0.4.1 (1).exe', ''],
    ['setup.exe', ''],
  ])('%s → "%s"', (name, version) => {
    expect(versionFromFileName(name)).toBe(version)
  })
})
