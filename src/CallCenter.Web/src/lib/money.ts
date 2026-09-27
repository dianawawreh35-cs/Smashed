/**
 * An amount of money as every screen shows it: two decimals, in the reader's
 * language, so a delivery price of 7.5 reads "7.50" as it does in the reports
 * beside it. Show it inside `dir="ltr"`, or `ltr()` in a sentence (lib/bidi),
 * so a signed amount keeps its sign in front in Arabic.
 */
export function formatMoney(value: number, language: string): string {
  return value.toLocaleString(language, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}
