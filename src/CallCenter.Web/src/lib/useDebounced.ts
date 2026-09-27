import { useEffect, useState } from 'react'

/**
 * How long a search box waits after the last key before it asks the server
 * (M-W09). Long enough that typing a number sends one request, not one per
 * digit; short enough that the list still seems to follow the typing.
 */
export const SEARCH_DEBOUNCE_MS = 250

/** The value, once it has stopped changing for `delay` milliseconds. */
export function useDebounced<T>(value: T, delay = SEARCH_DEBOUNCE_MS): T {
  const [settled, setSettled] = useState(value)

  useEffect(() => {
    const timer = window.setTimeout(() => setSettled(value), delay)
    return () => window.clearTimeout(timer)
  }, [value, delay])

  return settled
}
