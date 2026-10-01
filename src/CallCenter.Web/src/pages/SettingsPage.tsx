import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { listSettings, settingProblems, updateSettings } from '../api/settings'
import type { Setting } from '../api/settings'
import AbandonedImportCard from '../components/AbandonedImportCard'
import ChannelsCard from '../components/ChannelsCard'
import LoadError from '../components/LoadError'
import PbxBlacklistCard from '../components/PbxBlacklistCard'
import PosLookupCard from '../components/PosLookupCard'

/**
 * Settings: the system values (S-47), the POS customer lookup's Check now
 * (A-67), the PBX login the abandoned-call import uses (S-55), the extension
 * the PBX blacklist is dialled from (S-46), and the channel list (S-41), which
 * is a setting in the same sense — changed rarely, by the supervisor, read by
 * every agent's app.
 */
export default function SettingsPage() {
  return (
    <div className="max-w-3xl space-y-8">
      <SystemSettings />
      <PosLookupCard />
      <AbandonedImportCard />
      <PbxBlacklistCard />
      <ChannelsCard />
    </div>
  )
}

/**
 * System settings (S-47).
 *
 * The whole form saves at once, because the server applies a batch all or
 * nothing — a screen that could half-apply would be worse than one that makes
 * you fix the bad field first.
 *
 * Settings that did not load show no form (M-W04): a blank form here would
 * save blanks over every value. A save refused without naming a field (a 500,
 * no network) says it was not saved, rather than nothing.
 */
function SystemSettings() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const query = useQuery({ queryKey: ['settings'], queryFn: listSettings })
  const settings = query.data

  const [draft, setDraft] = useState<Record<string, string>>({})
  const [problems, setProblems] = useState<Record<string, string>>({})
  const [saved, setSaved] = useState(false)
  const [notSaved, setNotSaved] = useState(false)

  const fill = (values: Setting[]) => setDraft(Object.fromEntries(values.map((s) => [s.key, s.value])))

  // Fill the form once the values arrive, and again from what a save returns.
  const filled = useRef(false)
  useEffect(() => {
    if (settings && !filled.current) {
      filled.current = true
      fill(settings)
    }
  }, [settings])

  const save = useMutation({
    mutationFn: () => updateSettings(draft),
    onSuccess: (values) => {
      setProblems({})
      setNotSaved(false)
      setSaved(true)
      fill(values)
      queryClient.setQueryData(['settings'], values)
    },
    onError: (error) => {
      setSaved(false)
      const fields = settingProblems(error)
      setProblems(fields)
      setNotSaved(Object.keys(fields).length === 0)
    },
  })

  if (query.isPending) return <p className="text-slate-400">{t('app.loading')}</p>

  if (query.isError && !settings) {
    return (
      <section className="space-y-6" aria-label={t('settings.heading')}>
        <h1 className="page-title">{t('settings.heading')}</h1>
        <LoadError message={t('settings.failed')} onRetry={() => void query.refetch()} busy={query.isFetching} />
      </section>
    )
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    setSaved(false)
    save.mutate()
  }

  const hasProblems = Object.keys(problems).length > 0

  return (
    <form onSubmit={onSubmit} className="space-y-6">
      <div>
        <h1 className="page-title">{t('settings.heading')}</h1>
        <p className="page-subtitle">{t('settings.intro')}</p>
      </div>

      {hasProblems && (
        <p role="alert" className="notice-error">
          {t('settings.rejected')}
        </p>
      )}

      {saved && !hasProblems && (
        <p role="status" className="notice-success">
          {t('settings.saved')}
        </p>
      )}
      {notSaved && <p role="alert" className="notice-error">{t('common.notSaved')}</p>}

      <div className="card card-body space-y-5">
        {settings?.map((setting) => (
          <SettingField
            key={setting.key}
            setting={setting}
            value={draft[setting.key] ?? ''}
            problem={problems[setting.key]}
            onChange={(value) => setDraft((d) => ({ ...d, [setting.key]: value }))}
          />
        ))}
      </div>

      <button
        type="submit"
        disabled={save.isPending}
        className="btn-primary"
      >
        {t('settings.save')}
      </button>
    </form>
  )
}

/**
 * Settings whose values are addresses, times or lists of numbers, typed left
 * to right in Arabic too (M-W01): "2001, 2002" would otherwise show as
 * "2002 ,2001".
 */
const LTR_KEYS = new Set(['pbx.host', 'reports.internal_numbers', 'queue.auto_open_time'])

function SettingField({
  setting,
  value,
  problem,
  onChange,
}: {
  setting: Setting
  value: string
  problem?: string
  onChange: (value: string) => void
}) {
  const { t } = useTranslation()

  // Every key has its own label and explanation; an unknown one falls back to
  // the raw key rather than showing nothing.
  const label = t(`settings.keys.${setting.key}.label`, { defaultValue: setting.key })
  const hint = t(`settings.keys.${setting.key}.hint`, { defaultValue: '' })

  const inputClass = `input ${problem ? 'input-invalid' : ''}`

  return (
    <label className="field">
      <span className="field-label">{label}</span>
      {hint && <span className="field-hint">{hint}</span>}

      {setting.kind === 'Boolean' ? (
        <select value={value || 'false'} onChange={(e) => onChange(e.target.value)} className={inputClass}>
          <option value="true">{t('settings.yes')}</option>
          <option value="false">{t('settings.no')}</option>
        </select>
      ) : setting.kind === 'Choice' ? (
        <select value={value} onChange={(e) => onChange(e.target.value)} className={inputClass}>
          {setting.options?.map((option) => (
            <option key={option} value={option}>
              {t(`settings.choices.${option}`, { defaultValue: option })}
            </option>
          ))}
        </select>
      ) : (
        <input
          type={setting.kind === 'Integer' ? 'number' : 'text'}
          dir={setting.kind === 'Integer' || LTR_KEYS.has(setting.key) ? 'ltr' : undefined}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          className={inputClass}
        />
      )}

      {problem && <span className="block text-xs text-red-400">{problem}</span>}

      {setting.updatedByDisplayName && (
        <span className="block text-xs text-slate-500">
          {t('settings.lastChangedBy', { name: setting.updatedByDisplayName })}
        </span>
      )}
    </label>
  )
}
