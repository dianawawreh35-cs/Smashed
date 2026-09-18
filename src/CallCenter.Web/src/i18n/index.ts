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

/**
 * Puts the language and its direction on the document element.
 *
 * Separate from {@link applyLanguage} because it has to run at startup too.
 * `index.html` is written `dir="rtl"`, which is right for the default but wrong
 * for anyone whose stored choice is English - without this the page loads
 * right-to-left with English text, and the full stops land at the start of the
 * line.
 */
function syncDocument(language: Language): void {
  document.documentElement.lang = language
  document.documentElement.dir = directionOf(language)
}

/** Applies the language and its direction to the document element. */
export function applyLanguage(language: Language): void {
  void i18n.changeLanguage(language)
  syncDocument(language)
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

// The stored choice decides the direction from the first paint, not from the
// first time someone touches the switcher.
syncDocument(storedLanguage())

export default i18n
