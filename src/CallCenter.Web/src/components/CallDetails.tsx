import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { callClassification, callDetails, classificationHistory } from '../api/calls'
import type { CallClassification, CallRow, ClassificationChange } from '../api/calls'
import { getClassificationForm } from '../api/classifications'
import type { FormField } from '../api/classifications'
import { formatClock } from '../lib/recordingWav'
import ClassificationEditor from './ClassificationEditor'
import RecordingPlayer from './RecordingPlayer'

/**
 * One call opened in full (S-03): who, when, how it went, what it was, what it
 * was worth, who changed that and when, and the recording with its holds.
 *
 * **A supervisor classifies or changes it here** (S-04): Edit on a classified
 * call, Classify on an answered call nobody classified, opening the call
 * direction's form in place of the answers (`ClassificationEditor`). Saved
 * through the endpoint the Agent App uses; the change appears in the history
 * below at once.
 *
 * **It opens whole, at once, and then fills in without moving.** It first
 * showed "Loading…", then the facts, then the classification, then the
 * player, each one pushing the panel taller, and the classification waited for
 * the facts before it was even asked for. Reported as glitchy on 24 Sep. Now
 * the list row it was opened from supplies everything it already knows, so
 * the panel draws at once; the rest is fetched in parallel; and what is still
 * coming holds its space.
 *
 * **The page does not scroll when it opens.** It used to scroll to show the
 * whole panel when opened near the bottom of the window, and that movement was
 * the part that still felt wrong (24 Sep). It opens where it is, and the
 * supervisor scrolls if they want to see more.
 */
export default function CallDetails({ row, onClose }: { row: CallRow; onClose: () => void }) {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const id = row.id

  const details = useQuery({ queryKey: ['calls', 'details', id], queryFn: () => callDetails(id) })
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState(false)
  // Classified here just now: the list row that says otherwise is refreshing.
  const [classifiedHere, setClassifiedHere] = useState(false)
  const classified = row.isClassified || classifiedHere

  const classification = useQuery({
    queryKey: ['calls', 'classification', id],
    queryFn: () => callClassification(id),
    enabled: classified,
  })
  const history = useQuery({
    queryKey: ['calls', 'history', id],
    queryFn: () => classificationHistory(id),
    enabled: classified,
  })
  // For the questions' labels. The current form's, for the call's own
  // direction: an answer to a question since removed shows under its key
  // rather than not at all.
  const direction = row.direction === 'Out' ? 'Out' : 'In'
  const form = useQuery({
    queryKey: ['classification', 'form', direction],
    queryFn: () => getClassificationForm(direction),
    enabled: classified || editing,
  })

  function onSaved() {
    setEditing(false)
    setClassifiedHere(true)
    // Everything that shows this call's classification: the answers, their
    // history, the search rows (type, order value) and a contact's history.
    void queryClient.invalidateQueries({ queryKey: ['calls'] })
    void queryClient.invalidateQueries({ queryKey: ['communications', 'by-contact'] })
  }

  const summary = row
  // What the row does not carry, as a placeholder of the same size until it arrives.
  const pending = details.isLoading ? '…' : null
  const extra = details.data
  // The row's notes are the call's own note whenever the call is not classified.
  const callNotes = extra ? extra.callNotes : classified ? null : row.notes
  const when = (at: string | null | undefined) => (at ? new Date(at).toLocaleString(i18n.language) : '')

  return (
    <section className="card animate-fade-in" aria-label={t('calls.details.heading')}>
      <div className="card-header">
        <div>
          <h2 className="font-semibold text-slate-100">
            {summary.contactName ?? summary.remoteNumberRaw ?? t('calls.unknownCaller')}
          </h2>
          <p className="text-sm text-slate-400">
            <span dir="ltr">{summary.remoteNumberRaw}</span> · {when(summary.startedAt)}
          </p>
        </div>
        <button type="button" className="btn-ghost btn-sm" onClick={onClose}>
          {t('calls.details.close')}
        </button>
      </div>

      <div className="card-body space-y-6">
        {details.isError && <p className="notice-error">{t('calls.details.failed')}</p>}
        <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm md:grid-cols-4">
          <Fact label={t('calls.columns.agent')} value={summary.agentDisplayName} />
          <Fact label={t('calls.details.extension')} value={pending ?? extra?.extension} ltr />
          <Fact label={t('calls.columns.direction')} value={t(`calls.directions.${summary.direction}`)} />
          <Fact
            label={t('calls.columns.status')}
            value={t(`history.statuses.${summary.status}`, { defaultValue: summary.status })}
          />
          <Fact label={t('calls.details.queue')} value={pending ?? extra?.queueName} />
          <Fact label={t('calls.details.answeredAt')} value={pending ?? when(extra?.answeredAt)} />
          <Fact label={t('calls.details.endedAt')} value={pending ?? when(extra?.endedAt)} />
          <Fact
            label={t('calls.columns.duration')}
            value={summary.durationSec === null ? null : formatClock(summary.durationSec)}
            ltr
          />
        </dl>

        <div>
          <h3 className="mb-2 text-sm font-semibold text-slate-200">{t('calls.details.recording')}</h3>
          <RecordingPlayer
            communicationId={summary.id}
            hasRecording={summary.hasRecording}
            expired={summary.recordingExpired}
          />
        </div>

        {callNotes && (
          <div>
            <h3 className="mb-1 text-sm font-semibold text-slate-200">{t('calls.details.callNotes')}</h3>
            <p className="whitespace-pre-wrap text-sm text-slate-300">{callNotes}</p>
          </div>
        )}

        {/* Only an answered call is classified (A-40): nobody spoke on the rest. */}
        {summary.status === 'Answered' && (
          <div>
            <div className="mb-2 flex items-center justify-between gap-3">
              <h3 className="text-sm font-semibold text-slate-200">{t('calls.details.classification')}</h3>
              {!editing && (
                <button type="button" className="btn-ghost btn-sm" onClick={() => setEditing(true)}>
                  {classified ? t('calls.edit.edit') : t('calls.edit.classify')}
                </button>
              )}
            </div>

            {editing ? (
              form.data && (!classified || classification.data) ? (
                <ClassificationEditor
                  callId={id}
                  form={form.data}
                  existing={classified ? classification.data ?? null : null}
                  onSaved={onSaved}
                  onCancel={() => setEditing(false)}
                />
              ) : (
                <div className="h-28 rounded-md bg-ink-800/40" aria-hidden="true" />
              )
            ) : classified ? (
              classification.data ? (
                <Classification
                  value={classification.data}
                  fields={form.data?.definition.fields ?? []}
                  arabic={arabic}
                />
              ) : (
                // Its space, held while it comes, so the panel does not jump.
                <div className="h-28 animate-pulse rounded-md bg-ink-800/60" aria-hidden="true" />
              )
            ) : (
              <p className="badge-muted">{t('history.unclassified')}</p>
            )}
          </div>
        )}

        {history.data && history.data.length > 0 && <History changes={history.data} />}
      </div>
    </section>
  )
}

