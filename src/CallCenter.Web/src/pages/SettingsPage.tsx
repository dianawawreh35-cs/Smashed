import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { listSettings, settingProblems, updateSettings } from '../api/settings'
import type { Setting } from '../api/settings'

/**
 * System settings (S-47).
 *
 * The whole form saves at once, because the server applies a batch all or
 * nothing — a screen that could half-apply would be worse than one that makes
 * you fix the bad field first.
 */
export default function SettingsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const { data: settings, isLoading } = useQuery({ queryKey: ['settings'], queryFn: listSettings })

  const [draft, setDraft] = useState<Record<string, string>>({})
  const [problems, setProblems] = useState<Record<string, string>>({})
  const [saved, setSaved] = useState(false)

  // Fill the form once the values arrive, and again after a save returns them.
  useEffect(() => {
    if (settings) {
      setDraft(Object.fromEntries(settings.map((s) => [s.key, s.value])))
    }
  }, [settings])

  const save = useMutation({
    mutationFn: () => updateSettings(draft),
    onSuccess: () => {
      setProblems({})
      setSaved(true)
      void queryClient.invalidateQueries({ queryKey: ['settings'] })
    },
    onError: (error) => {
      setSaved(false)
      setProblems(settingProblems(error))
    },
  })

  if (isLoading) return <p className="text-slate-500">{t('app.loading')}</p>

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    setSaved(false)
    save.mutate()
  }

  const hasProblems = Object.keys(problems).length > 0

  return (
    <form onSubmit={onSubmit} className="space-y-6 max-w-3xl">
      <div>
        <h2 className="text-lg font-semibold">{t('settings.heading')}</h2>
        <p className="text-sm text-slate-500">{t('settings.intro')}</p>
      </div>

      {hasProblems && (
        <p role="alert" className="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
          {t('settings.rejected')}
        </p>
      )}

      {saved && !hasProblems && (
        <p role="status" className="rounded border border-emerald-300 bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
          {t('settings.saved')}
        </p>
      )}

      <div className="space-y-4 rounded border border-slate-200 bg-white p-4">
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
        className="rounded bg-brand-600 px-4 py-2 text-white disabled:opacity-50"
      >
        {t('settings.save')}
      </button>
    </form>
  )
}

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

  const inputClass = `w-full rounded border px-3 py-2 ${
    problem ? 'border-red-400 bg-red-50' : 'border-slate-300'
  }`

  return (
    <label className="block space-y-1">
      <span className="text-sm font-medium text-slate-700">{label}</span>
      {hint && <span className="block text-xs text-slate-500">{hint}</span>}

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
          value={value}
          onChange={(e) => onChange(e.target.value)}
          className={inputClass}
        />
      )}

      {problem && <span className="block text-xs text-red-700">{problem}</span>}

      {setting.updatedByDisplayName && (
        <span className="block text-xs text-slate-400">
          {t('settings.lastChangedBy', { name: setting.updatedByDisplayName })}
        </span>
      )}
    </label>
  )
}
