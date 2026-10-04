import { useSyncExternalStore } from 'react'

/**
 * Light or dark (S-69): the supervisor's choice, remembered in this browser as
 * the language is, and dark until somebody changes it.
 *
 * The colours are CSS variables (index.css) that Tailwind's ink, slate, brand
 * and status shades read, so the whole app follows `data-theme` on the
 * document element. `index.html` sets it before the first paint from the same
 * stored value, so a light page never flashes dark while the script loads.
 * The few colours drawn by script rather than CSS, the charts, read
 * {@link useTheme}.
 */

export const THEMES = ['dark', 'light'] as const
export type Theme = (typeof THEMES)[number]

export const DEFAULT_THEME: Theme = 'dark'

/** The key `index.html` reads too; change both together. */
export const THEME_STORAGE_KEY = 'callcenter.theme'

/** The browser's own bar colour on phones: the canvas of each theme (ink-950). */
const THEME_COLOUR: Record<Theme, string> = { dark: '#0F1115', light: '#F4F5F7' }

const listeners = new Set<() => void>()

export function storedTheme(): Theme {
  try {
    return localStorage.getItem(THEME_STORAGE_KEY) === 'light' ? 'light' : DEFAULT_THEME
  } catch {
    // private mode / blocked storage - the default, as for the language
    return DEFAULT_THEME
  }
}

/** The theme on the page now. */
export function currentTheme(): Theme {
  return document.documentElement.dataset.theme === 'light' ? 'light' : 'dark'
}

/** Puts a theme on the page without remembering it: start-up. */
export function syncTheme(theme: Theme): void {
  const root = document.documentElement
  root.dataset.theme = theme
  root.style.colorScheme = theme
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', THEME_COLOUR[theme])
  listeners.forEach((listener) => listener())
}

/** Switches theme and remembers it for this browser. */
export function applyTheme(theme: Theme): void {
  try {
    localStorage.setItem(THEME_STORAGE_KEY, theme)
  } catch {
    // still switch for this visit
  }
  syncTheme(theme)
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** The theme, re-rendering the component when it changes. */
export function useTheme(): Theme {
  return useSyncExternalStore(subscribe, currentTheme, () => DEFAULT_THEME)
}
