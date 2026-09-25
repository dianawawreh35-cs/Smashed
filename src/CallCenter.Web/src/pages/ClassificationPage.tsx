import { Fragment, useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  createClassificationType,
  deleteClassificationType,
  getClassificationForm,
  publishClassificationForm,
  updateClassificationType,
} from '../api/classifications'
import type {
  ClassificationType,
  FieldKind,
  FormDirection,
  FormField,
} from '../api/classifications'
import { errorCodeOf } from '../api/users'

/**
 * The classification form, as the supervisor defines it (S-40).
 *
 * Two things on one screen because they are one decision: the list of call
 * types, and the questions asked about a call. A field that only applies to
 * complaints has to name the complaint type, so editing them apart would mean
 * holding one in your head while changing the other.
 *
 * There are three sets of questions: one for calls that come in, one for
 * calls the agent places — a call-back or a confirmation is not an order, and
 * asking for a branch and an order value on it was noise — and one for
 * messages (A-70), the conversations on WhatsApp and the other apps, which have
 * no direction and are recorded rather than answered. The types are shared
 * because the reports count by type whichever way the conversation went.
 *
 * Publishing writes a **new version**. Existing classifications keep the
 * version they were captured under, so a complaint classified in January still
 * reads back with January's questions — and a form that turns out wrong can be
 * undone by publishing the old one again.
 */
export default function ClassificationPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const { data: form, isLoading } = useQuery({
    queryKey: ['classification-form', 'In'],
    queryFn: () => getClassificationForm('In'),
  })

  const { data: outbound } = useQuery({
    queryKey: ['classification-form', 'Out'],
    queryFn: () => getClassificationForm('Out'),
  })

  const { data: messages } = useQuery({
    queryKey: ['classification-form', 'None'],
    queryFn: () => getClassificationForm('None'),
  })

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['classification-form'] })

  return (
    <div className="space-y-6">
      <div>
        <h2 className="page-title">{t('classification.heading')}</h2>
        <p className="page-subtitle">{t('classification.intro')}</p>
      </div>

      {isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : form ? (
        <>
          <TypesCard types={form.types} onChanged={refresh} />
          <FieldsCard
            direction="In"
            version={form.version}
            fields={form.definition.fields}
            types={form.types}
            onPublished={refresh}
          />
          {outbound && (
            <FieldsCard
              direction="Out"
              version={outbound.version}
              fields={outbound.definition.fields}
              types={outbound.types}
              onPublished={refresh}
            />
          )}
          {messages && (
            <FieldsCard
              direction="None"
              version={messages.version}
              fields={messages.definition.fields}
              types={messages.types}
              onPublished={refresh}
            />
          )}
        </>
      ) : (
        <div className="card card-body text-center">
          <p className="text-slate-300">{t('classification.unavailable')}</p>
        </div>
      )}
    </div>
  )
}

