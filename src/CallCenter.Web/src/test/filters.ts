import { fireEvent, screen } from '@testing-library/react'

/**
 * A multi-select filter's field (`FilterMultiSelect`), by its label. Its list
 * of ticks, while open, carries the same label, so it is the button that is
 * wanted, not the first thing so labelled.
 */
export function filterField(label: string): HTMLElement {
  const field = screen.getAllByLabelText(label).find((el) => el.getAttribute('aria-haspopup') === 'true')
  if (!field) throw new Error(`No multi-select filter labelled ${label}`)
  return field
}

/**
 * Ticks choices in a multi-select filter as the supervisor does: opens the
 * field, then ticks each name, waiting for the choices to arrive. The list
 * stays open, as it does on screen.
 */
export async function tick(label: string, ...names: string[]) {
  const field = filterField(label)
  if (field.getAttribute('aria-expanded') !== 'true') fireEvent.click(field)
  for (const name of names) fireEvent.click(await screen.findByRole('checkbox', { name }))
}
