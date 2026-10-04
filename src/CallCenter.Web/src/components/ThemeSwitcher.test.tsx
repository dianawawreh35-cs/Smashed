import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import ThemeSwitcher from './ThemeSwitcher'
import i18n from '../i18n'
import { storedTheme, syncTheme, THEME_STORAGE_KEY } from '../lib/theme'

/** Light or dark, remembered in the browser (S-69). */

describe('ThemeSwitcher', () => {
  beforeEach(async () => {
    localStorage.clear()
    syncTheme('dark')
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    localStorage.clear()
    syncTheme('dark')
  })

  it('starts dark for a browser that never chose', () => {
    expect(storedTheme()).toBe('dark')
  })

  it('switches to light and back, and remembers each choice', () => {
    render(<ThemeSwitcher />)

    fireEvent.click(screen.getByRole('button', { name: 'Light mode' }))
    expect(document.documentElement.dataset.theme).toBe('light')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light')
    expect(storedTheme()).toBe('light')

    // The button now offers the other one.
    fireEvent.click(screen.getByRole('button', { name: 'Dark mode' }))
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(storedTheme()).toBe('dark')
  })

  it('follows a change made elsewhere on the page', () => {
    render(<ThemeSwitcher />)
    act(() => syncTheme('light'))
    expect(screen.getByRole('button', { name: 'Dark mode' })).toBeInTheDocument()
  })
})
