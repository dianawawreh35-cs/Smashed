import { useTranslation } from 'react-i18next'

/** Placeholder sign-in screen - no authentication wired up yet. */
export default function LoginPage() {
  const { t } = useTranslation()

  return (
    <div className="min-h-screen grid place-items-center px-4">
      <form
        className="w-full max-w-sm space-y-4 rounded-lg bg-white p-6 shadow-sm border border-slate-200"
        onSubmit={(event) => event.preventDefault()}
      >
        <h1 className="text-xl font-semibold">{t('login.heading')}</h1>

        <label className="block space-y-1">
          <span className="text-sm text-slate-600">{t('login.username')}</span>
          <input type="text" autoComplete="username" disabled
                 className="w-full rounded border border-slate-300 px-3 py-2 disabled:bg-slate-100" />
        </label>

        <label className="block space-y-1">
          <span className="text-sm text-slate-600">{t('login.password')}</span>
          <input type="password" autoComplete="current-password" disabled
                 className="w-full rounded border border-slate-300 px-3 py-2 disabled:bg-slate-100" />
        </label>

        <button type="submit" disabled
                className="w-full rounded bg-brand-600 px-3 py-2 text-white disabled:opacity-50">
          {t('login.submit')}
        </button>
      </form>
    </div>
  )
}
