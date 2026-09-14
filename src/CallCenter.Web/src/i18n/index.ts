import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import ar from './ar.json'
import en from './en.json'

export const SUPPORTED_LANGUAGES = ['ar', 'en'] as const
export type Language = (typeof SUPPORTED_LANGUAGES)[number]

export const DEFAULT_LANGUAGE: Language = 'ar'
const STORAGE_KEY = 'callcenter.lang'

function storedLanguage(): Language {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved && (SUPPORTED_LANGUAGES as readonly string[]).includes(saved)) {
      return saved as Language
    }
  } catch {
    // private mode / blocked storage - fall through to the default
  }
  return DEFAULT_LANGUAGE
}

/** Arabic is RTL; English is LTR. */
export function directionOf(language: string): 'rtl' | 'ltr' {
  return language === 'ar' ? 'rtl' : 'ltr'
}

/** Applies the language and its direction to the document element. */
export function applyLanguage(language: Language): void {
  void i18n.changeLanguage(language)
  document.documentElement.lang = language
  document.documentElement.dir = directionOf(language)
  try {
    localStorage.setItem(STORAGE_KEY, language)
  } catch {
    // ignore - the choice simply will not persist
  }
}

void i18n.use(initReactI18next).init({
  resources: {
    ar: { translation: ar },
    en: { translation: en },
  },
  lng: storedLanguage(),
  fallbackLng: DEFAULT_LANGUAGE,
  supportedLngs: [...SUPPORTED_LANGUAGES],
  interpolation: { escapeValue: false },
})

export default i18n
