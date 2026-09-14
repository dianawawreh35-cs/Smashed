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
        className="rounded border border-slate-300 bg-white px-2 py-1"
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
