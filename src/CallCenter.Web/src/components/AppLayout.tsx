import { Outlet } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import LanguageSwitcher from './LanguageSwitcher'

/** Shell around the authenticated supervisor pages. */
export default function AppLayout() {
  const { t } = useTranslation()

  return (
    <div className="min-h-screen flex flex-col">
      <header className="bg-white border-b border-slate-200">
        <div className="mx-auto max-w-7xl px-4 py-3 flex items-center justify-between gap-4">
          <div>
            <h1 className="text-lg font-semibold">{t('app.title')}</h1>
            <p className="text-xs text-slate-500">{t('app.subtitle')}</p>
          </div>
          <LanguageSwitcher />
        </div>
      </header>

      <main className="flex-1 mx-auto w-full max-w-7xl px-4 py-6">
        <Outlet />
      </main>
    </div>
  )
}
