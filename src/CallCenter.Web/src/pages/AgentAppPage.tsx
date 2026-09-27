import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  downloadAgentAppInstaller,
  downloadAgentAppZip,
  fetchAgentAppInstaller,
  uploadAgentAppInstaller,
  uploadAgentAppZip,
  uploadErrorCode,
  versionFromFileName,
} from '../api/agentApp'
import type { UploadErrorCode } from '../api/agentApp'
import { UserRoles } from '../api/auth'
import { useAuth } from '../auth/context'
import LoadError from '../components/LoadError'
import { ltr } from '../lib/bidi'
import { downloadBlob } from '../lib/csv'

const QUERY_KEY = ['agent-app-installer']

/**
 * The Agent App installer (N-11, S-63). An agent signs in to the web app and
 * this is the only page they see: the version on offer, a Download button, and
 * how to install it; and, when the installer will not run on a laptop, the same
 * version as a zip with the steps to put it in place by hand. A supervisor sees
 * the same, and under it the form that uploads a new version.
 */
export default function AgentAppPage() {
  const { t, i18n } = useTranslation()
  const { user } = useAuth()
  const isSupervisor = user?.role === UserRoles.Supervisor

  const query = useQuery({ queryKey: QUERY_KEY, queryFn: fetchAgentAppInstaller })
  const installer = query.data

  return (
    <div className="max-w-3xl space-y-8">
      <section className="space-y-4" aria-label={t('agentApp.heading')}>
        <div>
          <h1 className="page-title">{t('agentApp.heading')}</h1>
          <p className="page-subtitle">{t('agentApp.intro')}</p>
        </div>

        {query.isPending ? (
          <p className="text-slate-400">{t('app.loading')}</p>
        ) : query.isError ? (
          <LoadError message={t('agentApp.failed')} onRetry={() => void query.refetch()} busy={query.isFetching} />
        ) : installer === null ? (
          <p role="status" className="notice-warning">
            {t(isSupervisor ? 'agentApp.noneSupervisor' : 'agentApp.noneAgent')}
          </p>
        ) : (
          <DownloadCard
            title={t('agentApp.version', { version: ltr(installer!.version) })}
            details={t('agentApp.details', {
              size: megabytes(installer!.sizeBytes, i18n.language),
              date: new Date(installer!.uploadedAt).toLocaleString(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }),
            })}
            label={t('agentApp.download')}
            fileName={installer!.fileName}
            fetch={downloadAgentAppInstaller}
            primary
          />
        )}
      </section>

      {installer && (
        <section className="space-y-3" aria-label={t('agentApp.steps.heading')}>
          <h2 className="text-base font-semibold text-slate-100">{t('agentApp.steps.heading')}</h2>
          <ol className="list-decimal space-y-2 ps-5 text-sm text-slate-300">
            <li>{t('agentApp.steps.close')}</li>
            <li>{t('agentApp.steps.open')}</li>
            <li>{t('agentApp.steps.follow')}</li>
            <li>{t('agentApp.steps.start')}</li>
          </ol>
        </section>
      )}

      {installer?.zipFileName && (
        <section className="space-y-3" aria-label={t('agentApp.zip.heading')}>
          <div>
            <h2 className="text-base font-semibold text-slate-100">{t('agentApp.zip.heading')}</h2>
            <p className="text-sm text-slate-400">{t('agentApp.zip.intro')}</p>
          </div>

          <DownloadCard
            title={installer.zipFileName}
            details={t('agentApp.zip.details', { size: megabytes(installer.zipSizeBytes ?? 0, i18n.language) })}
            label={t('agentApp.zip.download')}
            fileName={installer.zipFileName}
            fetch={downloadAgentAppZip}
          />

          <ol className="list-decimal space-y-2 ps-5 text-sm text-slate-300">
            <li>{t('agentApp.zip.steps.close')}</li>
            <li>{t('agentApp.zip.steps.unblock')}</li>
            <li>{t('agentApp.zip.steps.empty')}</li>
            <li>{t('agentApp.zip.steps.extract')}</li>
            <li>{t('agentApp.zip.steps.start')}</li>
          </ol>

          {/* Prompt 20: two copies signed in as one agent took each other's calls. */}
          <p className="notice-warning">{t('agentApp.zip.once')}</p>
        </section>
      )}

      {isSupervisor && <Upload />}
    </div>
  )
}

function megabytes(bytes: number, language: string): string {
  return (bytes / 1024 / 1024).toLocaleString(language, { maximumFractionDigits: 0 })
}

