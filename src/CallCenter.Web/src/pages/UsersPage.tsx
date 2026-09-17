import { useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  createUser,
  errorCodeOf,
  listUsers,
  resetPassword,
  setExtension,
  updateUser,
} from '../api/users'
import type { User } from '../api/users'

/**
 * Accounts and extensions (S-42).
 *
 * One extension per agent (SRS 2.3). Its SIP secret is write-only: the screen
 * can set one and replace one, and shows whether one is set, but never displays
 * it — there is no endpoint that returns it (N-05).
 */
export default function UsersPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)

  const { data: users, isLoading } = useQuery({ queryKey: ['users'], queryFn: listUsers })

  const refresh = () => {
    setError(null)
    return queryClient.invalidateQueries({ queryKey: ['users'] })
  }
  const onError = (e: unknown) => setError(t(`users.errors.${errorCodeOf(e)}`))

  const create = useMutation({ mutationFn: createUser, onSuccess: refresh, onError })
  const toggle = useMutation({
    mutationFn: (user: User) => updateUser(user.id, user.displayName, !user.isActive),
    onSuccess: refresh,
    onError,
  })

  if (isLoading) return <p className="text-slate-500">{t('app.loading')}</p>

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold">{t('users.heading')}</h2>

      {error && (
        <p role="alert" className="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
          {error}
        </p>
      )}

      <CreateUserForm onSubmit={(request) => create.mutate(request)} busy={create.isPending} />

      <div className="overflow-x-auto rounded border border-slate-200 bg-white">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-slate-600">
            <tr>
              <th className="px-3 py-2 text-start">{t('users.name')}</th>
              <th className="px-3 py-2 text-start">{t('users.login')}</th>
              <th className="px-3 py-2 text-start">{t('users.role')}</th>
              <th className="px-3 py-2 text-start">{t('users.extension')}</th>
              <th className="px-3 py-2 text-start">{t('users.status')}</th>
              <th className="px-3 py-2" />
            </tr>
          </thead>
          <tbody>
            {users?.map((user) => (
              <UserRow
                key={user.id}
                user={user}
                onToggle={() => toggle.mutate(user)}
                onChanged={refresh}
                onError={onError}
              />
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function UserRow({
  user,
  onToggle,
  onChanged,
  onError,
}: {
  user: User
  onToggle: () => void
  onChanged: () => void
  onError: (e: unknown) => void
}) {
  const { t } = useTranslation()
  const [panel, setPanel] = useState<'none' | 'extensions' | 'password'>('none')

  const close = () => {
    setPanel('none')
    onChanged()
  }

  return (
    <>
      <tr className="border-t border-slate-100">
        <td className="px-3 py-2">{user.displayName}</td>
        <td className="px-3 py-2 text-slate-500">{user.login}</td>
        <td className="px-3 py-2">{t(`users.roles.${user.role}`)}</td>
        <td className="px-3 py-2">
          {user.extension ? (
            <span>
              {user.extension}
              {/* A number without a secret cannot register, so say so plainly. */}
              {!user.hasSipCredentials && (
                <span className="ms-2 text-amber-600">{t('users.noSecret')}</span>
              )}
            </span>
          ) : (
            <span className="text-slate-400">{t('users.noExtension')}</span>
          )}
        </td>
        <td className="px-3 py-2">
          {user.isActive ? (
            <span className="text-emerald-700">{t('users.active')}</span>
          ) : (
            <span className="text-slate-400">{t('users.disabled')}</span>
          )}
        </td>
        <td className="px-3 py-2 text-end whitespace-nowrap">
          {user.role === 'Agent' && (
            <button
              type="button"
              className="rounded border border-slate-300 px-2 py-1 text-xs hover:bg-slate-50"
              onClick={() => setPanel(panel === 'extensions' ? 'none' : 'extensions')}
            >
              {t('users.setExtension')}
            </button>
          )}
          <button
            type="button"
            className="ms-2 rounded border border-slate-300 px-2 py-1 text-xs hover:bg-slate-50"
            onClick={() => setPanel(panel === 'password' ? 'none' : 'password')}
          >
            {t('users.resetPassword')}
          </button>
          <button
            type="button"
            className="ms-2 rounded border border-slate-300 px-2 py-1 text-xs hover:bg-slate-50"
            onClick={onToggle}
          >
            {user.isActive ? t('users.disable') : t('users.enable')}
          </button>
        </td>
      </tr>

      {panel !== 'none' && (
        <tr className="border-t border-slate-100 bg-slate-50">
          <td colSpan={6} className="px-3 py-3">
            {panel === 'extensions' ? (
              <ExtensionForm user={user} onDone={close} onError={onError} />
            ) : (
              <PasswordForm user={user} onDone={close} onError={onError} />
            )}
          </td>
        </tr>
      )}
    </>
  )
}

function CreateUserForm({
  onSubmit,
  busy,
}: {
  onSubmit: (request: { login: string; displayName: string; role: string; password: string }) => void
  busy: boolean
}) {
  const { t } = useTranslation()
  const [login, setLogin] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [role, setRole] = useState('Agent')
  const [password, setPassword] = useState('')

  function submit(event: FormEvent) {
    event.preventDefault()
    onSubmit({ login, displayName, role, password })
    setLogin('')
    setDisplayName('')
    setPassword('')
  }

  return (
    <form onSubmit={submit} className="rounded border border-slate-200 bg-white p-4 space-y-3">
      <h3 className="font-medium">{t('users.addHeading')}</h3>
      <div className="grid gap-3 sm:grid-cols-4">
        <Field label={t('users.name')} value={displayName} onChange={setDisplayName} />
        <Field label={t('users.login')} value={login} onChange={setLogin} />
        <label className="block space-y-1">
          <span className="text-sm text-slate-600">{t('users.role')}</span>
          <select
            value={role}
            onChange={(e) => setRole(e.target.value)}
            className="w-full rounded border border-slate-300 px-3 py-2"
          >
            <option value="Agent">{t('users.roles.Agent')}</option>
            <option value="Supervisor">{t('users.roles.Supervisor')}</option>
          </select>
        </label>
        <Field label={t('users.password')} value={password} onChange={setPassword} type="password" />
      </div>
      <button
        type="submit"
        disabled={busy || !login.trim() || !displayName.trim() || password.length < 8}
        className="rounded bg-brand-600 px-3 py-2 text-white disabled:opacity-50"
      >
        {t('users.add')}
      </button>
      <p className="text-xs text-slate-500">{t('users.passwordHint')}</p>
    </form>
  )
}

function ExtensionForm({
  user,
  onDone,
  onError,
}: {
  user: User
  onDone: () => void
  onError: (e: unknown) => void
}) {
  const { t } = useTranslation()
  const [extension, setExt] = useState(user.extension ?? '')
  const [secret, setSecret] = useState('')

  const save = useMutation({
    mutationFn: () => setExtension(user.id, { extension, secret }),
    onSuccess: onDone,
    onError,
  })

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
      className="space-y-3"
    >
      <p className="text-sm text-slate-600">{t('users.extensionHint')}</p>
      <div className="grid gap-3 sm:grid-cols-2 max-w-lg">
        <Field label={t('users.extension')} value={extension} onChange={setExt} />
        <Field
          label={t('users.secret')}
          value={secret}
          onChange={setSecret}
          type="password"
          placeholder={user.hasSipCredentials ? t('users.secretSet') : undefined}
        />
      </div>
      <button
        type="submit"
        disabled={save.isPending || !extension.trim()}
        className="rounded bg-brand-600 px-3 py-2 text-white disabled:opacity-50"
      >
        {t('users.save')}
      </button>
    </form>
  )
}

function PasswordForm({
  user,
  onDone,
  onError,
}: {
  user: User
  onDone: () => void
  onError: (e: unknown) => void
}) {
  const { t } = useTranslation()
  const [newPassword, setNewPassword] = useState('')

  const save = useMutation({
    mutationFn: () => resetPassword(user.id, newPassword),
    onSuccess: onDone,
    onError,
  })

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
      className="space-y-3"
    >
      <div className="max-w-xs">
        <Field label={t('users.newPassword')} value={newPassword} onChange={setNewPassword} type="password" />
      </div>
      <button
        type="submit"
        disabled={save.isPending || newPassword.length < 8}
        className="rounded bg-brand-600 px-3 py-2 text-white disabled:opacity-50"
      >
        {t('users.save')}
      </button>
      <p className="text-xs text-slate-500">{t('users.passwordHint')}</p>
    </form>
  )
}

function Field({
  label,
  value,
  onChange,
  type = 'text',
  placeholder,
}: {
  label: string
  value: string
  onChange: (value: string) => void
  type?: string
  placeholder?: string
}) {
  return (
    <label className="block space-y-1">
      <span className="text-sm text-slate-600">{label}</span>
      <input
        type={type}
        value={value}
        placeholder={placeholder}
        autoComplete="off"
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded border border-slate-300 px-3 py-2"
      />
    </label>
  )
}
