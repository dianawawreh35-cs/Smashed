import { useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { flagHistory, flagNumber, setContactFlags } from '../api/flags'
import { errorCodeOf } from '../api/users'

/** VIP, Blocked, or neither. Mutually exclusive, so one choice and two booleans on the wire. */
type Choice = 'vip' | 'blocked' | 'none'

/**
 * What is being flagged: a contact already on file, or a bare number that
 * matched nothing (S-45). The second is why the dialog is not simply part of
 * the contact form — a nuisance caller who is not a customer still has to be
 * blockable without inventing a contact for them first.
 */
export type FlagTarget =
  | { kind: 'contact'; id: string; name: string | null; isVip: boolean; isBlocked: boolean; flagReason: string | null }
  | { kind: 'number'; number: string }

/**
 * Sets, changes or removes a contact's flags (S-45). Supervisor-only — the web
 * app refuses agent accounts at sign-in, and every write behind this is
 * `SupervisorOnly` on the server regardless.
 *
 * It asks for the reason rather than taking one typed in advance, because the
 * reason is the point: a block nobody can account for is one nobody later
 * dares remove. Removing a flag asks for nothing — the removal is logged with
 * who and when, which is what a later reader wants.
 */
export default function FlagDialog({
  target,
  onClose,
}: {
  target: FlagTarget
  onClose: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const current: Choice =
    target.kind === 'contact' && target.isVip
      ? 'vip'
      : target.kind === 'contact' && target.isBlocked
        ? 'blocked'
        : 'blocked'

  const [choice, setChoice] = useState<Choice>(current)
  const [reason, setReason] = useState(
    target.kind === 'contact' ? (target.flagReason ?? '') : '',
  )
  const [error, setError] = useState<string | null>(null)

  const isFlagged = target.kind === 'contact' && (target.isVip || target.isBlocked)

  const save = useMutation({
    mutationFn: () => {
      const flags = {
        isVip: choice === 'vip',
        isBlocked: choice === 'blocked',
        reason: choice === 'none' ? null : reason.trim() || null,
      }

      return target.kind === 'contact'
        ? setContactFlags(target.id, flags)
        : flagNumber({ number: target.number, ...flags })
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['contacts'] })
      onClose()
    },
    onError: (e) => setError(t(`flags.errors.${errorCodeOf(e)}`)),
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    save.mutate()
  }

  // A reason is required to set a flag and meaningless when removing one.
  const canSave = choice === 'none' || reason.trim().length > 0

  const heading =
    target.kind === 'contact'
      ? t('flags.heading', { name: target.name ?? t('flags.bareNumber') })
      : t('flags.headingNumber', { number: target.number })

  return (
    <form onSubmit={submit} className="card card-body space-y-4">
      <h3 className="text-base font-semibold text-slate-100">{heading}</h3>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      <fieldset className="space-y-2">
        <legend className="field-label">{t('flags.flag')}</legend>
        <div className="flex flex-wrap gap-4">
          {(['vip', 'blocked', ...(isFlagged ? (['none'] as const) : [])] as Choice[]).map((option) => (
            <label key={option} className="flex items-center gap-2 text-sm text-slate-300">
              <input
                type="radio"
                checked={choice === option}
                onChange={() => setChoice(option)}
                className="accent-brand-500"
              />
              {t(`flags.${option}`)}
            </label>
          ))}
        </div>
      </fieldset>

      {/* Hidden when removing: there is nothing to give a reason for. */}
      {choice !== 'none' && (
        <label className="field max-w-md">
          <span className="field-label">{t('flags.reason')}</span>
          <input
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            aria-label={t('flags.reason')}
            className="input"
            autoFocus
          />
          <span className="field-hint">{t('flags.reasonHint')}</span>
        </label>
      )}

      <div className="flex gap-2">
        <button type="submit" disabled={save.isPending || !canSave} className="btn-primary">
          {choice === 'none' ? t('flags.remove') : t('flags.apply')}
        </button>
        <button type="button" onClick={onClose} className="btn-ghost">
          {t('flags.cancel')}
        </button>
      </div>

      {target.kind === 'contact' && <FlagHistory contactId={target.id} />}
    </form>
  )
}

/**
 * Every flag change on this contact (S-45). Read from the audit log, so it
 * includes the removals — the half the contact's own columns cannot answer,
 * and the half a dispute turns on.
 */
function FlagHistory({ contactId }: { contactId: string }) {
  const { t, i18n } = useTranslation()

  const { data: changes, isLoading } = useQuery({
    queryKey: ['contacts', 'flag-history', contactId],
    queryFn: () => flagHistory(contactId),
  })

  if (isLoading || !changes || changes.length === 0) return null

  return (
    <div className="border-t border-ink-700 pt-3">
      <p className="field-label mb-2">{t('flags.history')}</p>
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
    </div>
  )
}
