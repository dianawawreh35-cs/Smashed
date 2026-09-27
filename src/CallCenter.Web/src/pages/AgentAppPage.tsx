import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  downloadAgentAppInstaller,
  fetchAgentAppInstaller,
  uploadAgentAppInstaller,
  uploadErrorCode,
  versionFromFileName,
} from '../api/agentApp'
import type { AgentAppInstaller, UploadErrorCode } from '../api/agentApp'
import { UserRoles } from '../api/auth'
import { useAuth } from '../auth/context'
import LoadError from '../components/LoadError'
import { ltr } from '../lib/bidi'
import { downloadBlob } from '../lib/csv'

const QUERY_KEY = ['agent-app-installer']

/**
 * The Agent App installer (N-11, S-63). An agent signs in to the web app and
 * this is the only page they see: the version on offer, a Download button, and
 * how to install it. A supervisor sees the same, and under it the form that
 * uploads a new version.
 */
export default function AgentAppPage() {
  const { t, i18n } = useTranslation()
  const { user } = useAuth()
  const isSupervisor = user?.role === UserRoles.Supervisor

  const query = useQuery({ queryKey: QUERY_KEY, queryFn: fetchAgentAppInstaller })

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
        ) : query.data === null ? (
          <p role="status" className="notice-warning">
            {t(isSupervisor ? 'agentApp.noneSupervisor' : 'agentApp.noneAgent')}
          </p>
        ) : (
          <Download installer={query.data} language={i18n.language} />
        )}
      </section>

      {query.data && (
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

      {isSupervisor && <Upload />}
    </div>
  )
}

function Download({ installer, language }: { installer: AgentAppInstaller; language: string }) {
  const { t } = useTranslation()
  const [busy, setBusy] = useState(false)
  const [failed, setFailed] = useState(false)

  // Fetched whole and then saved, because a plain link cannot carry the token.
  // 60-80 MB over the LAN is seconds, and the button says it is working.
  async function onDownload() {
    setBusy(true)
    setFailed(false)
    try {
      downloadBlob(installer.fileName, await downloadAgentAppInstaller())
    } catch {
      setFailed(true)
    } finally {
      setBusy(false)
    }
  }

  const size = (installer.sizeBytes / 1024 / 1024).toLocaleString(language, { maximumFractionDigits: 0 })
  const date = new Date(installer.uploadedAt).toLocaleString(language, { dateStyle: 'medium', timeStyle: 'short' })

  return (
    <div className="card card-body flex flex-wrap items-center justify-between gap-4">
      <div className="space-y-1">
        <p className="text-lg font-semibold text-slate-100">
          {t('agentApp.version', { version: ltr(installer.version) })}
        </p>
        <p className="text-sm text-slate-400">{t('agentApp.details', { size, date })}</p>
      </div>

      <div className="space-y-2">
        <button type="button" className="btn-primary" onClick={() => void onDownload()} disabled={busy}>
          {busy ? t('agentApp.downloading') : t('agentApp.download')}
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

/** S-63: a supervisor replaces what every laptop is offered. */
function Upload() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const fileInput = useRef<HTMLInputElement>(null)

  const [file, setFile] = useState<File | null>(null)
  const [version, setVersion] = useState('')
  const [error, setError] = useState<UploadErrorCode | null>(null)
  const [done, setDone] = useState<string | null>(null)

  const upload = useMutation({
    mutationFn: () => uploadAgentAppInstaller(file!, version.trim()),
    onSuccess: (installer) => {
      queryClient.setQueryData(QUERY_KEY, installer)
      setDone(installer.version)
      setFile(null)
      setVersion('')
      if (fileInput.current) fileInput.current.value = ''
    },
    onError: (failure) => setError(uploadErrorCode(failure)),
  })

  function onFile(chosen: File | null) {
    setFile(chosen)
    setError(null)
    setDone(null)
    // publish.ps1 names the file after the version, so it is seldom typed.
    if (chosen) setVersion(versionFromFileName(chosen.name))
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (!file) return
    setError(null)
    setDone(null)
    upload.mutate()
  }

  return (
    <form onSubmit={onSubmit} className="space-y-4" aria-label={t('agentApp.upload.heading')}>
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
          {t(`agentApp.upload.errors.${error}`)}
        </p>
      )}

      <div className="card card-body space-y-5">
        <label className="field">
          <span className="field-label">{t('agentApp.upload.file')}</span>
          <input
            ref={fileInput}
            type="file"
            accept=".exe"
            disabled={upload.isPending}
            onChange={(e) => onFile(e.target.files?.[0] ?? null)}
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
            disabled={upload.isPending}
            onChange={(e) => setVersion(e.target.value)}
            className={`input ${error === 'bad_version' ? 'input-invalid' : ''}`}
          />
        </label>

        <button type="submit" className="btn-primary" disabled={!file || !version.trim() || upload.isPending}>
          {upload.isPending ? t('agentApp.upload.uploading') : t('agentApp.upload.submit')}
        </button>
      </div>
    </form>
  )
}
