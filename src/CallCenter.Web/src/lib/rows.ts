import type { MouseEvent } from 'react'

/**
 * For a table row that opens on double-click: stops the double-click from also
 * selecting the word under the pointer.
 *
 * Without it every double-click painted a word blue at the same instant the row
 * opened, which is most of what made opening feel glitchy (24 Sep). Only the
 * second press is stopped, so text in the row can still be selected by
 * dragging, and copied.
 */
export function noSelectOnDoubleClick(event: MouseEvent) {
  if (event.detail > 1) event.preventDefault()
}
