import { useTranslation } from 'react-i18next'
import type { AgentPhone } from '../api/pbxAgents'
import { elapsed } from '../lib/agentPhones'
import type { Listening } from '../lib/agentPhones'
import { useClock } from '../lib/clock'

/**
 * What each agent's phone is doing (S-61) and the listen-in bar (S-62). The
 * data and the listening live in lib/agentPhones. The timers share one clock
 * (lib/clock), and only a badge showing a call, or a listen-in, ticks.
 */

/** Offline, free, ringing, or in a call with how long. */
export function PhoneBadge({ phone }: { phone: AgentPhone | undefined }) {
  const { t } = useTranslation()
  const now = useClock(phone?.state === 'InCall' && !!phone.since)

  if (!phone) return <span className="text-slate-400">—</span>

  switch (phone.state) {
    case 'InCall':
      return (
        <span className="badge-call tabular">
          {t('phones.states.InCall')}
          {phone.since && <> · <span dir="ltr">{elapsed(Date.parse(phone.since), now)}</span></>}
        </span>
      )
    case 'Ringing':
      return <span className="badge-vip">{t('phones.states.Ringing')}</span>
    case 'Free':
      return <span className="badge-ok">{t('phones.states.Free')}</span>
    case 'Offline':
      return <span className="badge-muted">{t('phones.states.Offline')}</span>
    default:
      return <span className="badge-muted">{t('phones.states.Unknown')}</span>
  }
}

/** Shown while listening in, with the one button that matters: Stop. */
export function ListenBar({ listening, onStop }: { listening: Listening | null; onStop: () => void }) {
  const { t } = useTranslation()
  const now = useClock(listening?.startedAt !== undefined && listening.status === 'listening')

  if (!listening) return null

  if (listening.status === 'failed') {
    return (
      <div role="alert" className="notice-error flex items-center justify-between gap-4">
        <span>
          {t(`phones.errors.${listening.code}`, {
            name: listening.name,
            error: listening.error ?? '',
            defaultValue: t('phones.errors.server_error'),
          })}
        </span>
        <button type="button" className="btn-ghost btn-sm" onClick={onStop}>
          {t('phones.close')}
        </button>
      </div>
    )
  }

  if (listening.status === 'ended') {
    return (
      <div role="status" className="card card-body flex items-center justify-between gap-4">
        <span>{t('phones.ended', { name: listening.name })}</span>
        <button type="button" className="btn-ghost btn-sm" onClick={onStop}>
          {t('phones.close')}
        </button>
      </div>
    )
  }

  return (
    <div role="status" className="card card-body flex items-center justify-between gap-4 border-brand-500/60">
      <span className="font-medium text-slate-100">
        {listening.status === 'connecting'
          ? t('phones.connecting', { name: listening.name })
          : t('phones.listening', { name: listening.name })}
        {listening.startedAt !== undefined && (
          <span className="tabular ms-2 text-slate-400" dir="ltr">{elapsed(listening.startedAt, now)}</span>
        )}
      </span>
      <button type="button" className="btn-danger" onClick={onStop}>
        {t('phones.stop')}
      </button>
    </div>
  )
}
