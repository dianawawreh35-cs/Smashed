import { useTranslation } from 'react-i18next'
import { applyTheme, useTheme } from '../lib/theme'

/**
 * Light or dark (S-69), beside the language choice. It shows what a press
 * gives: the sun in dark, the moon in light, its name in the tooltip and to a
 * screen reader.
 */
export default function ThemeSwitcher() {
  const { t } = useTranslation()
  const theme = useTheme()
  const next = theme === 'dark' ? 'light' : 'dark'
  const label = t(next === 'light' ? 'app.lightTheme' : 'app.darkTheme')

  return (
    <button type="button" onClick={() => applyTheme(next)} title={label} className="btn-quiet btn-sm">
      <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.75"
           strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden="true">
        {next === 'light' ? (
          <>
            <circle cx="10" cy="10" r="3.5" />
            <path d="M10 2v2M10 16v2M2 10h2M16 10h2M4.3 4.3l1.4 1.4M14.3 14.3l1.4 1.4M4.3 15.7l1.4-1.4M14.3 5.7l1.4-1.4" />
          </>
        ) : (
          <path d="M16.5 12.5A7 7 0 0 1 7.5 3.5a7 7 0 1 0 9 9z" />
        )}
      </svg>
      <span className="sr-only">{label}</span>
    </button>
  )
}
