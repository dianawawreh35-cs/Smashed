import { useState } from 'react'
import type { FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { LoginError, LoginErrorCodes } from '../api/auth'
import type { LoginErrorCode } from '../api/auth'
import { useAuth } from '../auth/context'
import LanguageSwitcher from '../components/LanguageSwitcher'

/** Supervisor sign-in (S-01). Browser on the LAN; agents use the desktop app. */
export default function LoginPage() {
  const { t } = useTranslation()
  const { user, isLoading, signIn } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [errorCode, setErrorCode] = useState<LoginErrorCode | null>(null)
  const [isBusy, setIsBusy] = useState(false)

  // Already signed in - go where they were headed, or the dashboard.
  if (!isLoading && user) {
    const from = (location.state as { from?: string } | null)?.from
    return <Navigate to={from ?? '/dashboard'} replace />
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setIsBusy(true)
    setErrorCode(null)

    try {
      await signIn(login, password)
      // Nothing keeps the password once it has been used.
      setPassword('')
      const from = (location.state as { from?: string } | null)?.from
      navigate(from ?? '/dashboard', { replace: true })
    } catch (error) {
      setErrorCode(error instanceof LoginError ? error.code : LoginErrorCodes.ServerError)
    } finally {
      setIsBusy(false)
    }
  }

  return (
    <div className="min-h-screen grid place-items-center px-4">
      <div className="w-full max-w-sm space-y-3">
        <div className="flex justify-end">
          <LanguageSwitcher />
        </div>

        <form
          className="space-y-4 rounded-lg bg-white p-6 shadow-sm border border-slate-200"
          onSubmit={onSubmit}
        >
          <div>
            <h1 className="text-xl font-semibold">{t('login.heading')}</h1>
            <p className="text-xs text-slate-500">{t('app.subtitle')}</p>
          </div>

          <label className="block space-y-1">
            <span className="text-sm text-slate-600">{t('login.username')}</span>
            <input
              type="text"
              autoComplete="username"
              autoFocus
              value={login}
              disabled={isBusy}
              onChange={(e) => setLogin(e.target.value)}
              className="w-full rounded border border-slate-300 px-3 py-2 disabled:bg-slate-100"
            />
          </label>

          <label className="block space-y-1">
            <span className="text-sm text-slate-600">{t('login.password')}</span>
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              disabled={isBusy}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full rounded border border-slate-300 px-3 py-2 disabled:bg-slate-100"
            />
          </label>

          {/* Wrong password, disabled account, an agent in the wrong app, or the
              server being down: one place, so it is always looked for here. */}
          {errorCode && (
            <p role="alert" className="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
              {t(`login.errors.${errorCode}`)}
            </p>
          )}

          <button
            type="submit"
            disabled={isBusy || !login.trim()}
            className="w-full rounded bg-brand-600 px-3 py-2 text-white disabled:opacity-50"
          >
            {isBusy ? t('login.signingIn') : t('login.submit')}
          </button>
        </form>
      </div>
    </div>
  )
}
