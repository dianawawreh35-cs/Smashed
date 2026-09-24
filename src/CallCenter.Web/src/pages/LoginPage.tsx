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
  const { user, isLoading, signIn, signedOutByServer } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  // Opens with the reason when the server, not the supervisor, ended the
  // last sign-in: otherwise the login page would appear from nowhere.
  const [errorCode, setErrorCode] = useState<LoginErrorCode | null>(
    signedOutByServer ? LoginErrorCodes.SignedOut : null,
  )
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
    <div className="grid min-h-screen place-items-center bg-ink-950 px-4 py-10">
      <div className="w-full max-w-sm space-y-4">
        <div className="flex items-center justify-between">
          {/* The mark, so the sign-in screen is recognisably the same product
              as the one behind it. */}
          <span className="flex items-center gap-2">
            <span
              className="grid h-9 w-9 place-items-center rounded-lg bg-brand-50 font-bold text-brand-500"
              aria-hidden="true"
            >
              S
            </span>
            <span className="text-sm font-semibold text-slate-100">{t('app.title')}</span>
          </span>

          <LanguageSwitcher />
        </div>

        <form onSubmit={onSubmit} className="card card-body space-y-5">
          <div className="space-y-1">
            <h1 className="text-xl font-semibold text-slate-100">{t('login.heading')}</h1>
            <p className="text-sm text-slate-400">{t('app.subtitle')}</p>
          </div>

          <label className="field">
            <span className="field-label">{t('login.username')}</span>
            <input
              type="text"
              autoComplete="username"
              autoFocus
              value={login}
              disabled={isBusy}
              onChange={(e) => setLogin(e.target.value)}
              className={`input ${errorCode ? 'input-invalid' : ''}`}
            />
          </label>

          <label className="field">
            <span className="field-label">{t('login.password')}</span>
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              disabled={isBusy}
              onChange={(e) => setPassword(e.target.value)}
              className={`input ${errorCode ? 'input-invalid' : ''}`}
            />
          </label>

          {/* Wrong password, disabled account, an agent in the wrong app, or the
              server being down: one place, so it is always looked for here. */}
          {errorCode && (
            <p role="alert" className="notice-error">
              {t(`login.errors.${errorCode}`)}
            </p>
          )}

          <button type="submit" disabled={isBusy || !login.trim()} className="btn-primary w-full">
            {isBusy ? t('login.signingIn') : t('login.submit')}
          </button>
        </form>
      </div>
    </div>
  )
}
