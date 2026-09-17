import { beforeEach, describe, expect, it } from 'vitest'
import { applyLanguage, directionOf } from './index'

/**
 * Language and reading direction (A-80).
 *
 * The direction used to be set only when someone touched the switcher, so a
 * stored English choice loaded the page right-to-left — `index.html` is written
 * `dir="rtl"` — and English sentences came out with their full stops at the
 * start of the line. These check that the two stay in step.
 */
describe('language direction', () => {
  beforeEach(() => {
    document.documentElement.dir = 'rtl'
    document.documentElement.lang = 'ar'
  })

  it('knows which way each language reads', () => {
    expect(directionOf('ar')).toBe('rtl')
    expect(directionOf('en')).toBe('ltr')
  })

  it('turns the document around when English is chosen', () => {
    applyLanguage('en')

    expect(document.documentElement.dir).toBe('ltr')
    expect(document.documentElement.lang).toBe('en')
  })

  it('turns it back for Arabic', () => {
    applyLanguage('en')
    applyLanguage('ar')

    expect(document.documentElement.dir).toBe('rtl')
    expect(document.documentElement.lang).toBe('ar')
  })

  it('remembers the choice for the next visit', () => {
    applyLanguage('en')

    expect(localStorage.getItem('callcenter.lang')).toBe('en')
  })
})
