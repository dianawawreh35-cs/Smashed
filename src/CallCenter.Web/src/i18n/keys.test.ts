import { afterAll, describe, expect, it } from 'vitest'
import ar from './ar.json'
import en from './en.json'
import i18n from './index'

/**
 * The two language files (A-80, N-09): the same labels in both, and every
 * count in words that agree with it (M-W10).
 *
 * Arabic has six plural forms (zero, one, two, few for 3 to 10, many for 11
 * to 99, other) and English two, so the files cannot have identical keys
 * once a string has plurals. What must match is everything else: each label,
 * with its plural suffix set aside, and the placeholders it takes.
 */

type Tree = { [key: string]: string | Tree }

const SUFFIX = /_(zero|one|two|few|many|other)$/

function flatten(tree: Tree, prefix = ''): Record<string, string> {
  return Object.fromEntries(
    Object.entries(tree).flatMap(([key, value]) =>
      typeof value === 'string' ? [[`${prefix}${key}`, value]] : Object.entries(flatten(value, `${prefix}${key}.`)),
    ),
  )
}

const files = { ar: flatten(ar as Tree), en: flatten(en as Tree) }

/** Label → its plural forms (empty for a label with none). */
function labels(strings: Record<string, string>): Map<string, Set<string>> {
  const result = new Map<string, Set<string>>()
  for (const key of Object.keys(strings)) {
    const form = key.match(SUFFIX)?.[1]
    const base = key.replace(SUFFIX, '')
    if (!result.has(base)) result.set(base, new Set())
    if (form) result.get(base)!.add(form)
  }
  return result
}

/**
 * Label → the {{placeholders}} it takes in any of its forms, except count (a
 * form may spell the number out). One pass over the file: searching the whole
 * file once per label grew with the square of the labels, and at about 1,500
 * it ran past the five-second limit under a full parallel run (9 Oct 2026).
 */
function placeholders(strings: Record<string, string>): Map<string, string[]> {
  const found = new Map<string, Set<string>>()
  for (const [key, text] of Object.entries(strings)) {
    const base = key.replace(SUFFIX, '')
    if (!found.has(base)) found.set(base, new Set())
    for (const [, name] of text.matchAll(/\{\{(\w+)\}\}/g)) if (name !== 'count') found.get(base)!.add(name)
  }
  return new Map([...found].map(([base, names]) => [base, [...names].sort()]))
}

afterAll(async () => {
  await i18n.changeLanguage('en')
})

describe('the language files', () => {
  it('have the same labels in Arabic and English', () => {
    const arabic = [...labels(files.ar).keys()].sort()
    const english = [...labels(files.en).keys()].sort()
    expect(arabic).toEqual(english)
  })

  it('give each label the same placeholders in both languages', () => {
    const arabic = placeholders(files.ar)
    const english = placeholders(files.en)
    for (const base of labels(files.en).keys()) {
      expect({ base, ar: arabic.get(base) ?? [] }).toEqual({ base, ar: english.get(base) ?? [] })
    }
  })

  it('give every plural each form its language needs', () => {
    for (const [lang, strings] of Object.entries(files)) {
      const needed = new Intl.PluralRules(lang).resolvedOptions().pluralCategories
      for (const [base, forms] of labels(strings)) {
        if (forms.size === 0) continue
        for (const form of needed) expect({ lang, base, has: forms.has(form) }).toEqual({ lang, base, has: true })
        // A plural label has no bare form beside them: it would never be used.
        expect({ lang, base, bare: base in strings }).toEqual({ lang, base, bare: false })
      }
    }
  })

  it('count in Arabic with the form each number takes', async () => {
    await i18n.changeLanguage('ar')
    expect(i18n.t('calls.count', { count: 1 })).toBe('مكالمة واحدة')
    expect(i18n.t('calls.count', { count: 2 })).toBe('مكالمتان')
    expect(i18n.t('calls.count', { count: 5 })).toBe('5 مكالمات')
    expect(i18n.t('calls.count', { count: 11 })).toBe('11 مكالمة')
    expect(i18n.t('applications.count', { count: 2 })).toBe('تطبيقان')
    expect(i18n.t('applications.count', { count: 5 })).toBe('5 تطبيقات')
    expect(i18n.t('menu.itemCount', { count: 2 })).toBe('صنفان')
    expect(i18n.t('menu.itemCount', { count: 5 })).toBe('5 أصناف')
  })

  it('count in English without "1 items" or "already 2 contact"', async () => {
    await i18n.changeLanguage('en')
    expect(i18n.t('menu.itemCount', { count: 1 })).toBe('1 item')
    expect(i18n.t('menu.itemCount', { count: 3 })).toBe('3 items')
    expect(i18n.t('contacts.sameNameWarning', { count: 2 })).toBe('There are already 2 contacts with this name:')
    expect(i18n.t('delivery.importProblems', { count: 1 })).toBe('1 line could not be used:')
  })
})
