/**
 * Phone numbers and signed amounts in an Arabic page (M-W01, A-80).
 *
 * Digits are "weak" in the Unicode bidi algorithm: on their own they read
 * left to right, but the spaces, dashes and plus signs between them take the
 * page's direction. In Arabic `+970 59 912 3456` therefore shows as
 * `3456 912 59 +970`, and `+5` as `5+`. The cure is to isolate the number as
 * left to right: `dir="ltr"` on the element that holds it (the browser isolates
 * any element with a `dir`), or, inside a translated sentence where there is no
 * element of its own, the Unicode isolate characters this file adds.
 */

/** LEFT-TO-RIGHT ISOLATE … POP DIRECTIONAL ISOLATE, spelt out: both are invisible. */
const LRI = String.fromCharCode(0x2066)
const PDI = String.fromCharCode(0x2069)

/** The text held left to right wherever it lands, for a number passed into a translated sentence. */
export function ltr(text: string): string {
  return `${LRI}${text}${PDI}`
}

/** Whether what was typed is a bare phone number rather than a name. */
export function looksLikeNumber(text: string): boolean {
  return /^[\d\s+()-]{3,}$/.test(text.trim())
}

/**
 * The direction for a box that takes a name or a number. `auto` reads a name
 * the way it is written, but a number has no letters for it to go by and would
 * take the page's direction, so a number is set left to right outright.
 */
export function searchDir(text: string): 'ltr' | 'auto' {
  return /\d/.test(text) && /^[\d\s+()-]*$/.test(text.trim()) ? 'ltr' : 'auto'
}