function Fact({ label, value, ltr }: { label: string; value: string | null | undefined; ltr?: boolean }) {
  return (
    <div>
      <dt className="field-label">{label}</dt>
      <dd className="text-slate-200" dir={ltr ? 'ltr' : undefined}>
        {value || '–'}
      </dd>
    </div>
  )
}

function Classification({
  value,
  fields,
  arabic,
}: {
  value: CallClassification
  fields: FormField[]
  arabic: boolean
}) {
  const { t, i18n } = useTranslation()
  const labelOf = (key: string) => {
    const field = fields.find((f) => f.key === key)
    return (arabic ? field?.label?.ar : field?.label?.en) ?? field?.label?.en ?? field?.label?.ar ?? key
  }
  const answers = Object.entries(value.customValues ?? {}).filter(([, v]) => v !== null && v !== '')

  return (
    <div>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm md:grid-cols-4">
        <Fact label={t('calls.columns.type')} value={arabic ? value.typeLabelAr : value.typeLabelEn} />
        <Fact label={t('calls.columns.branch')} value={value.branchName} />
        <Fact
          label={t('calls.columns.orderValue')}
          value={value.orderValue === null ? null : value.orderValue.toLocaleString(i18n.language)}
          ltr
        />
        <Fact label={t('calls.details.followUp')} value={value.followUp ? t('common.yes') : t('common.no')} />
        {value.resolved !== null && (
          <Fact label={t('calls.details.resolved')} value={value.resolved ? t('common.yes') : t('common.no')} />
        )}
        {answers.map(([key, answer]) => (
          <Fact key={key} label={labelOf(key)} value={displayAnswer(answer, t)} />
        ))}
      </dl>
      {value.notes && <p className="mt-3 whitespace-pre-wrap text-sm text-slate-300">{value.notes}</p>}
      <p className="field-hint mt-3">
        {t('calls.details.classifiedBy', {
          name: value.classifiedByName,
          at: new Date(value.classifiedAt).toLocaleString(i18n.language),
        })}
      </p>
    </div>
  )
}

function displayAnswer(answer: unknown, t: (key: string) => string): string {
  if (typeof answer === 'boolean') return answer ? t('common.yes') : t('common.no')
  if (Array.isArray(answer)) return answer.join(', ')
  return String(answer)
}

/** Who changed the classification and when (A-43). The first entry is the first classification. */
function History({ changes }: { changes: ClassificationChange[] }) {
  const { t, i18n } = useTranslation()

  return (
    <div>
      <h3 className="mb-2 text-sm font-semibold text-slate-200">{t('calls.details.history')}</h3>
      <ol className="space-y-1 text-sm text-slate-300">
        {changes.map((change) => (
          <li key={change.changedAt}>
            {t(change.before ? 'calls.details.changed' : 'calls.details.firstClassified', {
              name: change.changedByName,
              at: new Date(change.changedAt).toLocaleString(i18n.language),
            })}
          </li>
        ))}
      </ol>
    </div>
  )
}
