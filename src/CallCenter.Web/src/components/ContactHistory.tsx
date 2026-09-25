import { Fragment, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import type { CallRow } from '../api/calls'
import { communicationsForContact } from '../api/communications'
import type { Communication } from '../api/communications'
import CallDetails from './CallDetails'
import { noSelectOnDoubleClick } from '../lib/rows'

/**
 * A contact's history, newest first (A-62): calls and messages together, each
 * message with its channel (A-72).
 *
 * Every agent's, not only the viewer's: the panel exists to show the
 * customer's whole relationship with the restaurant, and half of it would be
 * misleading.
 *
 * **Any call or message opens under its own row**, by double-click or its Open
 * button, in the same panel as the call search: facts, classification, history,
 * and for a call the recording with its holds marked. It opens where it is, without scrolling the
 * page, as every list in the app now does (24 Sep). This is the supervisor app,
 * so every agent's recording plays here; A-62's restriction on recordings is
 * the Agent App's.
 */
export default function ContactHistory({ contactId }: { contactId: string }) {
  const { t } = useTranslation()
  const [openId, setOpenId] = useState<string | null>(null)

  const { data: calls, isLoading } = useQuery({
    queryKey: ['communications', 'by-contact', contactId],
    queryFn: () => communicationsForContact(contactId),
  })

  // Its space held while it comes, so the editor above it does not grow in a
  // second step when the history arrives.
  if (isLoading) return <p className="min-h-[8rem] text-slate-400">{t('app.loading')}</p>

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
            <th>{t('history.channel')}</th>
            <th>{t('history.duration')}</th>
            <th>{t('history.queue')}</th>
            <th>{t('history.agent')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {calls.map((call) => (
            <Fragment key={call.id}>
              <HistoryRow
                call={call}
                open={call.id === openId}
                onToggle={() => setOpenId(call.id === openId ? null : call.id)}
              />
              {call.id === openId && (
                <tr>
                  <td colSpan={7} className="bg-ink-950/60 p-3">
                    {/* w-0 min-w-full: as wide as the table and never wider,
                        so opening a call cannot make every column jump. */}
                    <div className="w-0 min-w-full">
                      <CallDetails row={asCallRow(call)} onClose={() => setOpenId(null)} />
                    </div>
                  </td>
                </tr>
              )}
            </Fragment>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function HistoryRow({ call, open, onToggle }: { call: Communication; open: boolean; onToggle: () => void }) {
  const { t, i18n } = useTranslation()
  // A message: a communications row with kind App (A-70). Its status is
  // always Logged, which says nothing a reader wants; "Message" does.
  const isMessage = call.kind === 'App'

  return (
    // Double-click is a shortcut, never the only way in: the Open button does
    // the same, and is what a keyboard reaches.
    <tr
      onDoubleClick={onToggle}
      onMouseDown={noSelectOnDoubleClick}
      className={`cursor-pointer ${open ? 'bg-ink-800/40' : ''}`}
    >
      <td className="whitespace-nowrap text-slate-300">
        {new Date(call.startedAt).toLocaleString(i18n.language)}
      </td>
      <td>
        <span className={isMessage ? 'badge-muted' : badgeFor(call.status)}>
          {isMessage
            ? t('applications.message')
            : t(`history.statuses.${call.status}`, { defaultValue: call.status })}
        </span>
        {/* A-41: a call nobody has classified is worth seeing from here too —
            the supervisor chasing it is as likely to be on this page as in a
            report. A message recorded without its form is the same debt (A-70). */}
        {(call.status === 'Answered' || isMessage) && !call.isClassified && (
          <span className="badge-muted ms-2">{t('history.unclassified')}</span>
        )}
        {/* A missed or rejected call is never classified; the agent's note
            on why is what it carries instead. */}
        {call.notes && <div className="mt-1 text-xs text-slate-400">{call.notes}</div>}
      </td>
      <td className="text-slate-400">{call.channelName}</td>
      {/* Blank rather than 0:00 for a call that was never answered: a zero
          duration reads as a call that connected and was silent. */}
      <td className="tabular text-slate-400">
        {call.durationSec === null ? '' : formatDuration(call.durationSec)}
      </td>
      <td className="text-slate-400">{call.queueName}</td>
      <td className="text-slate-400">{call.agentDisplayName}</td>
      {/* The double-click stops here, so a quick double press of the button
          does not open the call and shut it again. */}
      <td className="text-end" onDoubleClick={(e) => e.stopPropagation()}>
        <button type="button" className="btn-ghost btn-sm" aria-expanded={open} onClick={onToggle}>
          {open ? t('calls.details.close') : t('calls.open')}
        </button>
      </td>
    </tr>
  )
}

/**
 * What the history already knows about a call or message, in the shape the
 * panel draws from at once. Branch, type and order value are left for the panel to
 * fetch with the classification, which it does whenever the call is classified.
 */
function asCallRow(call: Communication): CallRow {
  return {
    id: call.id,
    kind: call.kind,
    startedAt: call.startedAt,
    direction: call.direction,
    status: call.status,
    agentId: null,
    agentDisplayName: call.agentDisplayName,
    contactId: call.contactId,
    contactName: call.contactName,
    remoteNumberRaw: call.remoteNumberRaw,
    branchId: null,
    branchName: null,
    typeName: null,
    typeLabelAr: null,
    typeLabelEn: null,
    orderValue: null,
    durationSec: call.durationSec,
    notes: call.notes,
    isClassified: call.isClassified,
    hasRecording: call.hasRecording ?? false,
    recordingExpired: call.recordingExpired ?? false,
    channelId: null,
    channelName: call.channelName ?? null,
  }
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