/** The list of things a call can be about (S-40). */
function TypesCard({
  types,
  onChanged,
}: {
  types: ClassificationType[]
  onChanged: () => void
}) {
  const { t } = useTranslation()
  const [error, setError] = useState<string | null>(null)
  const [newAr, setNewAr] = useState('')
  const [newEn, setNewEn] = useState('')

  const fail = (e: unknown) => setError(t(`classification.errors.${errorCodeOf(e)}`))

  const add = useMutation({
    mutationFn: () =>
      createClassificationType({
        labelAr: newAr.trim(),
        labelEn: newEn.trim(),
        sortOrder: types.length * 10,
        isActive: true,
      }),
    onSuccess: () => {
      setNewAr('')
      setNewEn('')
      onChanged()
    },
    onError: fail,
  })

  const save = useMutation({
    mutationFn: (type: ClassificationType) =>
      updateClassificationType(type.id, {
        labelAr: type.labelAr,
        labelEn: type.labelEn,
        colour: type.colour,
        sortOrder: type.sortOrder,
        isActive: type.isActive,
      }),
    onSuccess: onChanged,
    onError: fail,
  })

  const remove = useMutation({
    mutationFn: deleteClassificationType,
    onSuccess: onChanged,
    onError: fail,
  })

  return (
    <div className="card card-body space-y-4">
      <div>
        <h3 className="text-base font-semibold text-slate-100">
          {t('classification.types')}
        </h3>
        <p className="field-hint">{t('classification.typesHint')}</p>
      </div>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      <div className="overflow-x-auto">
        <table className="table">
          <thead>
            <tr>
              <th>{t('classification.labelAr')}</th>
              <th>{t('classification.labelEn')}</th>
              <th>{t('classification.key')}</th>
              <th>{t('classification.shown')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {types.map((type) => (
              <tr key={type.id}>
                <td>
                  <input
                    defaultValue={type.labelAr}
                    aria-label={`${t('classification.labelAr')} ${type.name}`}
                    onBlur={(e) => {
                      const labelAr = e.target.value.trim()
                      if (labelAr && labelAr !== type.labelAr) save.mutate({ ...type, labelAr })
                    }}
                    className="input max-w-[12rem]"
                  />
                </td>
                <td>
                  <input
                    defaultValue={type.labelEn}
                    aria-label={`${t('classification.labelEn')} ${type.name}`}
                    onBlur={(e) => {
                      const labelEn = e.target.value.trim()
                      if (labelEn && labelEn !== type.labelEn) save.mutate({ ...type, labelEn })
                    }}
                    className="input max-w-[12rem]"
                  />
                </td>
                {/* The key never changes: reports and the show-when rules are
                    written against it, so renaming is what the labels are for. */}
                <td className="tabular text-xs text-slate-500">{type.name}</td>
                <td>
                  <label className="flex items-center gap-2 text-sm text-slate-300">
                    <input
                      type="checkbox"
                      checked={type.isActive}
                      onChange={(e) => save.mutate({ ...type, isActive: e.target.checked })}
                      className="accent-brand-500"
                    />
                    {type.isActive ? t('classification.offered') : t('classification.hidden')}
                  </label>
                </td>
                <td className="text-end whitespace-nowrap">
                  {type.isSystem || type.inUse ? (
                    <span className="text-xs text-slate-500">
                      {type.isSystem
                        ? t('classification.builtIn')
                        : t('classification.inUse')}
                    </span>
                  ) : (
                    <button
                      type="button"
                      onClick={() => remove.mutate(type.id)}
                      className="btn-ghost btn-sm"
                    >
                      {t('classification.remove')}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="flex flex-wrap items-end gap-2">
        <label className="field max-w-[12rem]">
          <span className="field-label">{t('classification.labelAr')}</span>
          <input value={newAr} onChange={(e) => setNewAr(e.target.value)} className="input" />
        </label>
        <label className="field max-w-[12rem]">
          <span className="field-label">{t('classification.labelEn')}</span>
          <input value={newEn} onChange={(e) => setNewEn(e.target.value)} className="input" />
        </label>
        <button
          type="button"
          onClick={() => {
            setError(null)
            add.mutate()
          }}
          disabled={newAr.trim().length === 0 || newEn.trim().length === 0 || add.isPending}
          className="btn-primary"
        >
          {t('classification.addType')}
        </button>
      </div>
    </div>
  )
}

/** Each form's heading and its hint, by the direction it is published under. */
const FORM_LABELS: Record<FormDirection, { title: string; hint: string }> = {
  In: { title: 'classification.fieldsIn', hint: 'classification.fieldsHint' },
  Out: { title: 'classification.fieldsOut', hint: 'classification.fieldsOutHint' },
  None: { title: 'classification.fieldsApp', hint: 'classification.fieldsAppHint' },
}

/** Every kind of question the form can ask, and what it is called. */
const KINDS: FieldKind[] = ['text', 'textarea', 'number', 'select', 'checkbox']

/** The questions asked about a call (S-40). */
function FieldsCard({
  direction,
  version,
  fields,
  types,
  onPublished,
}: {
  direction: FormDirection
  version: number
  fields: FormField[]
  types: ClassificationType[]
  onPublished: () => void
}) {
  const { t } = useTranslation()
  const [draft, setDraft] = useState<FormField[]>(fields)
  const [error, setError] = useState<string | null>(null)

  // Reset when a new version arrives, so publishing leaves the screen showing
  // what was actually published rather than the draft that produced it.
  useEffect(() => setDraft(fields), [fields])

  const publish = useMutation({
    mutationFn: () => publishClassificationForm({ fields: draft }, direction),
    onSuccess: onPublished,
    onError: (e) => setError(t(`classification.errors.${errorCodeOf(e)}`)),
  })

  function update(index: number, change: Partial<FormField>) {
    setDraft((current) =>
      current.map((field, i) => (i === index ? { ...field, ...change } : field)),
    )
  }

  function move(index: number, by: number) {
    setDraft((current) => {
      const next = [...current]
      const target = index + by
      if (target < 0 || target >= next.length) return current
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
  }

  function add() {
    // The key is what answers are stored against, so it has to be unique and
    // must never change once anyone has answered it.
    const key = `field_${Date.now().toString(36)}`
    setDraft((current) => [...current, { key, kind: 'text', label: { ar: '', en: '' } }])
  }

  const changed = JSON.stringify(draft) !== JSON.stringify(fields)

  return (
    <div className="card card-body space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-base font-semibold text-slate-100">
            {t(FORM_LABELS[direction].title)}
          </h3>
          <p className="field-hint">{t(FORM_LABELS[direction].hint)}</p>
        </div>
        <span className="badge-muted">{t('classification.version', { version })}</span>
      </div>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      <ul className="divide-y divide-ink-800">
        {draft.map((field, index) => (
          <FieldRow
            key={field.key}
            field={field}
            index={index}
            count={draft.length}
            types={types}
            onChange={(change) => update(index, change)}
            onMove={(by) => move(index, by)}
            onRemove={() => setDraft((c) => c.filter((_, i) => i !== index))}
          />
        ))}
      </ul>

      <div className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={add} className="btn-ghost">
          {t('classification.addField')}
        </button>

        <button
          type="button"
          onClick={() => {
            setError(null)
            publish.mutate()
          }}
          disabled={!changed || publish.isPending}
          className="btn-primary ms-auto"
        >
          {t('classification.publish')}
        </button>

        {changed && (
          <button type="button" onClick={() => setDraft(fields)} className="btn-ghost">
            {t('classification.discard')}
          </button>
        )}
      </div>

      <p className="field-hint">{t('classification.publishHint')}</p>
    </div>
  )
}

function FieldRow({
  field,
  index,
  count,
  types,
  onChange,
  onMove,
  onRemove,
}: {
  field: FormField
  index: number
  count: number
  types: ClassificationType[]
  onChange: (change: Partial<FormField>) => void
  onMove: (by: number) => void
  onRemove: () => void
}) {
  const { t } = useTranslation()

  // The type and branch questions are what the system is built around: the
  // reports group by them and they have their own columns. They can be moved
  // and relabelled but not removed or turned into something else.
  const isBuiltIn = field.kind === 'type' || field.kind === 'branch'

  return (
    <li className="space-y-2 py-3">
      <div className="flex flex-wrap items-end gap-2">
        <label className="field max-w-[11rem]">
          <span className="field-label">{t('classification.labelAr')}</span>
          <input
            value={field.label?.ar ?? ''}
            onChange={(e) => onChange({ label: { ...field.label, ar: e.target.value } })}
            className="input"
          />
        </label>

        <label className="field max-w-[11rem]">
          <span className="field-label">{t('classification.labelEn')}</span>
          <input
            value={field.label?.en ?? ''}
            onChange={(e) => onChange({ label: { ...field.label, en: e.target.value } })}
            className="input"
          />
        </label>

        <label className="field max-w-[9rem]">
          <span className="field-label">{t('classification.kind')}</span>
          {isBuiltIn ? (
            <input value={t(`classification.kinds.${field.kind}`)} disabled className="input" />
          ) : (
            <select
              value={field.kind}
              onChange={(e) => onChange({ kind: e.target.value as FieldKind })}
              className="input"
            >
              {KINDS.map((kind) => (
                <option key={kind} value={kind}>
                  {t(`classification.kinds.${kind}`)}
                </option>
              ))}
            </select>
          )}
        </label>

        <label className="flex items-center gap-2 pb-2 text-sm text-slate-300">
          <input
            type="checkbox"
            checked={field.required ?? false}
            onChange={(e) => onChange({ required: e.target.checked })}
            className="accent-brand-500"
          />
          {t('classification.required')}
        </label>

        <div className="ms-auto flex items-center gap-1 pb-2">
          <button
            type="button"
            onClick={() => onMove(-1)}
            disabled={index === 0}
            aria-label={t('classification.moveUp')}
            className="btn-ghost btn-sm"
          >
            ↑
          </button>
          <button
            type="button"
            onClick={() => onMove(1)}
            disabled={index === count - 1}
            aria-label={t('classification.moveDown')}
            className="btn-ghost btn-sm"
          >
            ↓
          </button>
          {!isBuiltIn && (
            <button type="button" onClick={onRemove} className="btn-ghost btn-sm">
              {t('classification.remove')}
            </button>
          )}
        </div>
      </div>

      {field.kind === 'type' ? (
        <OfferedTypes field={field} types={types} onChange={(list) => onChange({ types: list })} />
      ) : (
      /* Which call types this question is asked for. Nothing ticked means
          always, which is the common case and so needs no ticking. */
      <div className="flex flex-wrap items-center gap-3">
        <span className="field-hint">{t('classification.askedFor')}</span>
        {types.map((type) => {
          const on = field.showWhenType?.includes(type.name) ?? false
          return (
            <label key={type.id} className="flex items-center gap-1.5 text-xs text-slate-400">
              <input
                type="checkbox"
                checked={on}
                onChange={(e) => {
                  const next = new Set(field.showWhenType ?? [])
                  if (e.target.checked) next.add(type.name)
                  else next.delete(type.name)
                  onChange({ showWhenType: next.size > 0 ? [...next] : undefined })
                }}
                className="accent-brand-500"
              />
              {type.labelEn}
            </label>
          )
        })}
      </div>
      )}

      {field.kind === 'select' && (
        <SelectOptions
          field={field}
          onChange={(options) => onChange({ options })}
        />
      )}
    </li>
  )
}

/**
 * Which call types a form offers (S-40), chosen by the supervisor per form.
 *
 * Stored on the type question as a list of type *names*, the same way
 * showWhenType is, so relabelling a type never takes it off a form. No list
 * means every type, which is what every form did before and what a form with
 * all the boxes ticked goes back to: a type added later then joins it
 * without anyone remembering to tick it. At least one box stays ticked,
 * because a form that offers no type can never be saved (the server refuses
 * it too). Hidden types are left out unless the form already lists one.
 */
function OfferedTypes({
  field,
  types,
  onChange,
}: {
  field: FormField
  types: ClassificationType[]
  onChange: (types: string[] | undefined) => void
}) {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const listed = field.types && field.types.length > 0 ? field.types : null
  const shown = types.filter((ty) => ty.isActive || listed?.includes(ty.name))
  const isOn = (name: string) => listed === null || listed.includes(name)
  const onCount = shown.filter((ty) => isOn(ty.name)).length

  function toggle(name: string, on: boolean) {
    const next = new Set(shown.filter((ty) => isOn(ty.name)).map((ty) => ty.name))
    if (on) next.add(name)
    else next.delete(name)
    // Every box ticked is "all types", stored as no list.
    const all = shown.every((ty) => next.has(ty.name))
    onChange(all ? undefined : shown.map((ty) => ty.name).filter((n) => next.has(n)))
  }

  return (
    <div className="space-y-1">
      <div className="flex flex-wrap items-center gap-3">
        <span className="field-hint">{t('classification.typesOffered')}</span>
        {shown.map((type) => {
          const on = isOn(type.name)
          const last = on && onCount === 1
          return (
            <label
              key={type.id}
              className="flex items-center gap-1.5 text-xs text-slate-300"
              title={last ? t('classification.typesOfferedKeepOne') : undefined}
            >
              <input
                type="checkbox"
                checked={on}
                disabled={last}
                onChange={(e) => toggle(type.name, e.target.checked)}
                className="accent-brand-500"
              />
              {arabic ? type.labelAr : type.labelEn}
            </label>
          )
        })}
      </div>
      <p className="field-hint">{t('classification.typesOfferedHint')}</p>
    </div>
  )
}

/** The choices in a dropdown the supervisor wrote. */
function SelectOptions({
  field,
  onChange,
}: {
  field: FormField
  onChange: (options: FormField['options']) => void
}) {
  const { t } = useTranslation()
  const options = field.options ?? []

  return (
    <div className="space-y-2 ps-4">
      {options.map((option, i) => (
        <Fragment key={i}>
          <div className="flex flex-wrap items-end gap-2">
            <label className="field max-w-[10rem]">
              <span className="field-label">{t('classification.labelAr')}</span>
              <input
                value={option.label?.ar ?? ''}
                onChange={(e) =>
                  onChange(
                    options.map((o, j) =>
                      j === i ? { ...o, label: { ...o.label, ar: e.target.value } } : o,
                    ),
                  )
                }
                className="input"
              />
            </label>
            <label className="field max-w-[10rem]">
              <span className="field-label">{t('classification.labelEn')}</span>
              <input
                value={option.label?.en ?? ''}
                onChange={(e) =>
                  onChange(
                    options.map((o, j) =>
                      j === i ? { ...o, label: { ...o.label, en: e.target.value } } : o,
                    ),
                  )
                }
                className="input"
              />
            </label>
            <button
              type="button"
              onClick={() => onChange(options.filter((_, j) => j !== i))}
              className="btn-ghost btn-sm mb-2"
            >
              {t('classification.remove')}
            </button>
          </div>
        </Fragment>
      ))}

      <button
        type="button"
        onClick={() =>
          onChange([
            ...options,
            // The stored value never changes once answers exist, so it is
            // generated rather than typed - a supervisor editing a label must
            // not silently orphan every answer given under it.
            { value: `opt_${Date.now().toString(36)}`, label: { ar: '', en: '' } },
          ])
        }
        className="btn-ghost btn-sm"
      >
        {t('classification.addOption')}
      </button>
    </div>
  )
}
