import { useEffect, useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { getPbxBlacklist, retryPbxBlacklist, savePbxBlacklist } from '../api/pbxBlacklist'
import type { PbxBlacklist, UpdatePbxBlacklist } from '../api/pbxBlacklist'
import { settingProblems } from '../api/settings'
import LoadError from './LoadError'

const KEY = ['pbx', 'blacklist']

/**
 * The PBX blacklist (S-46): the extension the server dials *30 and *31 from,
 * and how far the PBX has caught up with the Blocked flags.
 *
 * There is no block button here. Blocking is the Blocked flag on the contact;
 * this card only says whether the PBX has followed. It refreshes itself while
 * numbers are waiting, since each one is a phone call of its own.
 *
 * The password is write-only, as on the PBX import card. Like that card, it
 * shows no form when its values did not load, and says when a save failed
 * without naming a field (M-W04).
 */
export default function PbxBlacklistCard() {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery({
    queryKey: KEY,
    queryFn: getPbxBlacklist,
    refetchInterval: (query) => (query.state.data?.configured && query.state.data.waiting > 0 ? 15_000 : false),
  })

  const [draft, setDraft] = useState<UpdatePbxBlacklist>({ extension: '', secret: '' })
  const [problems, setProblems] = useState<Record<string, string>>({})
  const [saved, setSaved] = useState(false)
  const [notSaved, setNotSaved] = useState(false)

  // Filled once, when the values first arrive, and again from what a save
  // returns (M-W05). It used to refill on every refresh, and the card
  // refreshes every 15 seconds while numbers are waiting, so what the
  // supervisor was typing vanished mid-word.
  const filled = useRef(false)
  useEffect(() => {
    if (status.data && !filled.current) {
      filled.current = true
      setDraft({ extension: status.data.extension, secret: '' })
    }
  }, [status.data])

  const save = useMutation({
    mutationFn: () => savePbxBlacklist(draft),
    onSuccess: (s) => {
      setProblems({})
      setNotSaved(false)
      setSaved(true)
      setDraft({ extension: s.extension, secret: '' })
      queryClient.setQueryData(KEY, s)
    },
    onError: (error) => {
      setSaved(false)
      const fields = settingProblems(error)
      setProblems(fields)
      setNotSaved(Object.keys(fields).length === 0)
    },
  })

  const retry = useMutation({
    mutationFn: retryPbxBlacklist,
    onSuccess: (s) => queryClient.setQueryData(KEY, s),
  })

  if (status.isPending) return <p className="text-slate-400">{t('app.loading')}</p>

  if (status.isError && !status.data) {
    return (
      <section className="space-y-4" aria-label={t('settings.blacklist.heading')}>
        <h2 className="page-title">{t('settings.blacklist.heading')}</h2>
        <LoadError message={t('settings.blacklist.failed')} onRetry={() => void status.refetch()} busy={status.isFetching} />
      </section>
    )
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    setSaved(false)
    save.mutate()
  }

  const when = (iso: string) => new Date(iso).toLocaleString(i18n.language, { dateStyle: 'short', timeStyle: 'short' })
  const s = status.data

  return (
    <form onSubmit={onSubmit} className="space-y-4" aria-label={t('settings.blacklist.heading')}>
      <div>
        <h2 className="page-title">{t('settings.blacklist.heading')}</h2>
        <p className="page-subtitle">{t('settings.blacklist.intro')}</p>
      </div>

      {saved && Object.keys(problems).length === 0 && (
        <p role="status" className="notice-success">{t('settings.blacklist.saved')}</p>
      )}
      {notSaved && <p role="alert" className="notice-error">{t('common.notSaved')}</p>}

      <div className="card card-body space-y-5">
        <Field label={t('settings.blacklist.extension')} hint={t('settings.blacklist.extensionHint')} problem={problems.extension}>
          <input type="text" dir="ltr" inputMode="numeric" autoComplete="off" value={draft.extension}
            onChange={(e) => setDraft((d) => ({ ...d, extension: e.target.value }))}
            className={`input w-40 ${problems.extension ? 'input-invalid' : ''}`} />
        </Field>
        <Field label={t('settings.blacklist.secret')}
          hint={s?.secretSet ? t('settings.pbx.passwordSet') : t('settings.pbx.passwordUnset')}>
          <input type="password" dir="ltr" autoComplete="new-password" value={draft.secret}
            onChange={(e) => setDraft((d) => ({ ...d, secret: e.target.value }))} className="input" />
        </Field>

        {s && <Progress status={s} when={when} />}
        {retry.isError && <p role="alert" className="notice-error">{t('dashboard.failed')}</p>}
      </div>

      <div className="flex flex-wrap gap-3">
        <button type="submit" disabled={save.isPending} className="btn-primary">{t('settings.blacklist.save')}</button>
        {s && s.failures.length > 0 && (
          <button type="button" disabled={retry.isPending} className="btn-ghost" onClick={() => retry.mutate()}>
            {t('settings.blacklist.retry')}
          </button>
        )}
      </div>
    </form>
  )
}

/** How many numbers the PBX has, how many are waiting, and what failed. */
function Progress({ status: s, when }: { status: PbxBlacklist; when: (iso: string) => string }) {
  const { t } = useTranslation()

  if (!s.configured) return <p className="text-sm text-slate-400">{t('settings.blacklist.off')}</p>

  return (
    <div className="space-y-2 text-sm">
      <p className="text-slate-400">
        {t('settings.blacklist.counts', { onPbx: s.onPbx, waiting: s.waiting })}
        {s.lastSucceededAt && <> {t('settings.blacklist.lastSuccess', { when: when(s.lastSucceededAt) })}</>}
      </p>
      {s.failures.length > 0 && (
        <ul className="space-y-1">
          {s.failures.map((f) => (
            <li key={f.number} className="text-red-400">
              <span dir="ltr" className="font-mono">{f.number}</span>{' '}
              {t(f.adding ? 'settings.blacklist.failedAdd' : 'settings.blacklist.failedRemove',
                { when: when(f.at), attempts: f.attempts, error: f.error })}
            </li>
          ))}
        </ul>
      )}
    </div>
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
