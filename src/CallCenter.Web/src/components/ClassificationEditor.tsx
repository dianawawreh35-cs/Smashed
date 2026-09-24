import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { saveClassification } from '../api/calls'
import type { CallClassification, SaveClassification } from '../api/calls'
import type { ClassificationForm, FormField } from '../api/classifications'
import { ApiError } from '../api/client'

/** The answer keys that have columns of their own, as the Agent App saves them. */
const BUILT_IN = new Set(['order_value', 'notes', 'follow_up'])

/** Every answer as the form holds it: text in a box is a string until it is saved. */
type Answers = Record<string, string | boolean>

/**
 * A supervisor's classification of one call, new or changed (S-04).
 *
 * The questions are the call's direction's current form (S-40), drawn the way
 * the Agent App draws them: the type, the branch, and whatever the supervisor
 * added, each shown only for the types it is asked for. It saves exactly what
 * the Agent App saves, through the same endpoint. The server allows a
 * supervisor to change any call's at any time and records the change (A-43),
 * so the call's history shows it at once.
 *
 * Two things the Agent App's form does not need to care about:
 *
 * - **Answers to questions the form no longer asks are kept.** The server
 *   replaces a call's custom answers on every save, so editing an old call
 *   under today's questions would otherwise delete the answers to yesterday's.
 * - **Resolved**, for a complaint. The Agent App never sets it; a supervisor
 *   following a complaint up is who does. On any other type the existing value
 *   is sent back untouched, and the server clears it itself.
 */
export default function ClassificationEditor({
  callId,
  form,
  existing,
  onSaved,
  onCancel,
}: {
  callId: string
  form: ClassificationForm
  existing: CallClassification | null
  onSaved: () => void
  onCancel: () => void
}) {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const fields = form.definition.fields

  const [answers, setAnswers] = useState<Answers>(() => initialAnswers(fields, existing))
  const [resolved, setResolved] = useState<boolean>(existing?.resolved ?? false)
  const [error, setError] = useState<string | null>(null)

  const typeField = fields.find((f) => f.kind === 'type')
  const typeKey = typeField?.key ?? 'type'
  const selectedType = form.types.find((ty) => ty.id === answers[typeKey])
  const isComplaint = selectedType?.name.toLowerCase() === 'complaint'

  // A type the supervisor has since hidden cannot be put on a call (the server
  // refuses it), but a call that has it still shows it, so it can be read.
  const offeredTypes = useMemo(
    () => form.types.filter((ty) => ty.isActive || ty.id === existing?.typeId),
    [form.types, existing?.typeId],
  )

  const applies = (field: FormField) =>
    !field.showWhenType || field.showWhenType.length === 0
    || (selectedType !== undefined && field.showWhenType.includes(selectedType.name))

  const labelOf = (field: FormField) =>
    (arabic ? field.label?.ar : field.label?.en) ?? field.label?.en ?? field.label?.ar ?? field.key

  // What stops Save, said beside it rather than left as a dead button.
  const missing = fields
    .filter((f) => applies(f) && f.required && f.kind !== 'checkbox')
    .filter((f) => answers[f.key] === undefined || answers[f.key] === '')
    .map(labelOf)
  const unreadable = fields
    .filter((f) => applies(f) && f.kind === 'number')
    .filter((f) => typeof answers[f.key] === 'string' && answers[f.key] !== '' && Number.isNaN(Number(answers[f.key])))
    .map(labelOf)
  const inactiveType = selectedType !== undefined && !selectedType.isActive

  const save = useMutation({
    mutationFn: () => saveClassification(callId, toRequest(form, answers, applies, resolved, isComplaint, existing)),
    onSuccess: onSaved,
    onError: (e) => setError(t(`calls.edit.errors.${errorCode(e)}`, { defaultValue: t('calls.edit.errors.save_failed') })),
  })

  const canSave = !save.isPending && missing.length === 0 && unreadable.length === 0 && !inactiveType && selectedType !== undefined

  function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    if (canSave) save.mutate()
  }

  const set = (key: string, value: string | boolean) => setAnswers((a) => ({ ...a, [key]: value }))

  return (
    <form onSubmit={submit} className="space-y-4" aria-label={t('calls.edit.heading')}>
      <div className="grid gap-4 md:grid-cols-2">
        {fields.filter(applies).map((field) => (
          <FieldInput
            key={field.key}
            field={field}
            label={labelOf(field)}
            value={answers[field.key]}
            onChange={(v) => set(field.key, v)}
            types={offeredTypes}
            branches={form.branches}
            arabic={arabic}
          />
        ))}

        {isComplaint && (
          <label className="flex items-center gap-2 text-sm text-slate-200">
            <input type="checkbox" checked={resolved} onChange={(e) => setResolved(e.target.checked)} />
            {t('calls.details.resolved')}
          </label>
        )}
      </div>

      {inactiveType && <p className="notice-warning">{t('calls.edit.inactiveType')}</p>}
      {missing.length > 0 && (
        <p className="text-xs text-amber-300">{t('calls.edit.missing', { fields: missing.join(arabic ? '، ' : ', ') })}</p>
      )}
      {unreadable.length > 0 && (
        <p className="text-xs text-amber-300">{t('calls.edit.notANumber', { fields: unreadable.join(arabic ? '، ' : ', ') })}</p>
      )}
      {error && <p role="alert" className="notice-error">{error}</p>}

      <div className="flex gap-2">
        <button type="submit" className="btn-primary" disabled={!canSave}>
          {save.isPending ? t('calls.edit.saving') : t('calls.edit.save')}
        </button>
        <button type="button" className="btn-ghost" onClick={onCancel}>
          {t('calls.edit.cancel')}
        </button>
      </div>
    </form>
  )
}

