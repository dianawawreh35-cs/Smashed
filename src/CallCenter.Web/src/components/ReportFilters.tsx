import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { listChannels } from '../api/channels'
import { listClassificationTypes } from '../api/classifications'
import { listBranches } from '../api/delivery'
import { listUsers } from '../api/users'
import { FilterSelect as Select } from './SearchControls'
import type { Preset, ReportDraft } from '../lib/reportFilters'

/**
 * S-07's common filters — period, agent, branch, channel, type — as one bar,
 * shared by the application reports, the call reports and the dashboard so
 * they filter the same way. The filters apply as they are changed: they are
 * dates and drop-downs, nothing is typed, and each report is a handful of
 * rows. The state behind it is `useReportFilters` (lib/reportFilters).
 */

const PRESETS: Preset[] = ['today', 'week', 'month', 'custom']

export function ReportFilterBar({
  draft, set, choosePreset, includePhone = false, channels: showChannels = true, periodOnly = false,
}: {
  draft: ReportDraft
  set: <K extends keyof ReportDraft>(key: K) => (value: ReportDraft[K]) => void
  choosePreset: (preset: Preset) => void
  /** The call reports offer Phone too; the application reports are messages only, and Phone would always be empty. */
  includePhone?: boolean
  channels?: boolean
  /** The dashboard's charts take a period and nothing else (S-20). */
  periodOnly?: boolean
}) {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')

  const agents = useQuery({ queryKey: ['users'], queryFn: listUsers, enabled: !periodOnly })
  const branches = useQuery({ queryKey: ['branches'], queryFn: listBranches, enabled: !periodOnly })
  const channels = useQuery({ queryKey: ['channels', 'all'], queryFn: () => listChannels(true), enabled: showChannels && !periodOnly })
  const types = useQuery({ queryKey: ['classification', 'types'], queryFn: listClassificationTypes, enabled: !periodOnly })

  return (
    <form className="card card-body space-y-4" aria-label={t('applicationReports.filters')} onSubmit={(e) => e.preventDefault()}>
      <div className="flex flex-wrap items-center gap-2" role="group" aria-label={t('applicationReports.period')}>
        <span className="field-label me-2">{t('applicationReports.period')}</span>
        {PRESETS.map((preset) => (
          <button
            key={preset}
            type="button"
            aria-pressed={draft.preset === preset}
            className={draft.preset === preset ? 'btn-primary btn-sm' : 'btn-ghost btn-sm'}
            onClick={() => choosePreset(preset)}
          >
            {t(`applicationReports.presets.${preset}`)}
          </button>
        ))}
      </div>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <label className="field">
          <span className="field-label">{t('applicationReports.from')}</span>
          <input type="date" className="input" value={draft.from} disabled={draft.preset !== 'custom'}
            onChange={(e) => set('from')(e.target.value)} />
        </label>
        <label className="field">
          <span className="field-label">{t('applicationReports.to')}</span>
          <input type="date" className="input" value={draft.to} disabled={draft.preset !== 'custom'}
            onChange={(e) => set('to')(e.target.value)} />
        </label>
        {!periodOnly && showChannels && (
          <Select label={t('applicationReports.columns.channel')} value={draft.channelId} onChange={set('channelId')}>
            {(channels.data ?? []).filter((ch) => includePhone || !ch.isSystem).map((ch) => (
              <option key={ch.id} value={ch.id}>{ch.name}</option>
            ))}
          </Select>
        )}
        {!periodOnly && (
          <>
            <Select label={t('calls.columns.agent')} value={draft.agentId} onChange={set('agentId')}>
              {(agents.data ?? []).filter((u) => u.role === 'Agent').map((u) => (
                <option key={u.id} value={u.id}>{u.displayName}</option>
              ))}
            </Select>
            <Select label={t('calls.columns.branch')} value={draft.branchId} onChange={set('branchId')}>
              {(branches.data ?? []).map((b) => (
                <option key={b.id} value={b.id}>{b.name}</option>
              ))}
            </Select>
            <Select label={t('calls.columns.type')} value={draft.typeId} onChange={set('typeId')}>
              {(types.data ?? []).map((ty) => (
                <option key={ty.id} value={ty.id}>{arabic ? ty.labelAr : ty.labelEn}</option>
              ))}
            </Select>
          </>
        )}
      </div>
    </form>
  )
}