function DownloadCard({
  title,
  details,
  label,
  fileName,
  fetch,
  primary = false,
}: {
  title: string
  details: string
  label: string
  fileName: string
  fetch: () => Promise<Blob>
  primary?: boolean
}) {
  const { t } = useTranslation()
  const [busy, setBusy] = useState(false)
  const [failed, setFailed] = useState(false)

  // Fetched whole and then saved, because a plain link cannot carry the token.
  // 60-80 MB over the LAN is seconds, and the button says it is working.
  async function onDownload() {
    setBusy(true)
    setFailed(false)
    try {
      downloadBlob(fileName, await fetch())
    } catch {
      setFailed(true)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="card card-body flex flex-wrap items-center justify-between gap-4">
      <div className="space-y-1">
        <p className={primary ? 'text-lg font-semibold text-slate-100' : 'font-medium text-slate-100'} dir="auto">
          {title}
        </p>
        <p className="text-sm text-slate-400">{details}</p>
      </div>

      <div className="space-y-2">
        <button
          type="button"
          className={primary ? 'btn-primary' : 'btn-ghost'}
          onClick={() => void onDownload()}
          disabled={busy}
        >
          {busy ? t('agentApp.downloading') : label}
        </button>
        {failed && (
          <p role="alert" className="notice-error">
            {t('agentApp.downloadFailed')}
          </p>
        )}
      </div>
    </div>
  )
}

/**
 * S-63: a supervisor replaces what every laptop is offered. The installer and,
 * optionally, the zip of the same build, sent one after the other. A zip on
 * its own is added to the version already on offer, which the server checks.
 */
function Upload() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const installerInput = useRef<HTMLInputElement>(null)
  const zipInput = useRef<HTMLInputElement>(null)

  const [installer, setInstaller] = useState<File | null>(null)
  const [zip, setZip] = useState<File | null>(null)
  const [version, setVersion] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<UploadErrorCode | null>(null)
  const [done, setDone] = useState<string | null>(null)
  // The installer went up and then the zip was refused: say which.
  const [zipOnlyFailed, setZipOnlyFailed] = useState(false)

  function onChoose(kind: 'installer' | 'zip', chosen: File | null) {
    if (kind === 'installer') setInstaller(chosen)
    else setZip(chosen)
    setError(null)
    setDone(null)
    setZipOnlyFailed(false)
    // publish.ps1 names both files after the version, so it is seldom typed.
    // The installer's name wins; the zip's only fills an empty box.
    const found = chosen ? versionFromFileName(chosen.name) : ''
    if (found && (kind === 'installer' || !version)) setVersion(found)
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (!installer && !zip) return

    setBusy(true)
    setError(null)
    setDone(null)
    setZipOnlyFailed(false)

    const chosenVersion = version.trim()
    let installerSaved = false

    try {
      if (installer) {
        queryClient.setQueryData(QUERY_KEY, await uploadAgentAppInstaller(installer, chosenVersion))
        installerSaved = true
      }
      if (zip) {
        queryClient.setQueryData(QUERY_KEY, await uploadAgentAppZip(zip, chosenVersion))
      }

      setDone(chosenVersion)
      setInstaller(null)
      setZip(null)
      setVersion('')
      if (installerInput.current) installerInput.current.value = ''
      if (zipInput.current) zipInput.current.value = ''
    } catch (failure) {
      setError(uploadErrorCode(failure))
      setZipOnlyFailed(installerSaved)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={(e) => void onSubmit(e)} className="space-y-4" aria-label={t('agentApp.upload.heading')}>
      <div>
        <h2 className="page-title">{t('agentApp.upload.heading')}</h2>
        <p className="page-subtitle">{t('agentApp.upload.intro')}</p>
      </div>

      {done && (
        <p role="status" className="notice-success">
          {t('agentApp.upload.done', { version: ltr(done) })}
        </p>
      )}
      {error && (
        <p role="alert" className="notice-error">
          {zipOnlyFailed && `${t('agentApp.upload.zipFailed')} `}
          {t(`agentApp.upload.errors.${error}`)}
        </p>
      )}

      <div className="card card-body space-y-5">
        <label className="field">
          <span className="field-label">{t('agentApp.upload.file')}</span>
          <input
            ref={installerInput}
            type="file"
            accept=".exe"
            disabled={busy}
            onChange={(e) => onChoose('installer', e.target.files?.[0] ?? null)}
            className="input"
          />
        </label>

        <label className="field">
          <span className="field-label">{t('agentApp.upload.zip')}</span>
          <span className="field-hint">{t('agentApp.upload.zipHint')}</span>
          <input
            ref={zipInput}
            type="file"
            accept=".zip"
            disabled={busy}
            onChange={(e) => onChoose('zip', e.target.files?.[0] ?? null)}
            className="input"
          />
        </label>

        <label className="field">
          <span className="field-label">{t('agentApp.upload.version')}</span>
          <span className="field-hint">{t('agentApp.upload.versionHint')}</span>
          <input
            type="text"
            dir="ltr"
            value={version}
            disabled={busy}
            onChange={(e) => setVersion(e.target.value)}
            className={`input ${error === 'bad_version' || error === 'version_mismatch' ? 'input-invalid' : ''}`}
          />
        </label>

        <button type="submit" className="btn-primary" disabled={(!installer && !zip) || !version.trim() || busy}>
          {busy ? t('agentApp.upload.uploading') : t('agentApp.upload.submit')}
        </button>
      </div>
    </form>
  )
}
