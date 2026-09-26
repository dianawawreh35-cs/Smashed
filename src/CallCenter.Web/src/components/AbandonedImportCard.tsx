import { useEffect, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { fetchAbandoned, getAbandonedImport, saveAbandonedImport } from '../api/pbx'
import type { AbandonedImport, UpdateAbandonedImport } from '../api/pbx'
import { settingProblems } from '../api/settings'

const KEY = ['pbx', 'abandoned-import']

/**
 * The abandoned-call import (S-55): the PBX's web address, the login the
 * server uses, how often it checks, and how the last check went.
 *
 * The password is write-only. The server says whether one is stored, never
 * what it is, so the field starts empty and a blank save keeps the stored one.
 */
export default function AbandonedImportCard() {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery({ queryKey: KEY, queryFn: getAbandonedImport })

  const [draft, setDraft] = useState<UpdateAbandonedImport>({ url: '', username: '', password: '', intervalMinutes: 1 })
  const [problems, setProblems] = useState<Record<string, string>>({})
  const [saved, setSaved] = useState(false)

  // Fill the form once the values arrive, and again after a save returns them.
  useEffect(() => {
    if (status.data) {
      const s = status.data
      setDraft({ url: s.url, username: s.username, password: '', intervalMinutes: s.intervalMinutes })
    }
  }, [status.data])

  const save = useMutation({
    mutationFn: () => saveAbandonedImport(draft),
    onSuccess: (s) => {
      setProblems({})
      setSaved(true)
      queryClient.setQueryData(KEY, s)
    },
    onError: (error) => {
      setSaved(false)
      setProblems(settingProblems(error))
    },
  })

  const check = useMutation({
    mutationFn: () => fetchAbandoned(),
    onSuccess: (result) => {
      queryClient.setQueryData(KEY, result.status)
      if (result.ok) void queryClient.invalidateQueries({ queryKey: ['reports'] })
    },
  })

  if (status.isLoading) return <p className="text-slate-400">{t('app.loading')}</p>

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    setSaved(false)
    save.mutate()
  }

  const when = (iso: string) => new Date(iso).toLocaleString(i18n.language, { dateStyle: 'short', timeStyle: 'short' })
  const set = (patch: Partial<UpdateAbandonedImport>) => setDraft((d) => ({ ...d, ...patch }))
  const s = status.data
  const result = check.data

  return (
    <form onSubmit={onSubmit} className="space-y-4" aria-label={t('settings.pbx.heading')}>
      <div>
        <h2 className="page-title">{t('settings.pbx.heading')}</h2>
        <p className="page-subtitle">{t('settings.pbx.intro')}</p>
      </div>

      {saved && Object.keys(problems).length === 0 && (
        <p role="status" className="notice-success">{t('settings.pbx.saved')}</p>
      )}

      <div className="card card-body space-y-5">
        <Field label={t('settings.pbx.url')} hint={t('settings.pbx.urlHint')} problem={problems.url}>
          <input type="text" dir="ltr" value={draft.url} onChange={(e) => set({ url: e.target.value })}
            placeholder="https://10.8.0.1" className={`input ${problems.url ? 'input-invalid' : ''}`} />
        </Field>
        <Field label={t('settings.pbx.username')}>
          <input type="text" dir="ltr" autoComplete="off" value={draft.username}
            onChange={(e) => set({ username: e.target.value })} className="input" />
        </Field>
        <Field label={t('settings.pbx.password')}
          hint={s?.passwordSet ? t('settings.pbx.passwordSet') : t('settings.pbx.passwordUnset')}>
          <input type="password" dir="ltr" autoComplete="new-password" value={draft.password}
            onChange={(e) => set({ password: e.target.value })} className="input" />
        </Field>
        <Field label={t('settings.pbx.interval')} hint={t('settings.pbx.intervalHint')} problem={problems.intervalMinutes}>
          <input type="number" min={1} max={60} value={draft.intervalMinutes}
            onChange={(e) => set({ intervalMinutes: Number(e.target.value) })}
            className={`input w-28 ${problems.intervalMinutes ? 'input-invalid' : ''}`} />
        </Field>

        {s && <LastCheck status={s} when={when} />}

        {check.isError && <p role="alert" className="notice-error">{t('dashboard.failed')}</p>}
        {result && (
          <p role="status" className={result.ok ? 'notice-success' : 'notice-error'}>
            {result.ok
              ? t('settings.pbx.result', { from: result.from, to: result.to, calls: result.calls, abandoned: result.abandoned, added: result.added })
              : result.error}
          </p>
        )}
      </div>

      <div className="flex flex-wrap gap-3">
        <button type="submit" disabled={save.isPending} className="btn-primary">{t('settings.pbx.save')}</button>
        <button type="button" disabled={!s?.configured || check.isPending} className="btn-ghost"
          onClick={() => check.mutate()}>
          {check.isPending ? t('settings.pbx.checking') : t('settings.pbx.checkNow')}
        </button>
      </div>
    </form>
  )
}

/** How the last check went, in one or two lines. */
function LastCheck({ status: s, when }: { status: AbandonedImport; when: (iso: string) => string }) {
  const { t } = useTranslation()

  if (!s.configured) return <p className="text-sm text-slate-400">{t('settings.pbx.off')}</p>
  if (!s.lastCheckedAt) return <p className="text-sm text-slate-400">{t('settings.pbx.never')}</p>

  return s.lastError ? (
    <div className="space-y-1 text-sm">
      <p className="text-red-400">{t('settings.pbx.lastFailed', { when: when(s.lastCheckedAt), error: s.lastError })}</p>
      {s.lastSucceededAt && <p className="text-slate-500">{t('settings.pbx.lastSuccess', { when: when(s.lastSucceededAt) })}</p>}
    </div>
  ) : (
    <p className="text-sm text-slate-400">{t('settings.pbx.lastOk', { when: when(s.lastCheckedAt), added: s.lastAdded ?? 0 })}</p>
  )
}

function Field({ label, hint, problem, children }: {
  label: string
  hint?: string
  problem?: string
  children: ReactNode
}) {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      {hint && <span className="field-hint">{hint}</span>}
      {children}
      {problem && <span className="block text-xs text-red-400">{problem}</span>}
    </label>
  )
}