/** One report's own grouping (S-07's per day / week / month, and the others a report allows). */
export function Grouping<T extends string>({
  label, value, options, onChange, labelFor,
}: {
  label: string
  value: T
  options: T[]
  onChange: (value: T) => void
  /** The words for an option that is not a grouping, such as ranking by orders or by value. */
  labelFor?: (option: T) => string
}) {
  const { t } = useTranslation()
  return (
    <label className="flex items-center gap-2 text-sm text-slate-400">
      {label}
      <select className="input w-auto py-1" value={value} onChange={(e) => onChange(e.target.value as T)}>
        {options.map((option) => (
          <option key={option} value={option}>{labelFor ? labelFor(option) : t(`applicationReports.groups.${option}`)}</option>
        ))}
      </select>
    </label>
  )
}

/**
 * What a printed report covers, at the top of the page (26 Sep): the page, the
 * tab, the period and every filter chosen, by name, and when it was printed.
 * Hidden on screen, where the filter bar says the same; on paper the bar is
 * gone, and a report that does not say what it covers is a number without a
 * question. The names come from the same lists the bar uses, already fetched.
 */
export function ReportPrintHeading({
  title, section, draft, periodOnly = false,
}: {
  title: string
  /** The tab, on a page that has them. */
  section?: string
  draft: ReportDraft
  periodOnly?: boolean
}) {
  const { t, i18n } = useTranslation()
  const arabic = i18n.language.startsWith('ar')
  const agents = useQuery({ queryKey: ['users'], queryFn: listUsers, enabled: !periodOnly && !!draft.agentId })
  const branches = useQuery({ queryKey: ['branches'], queryFn: listBranches, enabled: !periodOnly && !!draft.branchId })
  const channels = useQuery({ queryKey: ['channels', 'all'], queryFn: () => listChannels(true), enabled: !periodOnly && !!draft.channelId })
  const types = useQuery({ queryKey: ['classification', 'types'], queryFn: listClassificationTypes, enabled: !periodOnly && !!draft.typeId })

  const day = (value: string) => (value ? new Date(`${value}T00:00:00`).toLocaleDateString(i18n.language) : '…')
  const period = draft.from === draft.to ? day(draft.from) : `${day(draft.from)} – ${day(draft.to)}`

  const chosen: [string, string | undefined][] = periodOnly ? [] : [
    [t('calls.columns.agent'), draft.agentId && agents.data?.find((u) => u.id === draft.agentId)?.displayName],
    [t('calls.columns.branch'), draft.branchId && branches.data?.find((b) => b.id === draft.branchId)?.name],
    [t('applicationReports.columns.channel'), draft.channelId && channels.data?.find((c) => c.id === draft.channelId)?.name],
    [t('calls.columns.type'), draft.typeId && (() => {
      const type = types.data?.find((ty) => ty.id === draft.typeId)
      return type && (arabic ? type.labelAr : type.labelEn)
    })()],
  ]
  const filters = chosen
    .filter(([, value]) => value)
    .map(([label, value]) => `${label}: ${value}`)

  // aria-hidden: on screen it is hidden and the page's own heading says the
  // same, and a second heading of the same name confuses a screen reader.
  return (
    <div className="print-only mb-4 border-b pb-3" aria-hidden="true" data-testid="print-heading">
      <h1 className="text-xl font-semibold">{section ? `${title} — ${section}` : title}</h1>
      <p className="text-sm">
        {t('applicationReports.period')}: <span dir="ltr">{period}</span>
        {filters.length > 0 ? ` · ${filters.join(' · ')}` : ` · ${t('callReports.printAll')}`}
      </p>
      <p className="text-xs">
        {t('callReports.printedAt', { at: new Date().toLocaleString(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }) })}
      </p>
    </div>
  )
}

/** Prints the page as it stands: every report on the open tab, under the printed heading. */
export function PrintPageButton({ onPrint }: { onPrint: () => void }) {
  const { t } = useTranslation()
  return (
    <button type="button" className="btn-ghost btn-sm shrink-0" onClick={onPrint}>
      {t('callReports.printPage')}
    </button>
  )
}
