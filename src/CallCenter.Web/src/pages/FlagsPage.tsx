import { useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { flagHistory, flagNumber, listFlagged, setContactFlags } from '../api/flags'
import type { FlaggedContact } from '../api/flags'
import { errorCodeOf } from '../api/users'

/** The two flags. Mutually exclusive, so one choice here and two booleans on the wire. */
type Flag = 'vip' | 'blocked'

const asRequest = (flag: Flag, reason: string) => ({
  isVip: flag === 'vip',
  isBlocked: flag === 'blocked',
  reason: reason.trim() || null,
})

/**
 * VIP and blocked numbers (S-45). Supervisor-only, and the only place either
 * flag can be changed.
 *
 * Its own screen rather than two checkboxes on the contact form. Three reasons:
 * S-45 asks for a list of every flagged number, which a per-contact form cannot
 * give; a flag needs a reason and a supervisor has to give it deliberately, not
 * while correcting an address; and the contact form is what agents use, so the
 * flags must not be on it at all.
 *
 * VIP and Blocked are one choice, not two checkboxes, because they are mutually
 * exclusive — the server refuses both — and a checkbox pair that cannot both be
 * ticked is a radio group wearing a disguise.
 */
export default function FlagsPage() {
  const { t } = useTranslation()

  const { data: flagged, isLoading } = useQuery({
    queryKey: ['flags'],
    queryFn: listFlagged,
  })

  return (
    <div className="space-y-6">
      <div>
        <h2 className="page-title">{t('flags.heading')}</h2>
        <p className="page-subtitle">{t('flags.intro')}</p>
      </div>

      <FlagNumberForm />

      {isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : flagged && flagged.length > 0 ? (
        <FlaggedTable contacts={flagged} />
      ) : (
        <div className="card card-body text-center">
          <p className="text-slate-300">{t('flags.empty')}</p>
        </div>
      )}
    </div>
  )
}

/**
 * Flags one number. The number is the way in rather than a contact picker,
 * because the number is what a supervisor has in front of them — from the call
 * log, or from an agent who has just been shouted at. The server decides
 * whether it already belongs to somebody (A-13).
 */
function FlagNumberForm() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const [number, setNumber] = useState('')
  const [flag, setFlag] = useState<Flag>('blocked')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [applied, setApplied] = useState<FlaggedContact | null>(null)

  const apply = useMutation({
    mutationFn: () => flagNumber({ number: number.trim(), ...asRequest(flag, reason) }),
    onSuccess: (contact) => {
      void queryClient.invalidateQueries({ queryKey: ['flags'] })
      void queryClient.invalidateQueries({ queryKey: ['contacts'] })
      setApplied(contact)
      setNumber('')
      setReason('')
    },
    onError: (e) => setError(t(`flags.errors.${errorCodeOf(e)}`)),
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setApplied(null)
    apply.mutate()
  }

  return (
    <form onSubmit={submit} className="card card-body space-y-4">
      <h3 className="text-base font-semibold text-slate-100">{t('flags.addHeading')}</h3>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      {/* Which contact the flag landed on. A number that already belonged to a
          customer is flagged on them, and the supervisor should see whose name
          it was rather than assume a new record appeared. */}
      {applied && (
        <div role="status" className="notice-success">
          {t('flags.applied', {
            name: applied.name ?? t('flags.bareNumber'),
          })}
        </div>
      )}

      <div className="grid gap-3 sm:grid-cols-2 max-w-2xl">
        <label className="field">
          <span className="field-label">{t('flags.number')}</span>
          {/* The hint sits inside the label, as elsewhere in this app, so the
              input carries its own aria-label: without one the hint would be
              read out as part of the field's name. */}
          <input
            value={number}
            onChange={(e) => setNumber(e.target.value)}
            aria-label={t('flags.number')}
            className="input tabular"
            inputMode="tel"
          />
          <span className="field-hint">{t('flags.numberHint')}</span>
        </label>

        <label className="field">
          <span className="field-label">{t('flags.reason')}</span>
          <input
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            aria-label={t('flags.reason')}
            className="input"
          />
          <span className="field-hint">{t('flags.reasonHint')}</span>
        </label>
      </div>

      <FlagChoice value={flag} onChange={setFlag} />

      <button
        type="submit"
        disabled={apply.isPending || !number.trim() || !reason.trim()}
        className="btn-primary self-start"
      >
        {t('flags.apply')}
      </button>
    </form>
  )
}

/**
 * VIP or Blocked. There is no "neither" here — removing a flag is the Remove
 * button on the row, which needs no reason and no number typed again.
 */
function FlagChoice({ value, onChange }: { value: Flag; onChange: (flag: Flag) => void }) {
  const { t } = useTranslation()
  const choices: Flag[] = ['vip', 'blocked']

  return (
    <fieldset className="space-y-2">
      <legend className="field-label">{t('flags.flag')}</legend>
      <div className="flex flex-wrap gap-4">
        {choices.map((choice) => (
          <label key={choice} className="flex items-center gap-2 text-sm text-slate-300">
            <input
              type="radio"
              checked={value === choice}
              onChange={() => onChange(choice)}
              className="accent-brand-500"
            />
            {t(`flags.${choice}`)}
          </label>
        ))}
      </div>
    </fieldset>
  )
}

function FlaggedTable({ contacts }: { contacts: FlaggedContact[] }) {
  const { t } = useTranslation()
  const [showingHistoryFor, setShowingHistoryFor] = useState<string | null>(null)

  return (
    <div className="card overflow-x-auto">
      <table className="table">
        <thead>
          <tr>
            <th>{t('flags.name')}</th>
            <th>{t('flags.numbers')}</th>
            <th>{t('flags.flag')}</th>
            <th>{t('flags.reason')}</th>
            <th>{t('flags.changedBy')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {contacts.map((contact) => (
            <FlaggedRow
              key={contact.id}
              contact={contact}
              showingHistory={showingHistoryFor === contact.id}
              onToggleHistory={() =>
                setShowingHistoryFor(showingHistoryFor === contact.id ? null : contact.id)
              }
            />
          ))}
        </tbody>
      </table>
    </div>
  )
}

function FlaggedRow({
  contact,
  showingHistory,
  onToggleHistory,
}: {
  contact: FlaggedContact
  showingHistory: boolean
  onToggleHistory: () => void
}) {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)

  const remove = useMutation({
    mutationFn: () => setContactFlags(contact.id, { isVip: false, isBlocked: false, reason: null }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['flags'] })
      void queryClient.invalidateQueries({ queryKey: ['contacts'] })
    },
    onError: (e) => setError(t(`flags.errors.${errorCodeOf(e)}`)),
  })

  const when = (value: string | null) =>
    value ? new Date(value).toLocaleString(i18n.language) : ''

  return (
    <>
      <tr>
        <td className="font-medium text-slate-100">
          {contact.name ?? <span className="text-slate-500">{t('flags.bareNumber')}</span>}
        </td>
        <td className="tabular text-slate-400">{contact.numbers.join(' · ')}</td>
        <td>
          {contact.isVip && <span className="badge-vip">{t('flags.vip')}</span>}
          {contact.isBlocked && <span className="badge-blocked">{t('flags.blocked')}</span>}
        </td>
        <td className="text-slate-400">{contact.flagReason}</td>
        <td className="text-slate-400">
          {contact.changedByDisplayName}
          <span className="block text-xs text-slate-500">{when(contact.changedAt)}</span>
        </td>
        <td className="text-end whitespace-nowrap">
          <button type="button" onClick={onToggleHistory} className="btn-ghost btn-sm">
            {t('flags.history')}
          </button>
          <button
            type="button"
            onClick={() => {
              setError(null)
              remove.mutate()
            }}
            disabled={remove.isPending}
            className="btn-ghost btn-sm"
          >
            {t('flags.remove')}
          </button>
        </td>
      </tr>

      {error && (
        <tr>
          <td colSpan={6}>
            <div role="alert" className="notice-error">
              {error}
            </div>
          </td>
        </tr>
      )}

      {showingHistory && (
        <tr>
          <td colSpan={6}>
            <FlagHistory contactId={contact.id} />
          </td>
        </tr>
      )}
    </>
  )
}

