import { describe, expect, it } from 'vitest'
import { toCsv } from './csv'

/** The CSV export (S-05), as a pure function: what Excel will open. */
describe('toCsv', () => {
  it('starts with a byte-order mark so Excel reads the Arabic', () => {
    const csv = toCsv(['القناة', 'الرسائل'], [['واتساب', 3]])
    expect(csv.charCodeAt(0)).toBe(0xfeff)
    expect(csv).toContain('واتساب,3')
  })

  it('writes the header then one line per row, CRLF-separated', () => {
    const csv = toCsv(['Channel', 'Messages'], [['WhatsApp', 3], ['Instagram', 0]])
    expect(csv.slice(1)).toBe('Channel,Messages\r\nWhatsApp,3\r\nInstagram,0\r\n')
  })

  it('quotes a field holding a comma, a quote or a line break', () => {
    const csv = toCsv(['Name'], [['Smashed, Ramallah'], ['He said "hi"'], ['two\nlines']])
    expect(csv).toContain('"Smashed, Ramallah"')
    expect(csv).toContain('"He said ""hi"""')
    expect(csv).toContain('"two\nlines"')
  })

  it('leaves a null or missing value blank, and writes numbers with a dot', () => {
    const csv = toCsv(['A', 'B', 'C'], [[null, undefined, 12.5]])
    expect(csv).toContain(',,12.5')
  })
})
