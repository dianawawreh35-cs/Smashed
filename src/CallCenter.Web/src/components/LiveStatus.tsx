import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { getQueue } from '../api/pbxQueue'
import { getAgentPhones } from '../api/pbxAgents'
import { breakMonitor } from '../api/breaks'

/** How often the header asks: the pages that show the detail ask more often, and share the answer. */
export const STATUS_REFRESH_MS = 30_000

/**
 * The three things a supervisor glances for, in the header of every page
 * (S-71): whether the queue is open (S-60), how many agents are on a call
 * (S-61), and how many are on a break (S-66). Each one links to the page that
 * shows the detail.
 *
 * Each uses the same query as its page (the queue card, the Users page, the
 * Breaks page), so opening that page shows the same figure, and the header
 * follows a switch made there at once. A figure that cannot be had is left
 * out rather than shown as an error: the page it links to says why.
 */
export default function LiveStatus() {
  const { t, i18n } = useTranslation()
  const queue = useQuery({ queryKey: ['pbx', 'queue'], queryFn: getQueue, refetchInterval: STATUS_REFRESH_MS })
  const phones = useQuery({ queryKey: ['agentPhones'], queryFn: getAgentPhones, refetchInterval: STATUS_REFRESH_MS })
  const breaks = useQuery({ queryKey: ['breaks', 'monitor'], queryFn: breakMonitor, refetchInterval: STATUS_REFRESH_MS })

  const n = (value: number) => value.toLocaleString(i18n.language)
  const isOpen = queue.data?.isOpen
  // This sits outside the page's error boundary, so an answer of the wrong
  // shape (a server older or newer than this page) must leave a figure out,
  // never take the whole layout down with it.
  const agentsOnCall = phones.data?.live && Array.isArray(phones.data.agents) ? phones.data.agents : null
  const agentsOnBreak = Array.isArray(breaks.data?.agents) ? breaks.data.agents : null
  // Only while the server is hearing from the PBX: otherwise "0" would be a guess.
  const onCall = agentsOnCall ? agentsOnCall.filter((a) => a.state === 'InCall').length : null
  const onBreak = agentsOnBreak ? agentsOnBreak.filter((a) => a.state === 'OnBreak').length : null

  return (
    <ul aria-label={t('header.status')} className="hidden items-center gap-2 text-xs md:flex">
      {queue.data && typeof queue.data === 'object' && (
        <li>
          <Link to="/dashboard" className={`status-pill ${isOpen === true ? 'status-pill-ok' : isOpen === false ? 'status-pill-off' : ''}`}>
            <span className="status-dot" aria-hidden="true" />
            {isOpen === true ? t('header.queueOpen') : isOpen === false ? t('header.queueClosed') : t('header.queueUnknown')}
          </Link>
        </li>
      )}
      {onCall !== null && (
        <li>
          <Link to="/users" className={`status-pill ${onCall > 0 ? 'status-pill-call' : ''}`}>
            {t('header.onCall')} <span className="tabular font-semibold" dir="ltr">{n(onCall)}</span>
          </Link>
        </li>
      )}
      {onBreak !== null && (
        <li>
          <Link to="/breaks" className={`status-pill ${onBreak > 0 ? 'status-pill-warn' : ''}`}>
            {t('header.onBreak')} <span className="tabular font-semibold" dir="ltr">{n(onBreak)}</span>
          </Link>
        </li>
      )}
    </ul>
  )
}
