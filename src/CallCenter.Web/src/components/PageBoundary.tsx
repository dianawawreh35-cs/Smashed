import { Component, Suspense } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import LoadError from './LoadError'

/**
 * Around the page in the layout: a line while a page's code is fetched, and a
 * failure with a Retry if it cannot be.
 *
 * The report pages and the dashboard are fetched on first opening rather than
 * with the rest (they carry the chart library, most of the app's weight). A
 * page left open across an update asks for a file the new version no longer
 * has, and without this boundary that would leave a blank screen. Retry
 * reloads, which brings the new version.
 */
export default function PageBoundary({ children }: { children: ReactNode }) {
  const { t } = useTranslation()
  return (
    <Boundary fallback={<LoadError onRetry={() => window.location.reload()} />}>
      <Suspense fallback={<p className="text-slate-400">{t('app.loading')}</p>}>{children}</Suspense>
    </Boundary>
  )
}

class Boundary extends Component<{ fallback: ReactNode; children: ReactNode }, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  render() {
    return this.state.failed ? this.props.fallback : this.props.children
  }
}
