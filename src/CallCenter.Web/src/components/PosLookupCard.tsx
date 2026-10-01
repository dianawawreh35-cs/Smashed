import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { getPosLookup, runPosLookup } from '../api/posLookup'
import type { PosLookupStatus } from '../api/posLookup'
import LoadError from './LoadError'

const KEY = ['pos', 'lookup']

/**
 * The POS customer lookup (A-67): what its last run did, and Check now, which
 * asks the POS straight away about every number that called in the last two
 * days and has no contact. The timer does the same every `pos.lookup.interval_minutes`; how
 * often is in the system settings above.
 *
 * It refreshes itself every minute, so a run the timer made shows up without
 * reloading the page.
 */
export default function PosLookupCard() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery({ queryKey: KEY, queryFn: getPosLookup, refetchInterval: 60_000 })

  const run = useMutation({
    mutationFn: runPosLookup,
    onSuccess: (s) => queryClient.setQueryData(KEY, s),
  })

  const heading = <h2 className="page-title">{t('settings.posLookup.heading')}</h2>

  if (status.isPending) return <p className="text-slate-400">{t('app.loading')}</p>

  if (status.isError && !status.data) {
    return (
      <section className="space-y-4" aria-label={t('settings.posLookup.heading')}>
        {heading}
        <LoadError message={t('settings.posLookup.failed')} onRetry={() => void status.refetch()} busy={status.isFetching} />
      </section>
    )
  }

  const s = status.data
  const off = !s.enabled || (run.error instanceof ApiError && run.error.code === 'pos_lookup_off')

  return (
    <section className="space-y-4" aria-label={t('settings.posLookup.heading')}>
      <div>
        {heading}
        <p className="page-subtitle">{t('settings.posLookup.intro', { minutes: s.intervalMinutes })}</p>
      </div>

      <div className="card card-body space-y-3 text-sm">
        {off ? <p className="text-slate-400">{t('settings.posLookup.off')}</p> : <LastRun status={s} />}
        {run.isError && !off && <p role="alert" className="notice-error">{t('settings.posLookup.runFailed')}</p>}
      </div>

      {!off && (
        <button type="button" disabled={run.isPending || s.running} className="btn-primary" onClick={() => run.mutate()}>
          {run.isPending || s.running ? t('settings.posLookup.checking') : t('settings.posLookup.checkNow')}
        </button>
      )}
    </section>
  )
}

/** When the last run was, and what it found. */
function LastRun({ status: s }: { status: PosLookupStatus }) {
  const { t, i18n } = useTranslation()

  if (!s.lastStartedAt) return <p className="text-slate-400">{t('settings.posLookup.none')}</p>

  const when = new Date(s.lastStartedAt).toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' })

  return (
    <div className="space-y-1">
      <p className="text-slate-300">{t('settings.posLookup.last', { when })}</p>
      <p className="text-slate-400">
        {t('settings.posLookup.counts', {
          asked: s.asked, created: s.created, filledIn: s.filledIn, notFound: s.notFound, calls: s.callsLinked,
        })}
      </p>
      {s.failed && <p className="text-red-400">{t('settings.posLookup.posDown')}</p>}
    </div>
  )
}
