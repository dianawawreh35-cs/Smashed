import { useTranslation } from 'react-i18next'

/** Placeholder dashboard - metrics and Recharts panels arrive with Reports. */
export default function DashboardPage() {
  const { t } = useTranslation()

  return (
    <section className="space-y-2">
      <h2 className="text-xl font-semibold">{t('dashboard.heading')}</h2>
      <p className="text-slate-500">{t('dashboard.placeholder')}</p>
    </section>
  )
}
