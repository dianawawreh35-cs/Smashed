import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, screen, within } from '@testing-library/react'
import ReportCard from './ReportCard'
import type { ReportColumn } from './ReportCard'
import i18n from '../i18n'
import { renderWithClient } from '../test/http'

/** A report table sorted by its headings (S-70). */

interface Row {
  agent: string
  calls: number
}

const ROWS: Row[] = [
  { agent: 'Sara', calls: 4 },
  { agent: 'Ahmad', calls: 12 },
  { agent: 'Lina', calls: 7 },
]

const COLUMNS: ReportColumn<Row>[] = [
  { key: 'agent', label: 'Agent', value: (r) => r.agent },
  { key: 'calls', label: 'Calls', value: (r) => r.calls, numeric: true, total: true },
]

function firstColumn(): string[] {
  const rows = within(screen.getByRole('table')).getAllByRole('row').slice(1)
  return rows.map((row) => within(row).getAllByRole('cell')[0].textContent ?? '')
}

beforeEach(async () => {
  await i18n.changeLanguage('en')
})

describe('a report table', () => {
  it('sorts by a figure most first, then least first, then back to the report order', () => {
    renderWithClient(<ReportCard title="By agent" columns={COLUMNS} rows={ROWS} loading={false} error={false} exportName="x" />)
    const calls = screen.getByRole('button', { name: /Calls/ })

    // The total row stays last whatever the order.
    expect(firstColumn()).toEqual(['Sara', 'Ahmad', 'Lina', 'Total'])

    fireEvent.click(calls)
    expect(firstColumn()).toEqual(['Ahmad', 'Lina', 'Sara', 'Total'])
    expect(screen.getByRole('columnheader', { name: /Calls/ })).toHaveAttribute('aria-sort', 'descending')

    fireEvent.click(calls)
    expect(firstColumn()).toEqual(['Sara', 'Lina', 'Ahmad', 'Total'])
    expect(screen.getByRole('columnheader', { name: /Calls/ })).toHaveAttribute('aria-sort', 'ascending')

    fireEvent.click(calls)
    expect(firstColumn()).toEqual(['Sara', 'Ahmad', 'Lina', 'Total'])
    expect(screen.getByRole('columnheader', { name: /Calls/ })).not.toHaveAttribute('aria-sort')
  })

  it('sorts text A to Z first', () => {
    renderWithClient(<ReportCard title="By agent" columns={COLUMNS} rows={ROWS} loading={false} error={false} exportName="x" />)
    fireEvent.click(screen.getByRole('button', { name: /Agent/ }))
    expect(firstColumn()).toEqual(['Ahmad', 'Lina', 'Sara', 'Total'])
  })

  it('keeps the heading and the first column on paper', () => {
    // Print hides buttons; these two are the table's own text (index.css).
    renderWithClient(<ReportCard title="By agent" columns={COLUMNS} rows={ROWS} loading={false} error={false} exportName="x"
      expand={() => <p>calls</p>} />)
    expect(screen.getByRole('button', { name: /Calls/ })).toHaveClass('print-keep')
    expect(screen.getByRole('button', { name: /Sara/ })).toHaveClass('print-keep')
  })
})
