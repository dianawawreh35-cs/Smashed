import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { communicationsForContact } from '../api/communications'
import type { Communication } from '../api/communications'

/**
 * A contact's history of calls, newest first (A-62).
 *
 * Every agent's calls, not only the viewer's: the panel exists to show the
 * customer's whole relationship with the restaurant, and half of it would be
 * misleading. Recordings are the part A-62 restricts, and they are not here yet.
 */
export default function ContactHistory({ contactId }: { contactId: string }) {
  const { t } = useTranslation()

  const { data: calls, isLoading } = useQuery({
    queryKey: ['communications', 'by-contact', contactId],
    queryFn: () => communicationsForContact(contactId),
  })

  if (isLoading) return <p className="text-slate-400">{t('app.loading')}</p>

  if (!calls || calls.length === 0) {
    return (
      <div className="card card-body text-center">
        <p className="text-slate-300">{t('history.empty')}</p>
        <p className="field-hint mt-1">{t('history.emptyHint')}</p>
      </div>
    )
  }

  return (
    <div className="card overflow-x-auto">
      <table className="table">
        <thead>
          <tr>
            <th>{t('history.when')}</th>
            <th>{t('history.status')}</th>
            <th>{t('history.duration')}</th>
            <th>{t('history.queue')}</th>
            <th>{t('history.agent')}</th>
          </tr>
        </thead>
        <tbody>
          {calls.map((call) => (
            <HistoryRow key={call.id} call={call} />
          ))}
        </tbody>
      </table>
    </div>
  )
}

function HistoryRow({ call }: { call: Communication }) {
  const { t, i18n } = useTranslation()

  return (
    <tr>
      <td className="whitespace-nowrap text-slate-300">
        {new Date(call.startedAt).toLocaleString(i18n.language)}
      </td>
      <td>
        <span className={badgeFor(call.status)}>
          {t(`history.statuses.${call.status}`, { defaultValue: call.status })}
        </span>
        {/* A-41: a call nobody has classified is worth seeing from here too —
            the supervisor chasing it is as likely to be on this page as in a
            report. */}
        {call.status === 'Answered' && !call.isClassified && (
          <span className="badge-muted ms-2">{t('history.unclassified')}</span>
        )}
      </td>
      {/* Blank rather than 0:00 for a call that was never answered: a zero
          duration reads as a call that connected and was silent. */}
      <td className="tabular text-slate-400">
        {call.durationSec === null ? '' : formatDuration(call.durationSec)}
      </td>
      <td className="text-slate-400">{call.queueName}</td>
      <td className="text-slate-400">{call.agentDisplayName}</td>
    </tr>
  )
}

const formatDuration = (seconds: number) =>
  `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`

/**
 * Colour by outcome, not by call. Green for answered, red for the ones the
 * customer did not get through on — so scanning the column shows how often this
 * customer has been let down.
 */
function badgeFor(status: string): string {
  switch (status) {
    case 'Answered':
      return 'badge-ok'
    case 'Missed':
    case 'Abandoned':
      return 'badge-blocked'
    case 'Blocked':
      return 'badge-vip'
    default:
      return 'badge-muted'
  }
}
