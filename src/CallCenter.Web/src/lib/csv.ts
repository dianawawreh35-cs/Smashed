/**
 * A report as a CSV file (S-05), built in the browser from the rows the page
 * already holds.
 *
 * Building it here rather than on the server is right only because a report's
 * rows are the whole report — one per channel, agent or day — never a page of
 * a longer list. Exporting a page of a list would lie the way filtering one
 * does (20 Sep), and a list export must come from the server.
 */

export type CsvCell = string | number | null | undefined

/** U+FEFF, spelt out: the literal character is invisible in an editor. */
const BOM = String.fromCharCode(0xfeff)

/**
 * Text for one field: quoted whenever it holds a comma, a quote or a line
 * break, with quotes doubled, as RFC 4180 has it. Numbers are written as
 * JavaScript prints them, with a dot, so Excel in any locale reads them as
 * numbers rather than text.
 */
function field(value: CsvCell): string {
  if (value === null || value === undefined) return ''
  const text = typeof value === 'number' ? String(value) : value
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

/**
 * Header row then one line per row, CRLF-separated, starting with a byte-order
 * mark: without the BOM, Excel opens a UTF-8 file as Windows-1252 and every
 * Arabic name comes out as question marks.
 */
export function toCsv(headers: string[], rows: CsvCell[][]): string {
  const lines = [headers, ...rows].map((cells) => cells.map(field).join(','))
  return `${BOM}${lines.join('\r\n')}\r\n`
}

/** Hands the browser the file to save, as a download of the given name. */
export function downloadCsv(filename: string, csv: string): void {
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename.endsWith('.csv') ? filename : `${filename}.csv`
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}