/**
 * Every flag change on this contact (S-45). Read from the audit log, so it
 * includes the removals — which is the half the contact's own columns cannot
 * answer, and the half a dispute turns on.
 */
function FlagHistory({ contactId }: { contactId: string }) {
  const { t, i18n } = useTranslation()

  const { data: changes, isLoading } = useQuery({
    queryKey: ['flags', 'history', contactId],
    queryFn: () => flagHistory(contactId),
  })

  if (isLoading) return <p className="text-slate-400">{t('app.loading')}</p>
  if (!changes || changes.length === 0) return <p className="text-slate-400">{t('flags.noHistory')}</p>

  return (
    <ol className="space-y-1 text-sm">
      {changes.map((change, index) => (
        <li key={index} className="flex flex-wrap items-center gap-2">
          <span className="text-xs text-slate-500">
            {new Date(change.at).toLocaleString(i18n.language)}
          </span>
          {change.isVip ? (
            <span className="badge-vip">{t('flags.vip')}</span>
          ) : change.isBlocked ? (
            <span className="badge-blocked">{t('flags.blocked')}</span>
          ) : (
            <span className="badge-muted">{t('flags.removed')}</span>
          )}
          <span className="text-slate-300">{change.reason}</span>
          <span className="text-xs text-slate-500">{change.byDisplayName}</span>
        </li>
      ))}
    </ol>
  )
}
