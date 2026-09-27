import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { getQueue, markQueue, switchQueue } from '../api/pbxQueue'
import type { QueueStatus, QueueSwitchResult } from '../api/pbxQueue'
import LoadError from './LoadError'

const KEY = ['pbx', 'queue']

/**
 * Open or close the call queue (S-60). The server dials *280, which the PBX
 * treats as a toggle.
 *
 * The state shown is what the server remembers from the last switch made
 * here: *280 dialled from a desk phone is not seen. So there is always a quiet
 * way to correct the display without calling the PBX, and until someone has
 * said which state the queue is in, that is the only thing the card offers.
 *
 * The queue also opens by itself every morning (queue.auto_open_time, on the
 * settings screen). The card says when, and why the day's opening did not
 * happen if it did not.
 *
 * **When the state cannot be loaded the card stays, and says so** (M-W03).
 * It used to vanish, and a supervisor looking for the switch had no way to
 * tell a server that was down from a switch that had been taken away.
 */
export default function QueueSwitchCard() {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery({ queryKey: KEY, queryFn: getQueue })
  const [result, setResult] = useState<QueueSwitchResult | null>(null)
  const [correcting, setCorrecting] = useState(false)

  const flip = useMutation({
    mutationFn: switchQueue,
    onSuccess: (r) => {
      setResult(r)
      queryClient.setQueryData(KEY, r.status)
    },
  })

  const mark = useMutation({
    mutationFn: markQueue,
    onSuccess: (s) => {
      setResult(null)
      setCorrecting(false)
      queryClient.setQueryData(KEY, s)
    },
  })

  const s = status.data

  if (status.isError && !s) {
    return (
      <section className="card card-body space-y-3" aria-label={t('queue.heading')}>
        <h3 className="font-semibold text-slate-100">{t('queue.heading')}</h3>
        <LoadError message={t('queue.failed')} onRetry={() => void status.refetch()} busy={status.isFetching} />
      </section>
    )
  }
  if (!s) return null

  const when = (iso: string) => new Date(iso).toLocaleString(i18n.language, { dateStyle: 'short', timeStyle: 'short' })
  const busy = flip.isPending || mark.isPending

  return (
    <section className="card card-body space-y-3" aria-label={t('queue.heading')}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <h3 className="font-semibold text-slate-100">{t('queue.heading')}</h3>
          <StateBadge status={s} />
        </div>

        {s.isOpen !== null && s.configured && (
          <button type="button" disabled={busy} onClick={() => flip.mutate(!s.isOpen)}
            className={s.isOpen ? 'btn-danger' : 'btn-primary'}>
            {flip.isPending ? t('queue.switching') : s.isOpen ? t('queue.close') : t('queue.open')}
          </button>
        )}
      </div>

      {!s.configured && <p className="text-sm text-slate-400">{t('queue.notConfigured')}</p>}

      {s.changedAt && (
        <p className="text-sm text-slate-400">
          {s.changedAutomatically
            ? t('queue.changedAutomatically', { when: when(s.changedAt) })
            : t('queue.changed', { when: when(s.changedAt), who: s.changedBy ?? '—' })}
        </p>
      )}

      <p className="text-sm text-slate-400">
        {s.autoOpenAt ? t('queue.autoOpen', { time: s.autoOpenAt }) : t('queue.autoOpenOff')}
      </p>
      {s.autoOpenProblem && <p role="alert" className="notice-warning">{s.autoOpenProblem}</p>}

      {result && !result.ok && (
        <p role="alert" className="notice-error">
          {result.code === 'pbx_failed' ? t('queue.errors.pbx_failed', { error: result.error }) : t(`queue.errors.${result.code}`)}
        </p>
      )}
      {(flip.isError || mark.isError) && <p role="alert" className="notice-error">{t('dashboard.failed')}</p>}

      {s.isOpen === null || correcting ? (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="text-slate-300">{t('queue.whichNow')}</span>
          <button type="button" disabled={busy} className="btn-ghost btn-sm" onClick={() => mark.mutate(true)}>
            {t('queue.itIsOpen')}
          </button>
          <button type="button" disabled={busy} className="btn-ghost btn-sm" onClick={() => mark.mutate(false)}>
            {t('queue.itIsClosed')}
          </button>
          {correcting && (
            <button type="button" className="btn-quiet btn-sm" onClick={() => setCorrecting(false)}>
              {t('flags.cancel')}
            </button>
          )}
        </div>
      ) : (
        <button type="button" className="btn-quiet btn-sm" onClick={() => setCorrecting(true)}>
          {t('queue.correct')}
        </button>
      )}
    </section>
  )
}

function StateBadge({ status: s }: { status: QueueStatus }) {
  const { t } = useTranslation()
  if (s.isOpen === null) return <span className="badge-muted">{t('queue.unknown')}</span>
  return s.isOpen
    ? <span className="badge-ok">{t('queue.isOpen')}</span>
    : <span className="badge-blocked">{t('queue.isClosed')}</span>
}
