import { useSyncExternalStore } from 'react'

/**
 * One clock for every running timer on a page, such as the phone badges'
 * "In a call · 2:14" (S-61).
 *
 * Each badge used to run its own one-second interval, so a Users page of
 * twenty agents re-rendered twenty times a second whether or not anyone was on
 * a call. Now there is one interval, running only while something that shows
 * a timer is on screen, and only those components re-render when it ticks.
 */

let now = Date.now()
let timer: number | undefined
const listeners = new Set<() => void>()

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  if (timer === undefined) {
    now = Date.now()
    timer = window.setInterval(() => {
      now = Date.now()
      listeners.forEach((l) => l())
    }, 1000)
  }
  return () => {
    listeners.delete(listener)
    if (listeners.size === 0 && timer !== undefined) {
      window.clearInterval(timer)
      timer = undefined
    }
  }
}

const noSubscription = () => () => {}
const read = () => now
const never = () => 0

/**
 * The time now, advancing once a second while `running`. When not running the
 * component is not subscribed at all, so a badge showing "Free" costs nothing.
 */
export function useClock(running: boolean): number {
  return useSyncExternalStore(running ? subscribe : noSubscription, running ? read : never)
}
