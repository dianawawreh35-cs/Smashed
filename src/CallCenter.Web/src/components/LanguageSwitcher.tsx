import { useTranslation } from 'react-i18next'
import { applyLanguage, SUPPORTED_LANGUAGES, type Language } from '../i18n'

const LABELS: Record<Language, string> = { ar: 'العربية', en: 'English' }

/** Toggles between Arabic (RTL) and English (LTR). */
export default function LanguageSwitcher() {
  const { i18n, t } = useTranslation()

  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="sr-only">{t('app.language')}</span>
      <select
        className="cursor-pointer rounded-md border border-ink-700 bg-ink-800 px-2.5 py-1.5
                   text-sm text-slate-200 transition hover:border-slate-500
                   focus:border-brand-500 focus:outline-none"
        value={i18n.resolvedLanguage}
        onChange={(event) => applyLanguage(event.target.value as Language)}
      >
        {SUPPORTED_LANGUAGES.map((code) => (
          <option key={code} value={code}>
            {LABELS[code]}
          </option>
        ))}
      </select>
    </label>
  )
}