function FieldInput({
  field, label, value, onChange, types, branches, arabic,
}: {
  field: FormField
  label: string
  value: string | boolean | undefined
  onChange: (value: string | boolean) => void
  types: ClassificationForm['types']
  branches: ClassificationForm['branches']
  arabic: boolean
}) {
  const { t } = useTranslation()
  const text = typeof value === 'string' ? value : ''
  const required = field.required ? ' *' : ''

  switch (field.kind) {
    case 'checkbox':
      return (
        <label className="flex items-center gap-2 text-sm text-slate-200">
          <input type="checkbox" checked={value === true} onChange={(e) => onChange(e.target.checked)} />
          {label}
        </label>
      )
    case 'type':
    case 'branch':
    case 'select': {
      const options = field.kind === 'type'
        ? types.map((ty) => ({ value: ty.id, label: arabic ? ty.labelAr : ty.labelEn }))
        : field.kind === 'branch'
          ? branches.map((b) => ({ value: b.id, label: b.name }))
          : (field.options ?? []).map((o) => ({
              value: o.value,
              label: (arabic ? o.label?.ar : o.label?.en) ?? o.label?.en ?? o.label?.ar ?? o.value,
            }))
      return (
        <label className="field">
          <span className="field-label">{label}{required}</span>
          <select className="input" value={text} onChange={(e) => onChange(e.target.value)}>
            <option value="">{t('calls.edit.choose')}</option>
            {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        </label>
      )
    }
    case 'textarea':
      return (
        <label className="field md:col-span-2">
          <span className="field-label">{label}{required}</span>
          <textarea className="input min-h-[5rem]" value={text} onChange={(e) => onChange(e.target.value)} />
        </label>
      )
    case 'number':
      return (
        <label className="field">
          <span className="field-label">{label}{required}</span>
          <input className="input" inputMode="decimal" dir="ltr" value={text} onChange={(e) => onChange(e.target.value)} />
        </label>
      )
    default:
      return (
        <label className="field">
          <span className="field-label">{label}{required}</span>
          <input className="input" value={text} onChange={(e) => onChange(e.target.value)} />
        </label>
      )
  }
}

/** The saved classification, laid out as the form's answers. */
function initialAnswers(fields: FormField[], existing: CallClassification | null): Answers {
  const answers: Answers = {}
  if (!existing) return answers

  for (const field of fields) {
    if (field.kind === 'type') answers[field.key] = existing.typeId
    else if (field.kind === 'branch') answers[field.key] = existing.branchId ?? ''
    else if (field.key === 'order_value') answers[field.key] = existing.orderValue?.toString() ?? ''
    else if (field.key === 'notes') answers[field.key] = existing.notes ?? ''
    else if (field.key === 'follow_up') answers[field.key] = existing.followUp
    else {
      const saved = existing.customValues?.[field.key]
      if (typeof saved === 'boolean') answers[field.key] = saved
      else if (saved !== undefined && saved !== null) answers[field.key] = String(saved)
    }
  }
  return answers
}

/** The answers as the server takes them: the same shape the Agent App sends. */
function toRequest(
  form: ClassificationForm,
  answers: Answers,
  applies: (field: FormField) => boolean,
  resolved: boolean,
  isComplaint: boolean,
  existing: CallClassification | null,
): SaveClassification {
  const fields = form.definition.fields
  // An answer counts only while its question is asked, as in the Agent App
  // (FieldValue: key and AppliesNow). Changing an order to a complaint must not
  // carry its order value into the complaint, where the reports would count it.
  const valueOf = (key: string) => {
    const field = fields.find((f) => f.key === key)
    return field && applies(field) ? answers[key] : undefined
  }
  const typeKey = fields.find((f) => f.kind === 'type')?.key ?? 'type'
  const branchKey = fields.find((f) => f.kind === 'branch')?.key

  const text = (key: string) => {
    const v = valueOf(key)
    return typeof v === 'string' && v.trim() ? v.trim() : null
  }

  // Answers to questions today's form no longer asks, kept rather than lost.
  const asked = new Set(fields.map((f) => f.key))
  const custom: Record<string, unknown> = Object.fromEntries(
    Object.entries(existing?.customValues ?? {}).filter(([key]) => !asked.has(key)),
  )

  for (const field of fields) {
    if (field.kind === 'type' || field.kind === 'branch' || BUILT_IN.has(field.key) || !applies(field)) continue
    const v = valueOf(field.key)
    if (field.kind === 'checkbox') custom[field.key] = v === true
    else if (field.kind === 'number') {
      if (typeof v === 'string' && v.trim()) custom[field.key] = Number(v)
    } else if (typeof v === 'string' && v.trim()) custom[field.key] = v.trim()
  }

  const orderText = text('order_value')

  return {
    typeId: String(valueOf(typeKey)),
    branchId: branchKey ? text(branchKey) : existing?.branchId ?? null,
    orderValue: orderText === null ? null : Number(orderText),
    notes: text('notes'),
    followUp: valueOf('follow_up') === true,
    resolved: isComplaint ? resolved : existing?.resolved ?? null,
    formVersion: form.version,
    customValues: custom,
  }
}

function errorCode(error: unknown): string {
  return error instanceof ApiError ? error.code ?? 'save_failed' : 'save_failed'
}
