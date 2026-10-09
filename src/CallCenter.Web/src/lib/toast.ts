import { useSyncExternalStore } from 'react'

/**
 * The short "Saved." / "Deleted." line in the corner of the screen (S-72),
 * shown when an editor closes on success and the change would otherwise
 * happen without a word.
 *
 * A save asks for one through its mutation's `meta` (`{ toast: 'saved' }`),
 * which the app's query client reads (lib/queryClient), so a page adds one
 * word to its mutation rather than its own state and timer. Failures are not
 * shown here: they stay inline beside the form, with what was typed.
 */

export type ToastKind = 'saved' | 'deleted'

export interface Toast {
  id: number
  kind: ToastKind
}

/** How long a notice stays, long enough to read twice. */
export const TOAST_MS = 4_000

let toasts: Toast[] = []
let nextId = 1
const listeners = new Set<() => void>()

function emit() {
  listeners.forEach((listener) => listener())
}

export function showToast(kind: ToastKind): void {
  const toast = { id: nextId++, kind }
  // Three at most: a burst of saves should not stack up the screen.
  toasts = [...toasts.slice(-2), toast]
  emit()
  setTimeout(() => dismissToast(toast.id), TOAST_MS)
}

export function dismissToast(id: number): void {
  if (!toasts.some((t) => t.id === id)) return
  toasts = toasts.filter((t) => t.id !== id)
  emit()
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useToasts(): Toast[] {
  return useSyncExternalStore(subscribe, () => toasts, () => toasts)
}

declare module '@tanstack/react-query' {
  interface Register {
    mutationMeta: { toast?: ToastKind }
  }
}
