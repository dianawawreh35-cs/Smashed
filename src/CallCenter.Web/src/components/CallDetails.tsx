import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { callClassification, callDetails, classificationHistory } from '../api/calls'
import type { CallClassification, ClassificationChange } from '../api/calls'
import { getClassificationForm } from '../api/classifications'
import type { FormField } from '../api/classifications'
import { formatClock } from '../lib/recordingWav'
import RecordingPlayer from './RecordingPlayer'

/**
 * One call opened in full (S-03): who, when, how it went, what it was, what it
 * was worth, who changed that and when, and the recording with its holds.
 *
 * Reading only. Editing a classification from here (S-04's second half) needs
 * the classification form in the browser, which does not exist yet.
 */
export default function CallDetails({ id, onClose }: { id: string; onClose: () => void }) {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')

  const details = useQuery({ queryKey: ['calls', 'details', id], queryFn: () => callDetails(id) })
  const classified = details.data?.summary.isClassified ?? false

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
  // For the questions' labels. The current form's: an answer to a question
  // since removed shows under its key rather than not at all.
  const form = useQuery({ queryKey: ['classification', 'form'], queryFn: getClassificationForm, enabled: classified })

  if (details.isLoading) return <div className="card card-body text-slate-400">{t('app.loading')}</div>
  if (!details.data) return <div className="card card-body notice-error">{t('calls.details.failed')}</div>

  const { summary, callNotes } = details.data
  const when = (at: string | null) => (at ? new Date(at).toLocaleString(i18n.language) : '')

  return (
    <section className="card" aria-label={t('calls.details.heading')}>
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
        <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm md:grid-cols-4">
          <Fact label={t('calls.columns.agent')} value={summary.agentDisplayName} />
          <Fact label={t('calls.details.extension')} value={details.data.extension} ltr />
          <Fact label={t('calls.columns.direction')} value={t(`calls.directions.${summary.direction}`)} />
          <Fact
            label={t('calls.columns.status')}
            value={t(`history.statuses.${summary.status}`, { defaultValue: summary.status })}
          />
          <Fact label={t('calls.details.queue')} value={details.data.queueName} />
          <Fact label={t('calls.details.answeredAt')} value={when(details.data.answeredAt)} />
          <Fact label={t('calls.details.endedAt')} value={when(details.data.endedAt)} />
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

        {classified ? (
          classification.data && (
            <Classification
              value={classification.data}
              fields={form.data?.definition.fields ?? []}
              arabic={arabic}
            />
          )
        ) : (
          summary.status === 'Answered' && <p className="badge-muted">{t('history.unclassified')}</p>
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
      <h3 className="mb-2 text-sm font-semibold text-slate-200">{t('calls.details.classification')}</h3>
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
