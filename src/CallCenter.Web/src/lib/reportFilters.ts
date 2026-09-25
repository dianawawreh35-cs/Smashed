import { useState } from 'react'
import type { ReportFilters } from '../api/applicationReports'
import { startOfDay, startOfNextDay } from '../api/calls'

/**
 * The state behind S-07's filter bar (`components/ReportFilters`): the days
 * and ids as chosen, and the filters they mean when sent.
 */

export type Preset = 'today' | 'week' | 'month' | 'custom'

/** The filters as chosen: days, not instants, until they are sent. */
export interface ReportDraft {
  preset: Preset
  from: string
  to: string
  agentId: string
  branchId: string
  channelId: string
  typeId: string
}

/** A date as the date input writes it, yyyy-mm-dd, in the browser's own time zone. */
export function localDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/**
 * The days a preset names, ending today. The week starts on Monday, as the
 * server's weekly buckets do, so "this week" and the week rows agree.
 */
export function presetRange(preset: Preset, today = new Date()): { from: string; to: string } {
  const to = localDate(today)
  if (preset === 'week') {
    const monday = new Date(today)
    monday.setDate(today.getDate() - ((today.getDay() + 6) % 7))
    return { from: localDate(monday), to }
  }
  if (preset === 'month') {
    return { from: localDate(new Date(today.getFullYear(), today.getMonth(), 1)), to }
  }
  return { from: to, to }
}

export function toReportFilters(d: ReportDraft): ReportFilters {
  const id = (v: string) => (v ? v : undefined)
  return {
    // Days in the supervisor's own time zone, sent as instants: from the start
    // of the first day to the start of the day after the last.
    from: d.from ? startOfDay(d.from) : undefined,
    to: d.to ? startOfNextDay(d.to) : undefined,
    agentId: id(d.agentId),
    branchId: id(d.branchId),
    channelId: id(d.channelId),
    typeId: id(d.typeId),
  }
}

/** The draft, the filters it means, and the setters the bar needs. */
export function useReportFilters(initial: Preset = 'week') {
  const [draft, setDraft] = useState<ReportDraft>(() => ({
    preset: initial,
    ...presetRange(initial),
    agentId: '', branchId: '', channelId: '', typeId: '',
  }))

  const set = <K extends keyof ReportDraft>(key: K) => (value: ReportDraft[K]) =>
    setDraft((d) => ({ ...d, [key]: value }))

  function choosePreset(preset: Preset) {
    setDraft((d) => (preset === 'custom' ? { ...d, preset } : { ...d, preset, ...presetRange(preset) }))
  }

  return { draft, filters: toReportFilters(draft), set, choosePreset }
}
