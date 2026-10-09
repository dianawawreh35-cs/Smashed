import { useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { asRequest, createWebsite, deleteWebsite, listWebsites, updateWebsite } from '../api/websites'
import type { UpsertWebsiteRequest, Website, WebsiteLogin } from '../api/websites'
import { errorCodeOf } from '../api/users'
import ConfirmButton from '../components/ConfirmButton'
import LoadError from '../components/LoadError'

/** The form as typed. The password box starts empty: an empty box keeps what is stored. */
interface Draft {
  nameAr: string
  nameEn: string
  url: string
  login: WebsiteLogin
  username: string
  password: string
  removePassword: boolean
  alertsWithSound: boolean
  cartUrl: string
  usernameSelector: string
  passwordSelector: string
  submitSelector: string
  isActive: boolean
}

const EMPTY: Draft = {
  nameAr: '',
  nameEn: '',
  url: 'https://',
  login: 'shared',
  username: '',
  password: '',
  removePassword: false,
  alertsWithSound: true,
  cartUrl: '',
  usernameSelector: '',
  passwordSelector: '',
  submitSelector: '',
  isActive: true,
}

function toDraft(w: Website): Draft {
  return {
    nameAr: w.nameAr,
    nameEn: w.nameEn,
    url: w.url,
    login: w.login,
    username: w.username ?? '',
    password: '',
    removePassword: false,
    alertsWithSound: w.alertsWithSound,
    cartUrl: w.cartUrl ?? '',
    usernameSelector: w.usernameSelector ?? '',
    passwordSelector: w.passwordSelector ?? '',
    submitSelector: w.submitSelector ?? '',
    isActive: w.isActive,
  }
}

const orNull = (v: string) => (v.trim() ? v.trim() : null)

function toRequest(d: Draft, sortOrder: number): UpsertWebsiteRequest {
  const shared = d.login === 'shared'
  return {
    nameAr: d.nameAr.trim(),
    nameEn: d.nameEn.trim(),
    url: d.url.trim(),
    login: d.login,
    username: shared ? orNull(d.username) : null,
    // Typed: the new one. Ticked: none. Neither: keep what is stored.
    password: !shared ? null : d.removePassword ? '' : d.password ? d.password : null,
    alertsWithSound: d.alertsWithSound,
    cartUrl: orNull(d.cartUrl),
    usernameSelector: orNull(d.usernameSelector),
    passwordSelector: orNull(d.passwordSelector),
    submitSelector: orNull(d.submitSelector),
    sortOrder,
    isActive: d.isActive,
  }
}

/**
 * The websites the agents work in, shown as tabs inside the Agent App (A-88).
 *
 * Each tab has an Arabic and an English name, and the app shows the one in the
 * agent's language. **The login** is either each agent's own, which the app
 * remembers for that agent (the POS), or one set here, which the app types in
 * by itself; the agents never see that password, and **neither does this
 * page**: it is told only whether one is stored, and an empty password box
 * keeps it. **Alerts with sound** keeps the tab awake so its ding is heard.
 * **The caller's cart** goes on one tab, the POS: answering a call opens it
 * there (A-85).
 *
 * The laptops pick up a change at their next sign-in. A tab is edited under
 * its own row, as the mistakes are, and Remove asks twice (M-W07).
 */
export default function WebsitesPage() {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<string | 'new' | null>(null)
  const [error, setError] = useState<string | null>(null)

  const query = useQuery({ queryKey: ['websites'], queryFn: listWebsites })
  const websites = query.data ?? []
  const arabic = i18n.language.startsWith('ar')

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['websites'] })
  const fail = (e: unknown) => setError(t(`websites.errors.${errorCodeOf(e)}`))

  /** Swaps a tab with its neighbour and renumbers what is out of step, as the channels do. */
  const move = useMutation({
    mutationFn: async ({ index, by }: { index: number; by: number }) => {
      const list = [...websites]
      const target = index + by
      if (target < 0 || target >= list.length) return
      ;[list[index], list[target]] = [list[target], list[index]]
      for (const [i, website] of list.entries()) {
        const sortOrder = i * 10
        if (website.sortOrder !== sortOrder) {
          await updateWebsite(website.id, { ...asRequest(website), sortOrder })
        }
      }
    },
    onSuccess: refresh,
    onError: fail,
  })

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold text-slate-100">{t('websites.heading')}</h2>
          <p className="field-hint max-w-3xl">{t('websites.intro')}</p>
        </div>
        <button
          type="button"
          className="btn-primary"
          onClick={() => {
            setError(null)
            setEditing('new')
          }}
          disabled={editing === 'new'}
        >
          {t('websites.add')}
        </button>
      </div>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      {editing === 'new' && (
        <WebsiteForm
          sortOrder={(websites.length + 1) * 10}
          onDone={() => {
            setEditing(null)
            refresh()
          }}
          onCancel={() => setEditing(null)}
        />
      )}

      {query.isError ? (
        <LoadError message={t('websites.failed')} onRetry={() => void query.refetch()} busy={query.isFetching} />
      ) : query.isPending ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : websites.length === 0 ? (
        <div className="card card-body text-center text-slate-300">{t('websites.empty')}</div>
      ) : (
        <div className="card overflow-x-auto">
          <table className="table">
            <thead>
              <tr>
                <th>{t('websites.name')}</th>
                <th>{t('websites.url')}</th>
                <th>{t('websites.login')}</th>
                <th>{t('websites.sound')}</th>
                <th>{t('websites.status')}</th>
                <th>{t('websites.order')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {websites.map((website, index) => (
                <WebsiteRow
                  key={website.id}
                  website={website}
                  arabic={arabic}
                  first={index === 0}
                  last={index === websites.length - 1}
                  moving={move.isPending}
                  onMove={(by) => {
                    setError(null)
                    move.mutate({ index, by })
                  }}
                  editing={editing === website.id}
                  onEdit={() => {
                    setError(null)
                    setEditing(editing === website.id ? null : website.id)
                  }}
                  onDone={() => {
                    setEditing(null)
                    refresh()
                  }}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function WebsiteRow({
  website,
  arabic,
  first,
  last,
  moving,
  onMove,
  editing,
  onEdit,
  onDone,
}: {
  website: Website
  arabic: boolean
  first: boolean
  last: boolean
  moving: boolean
  onMove: (by: number) => void
  editing: boolean
  onEdit: () => void
  onDone: () => void
}) {
  const { t } = useTranslation()
  const name = arabic ? website.nameAr : website.nameEn

  return (
    <>
      <tr>
        <td>
          <div className="font-medium text-slate-100">{name}</div>
          <div className="text-xs text-slate-400">{arabic ? website.nameEn : website.nameAr}</div>
          {website.cartUrl && <span className="badge badge-ok mt-1">{t('websites.cartBadge')}</span>}
        </td>
        <td className="max-w-[16rem] truncate" dir="ltr" title={website.url}>
          {website.url}
        </td>
        <td>
          {website.login === 'own' ? (
            <span className="text-sm text-slate-300">{t('websites.loginOwn')}</span>
          ) : (
            <div className="text-sm">
              <div dir="ltr" className="text-slate-200">
                {website.username}
              </div>
              <div className={website.hasPassword ? 'text-xs text-slate-400' : 'text-xs text-amber-400'}>
                {website.hasPassword ? t('websites.passwordStored') : t('websites.passwordMissing')}
              </div>
            </div>
          )}
        </td>
        <td className="text-sm text-slate-300">{website.alertsWithSound ? t('websites.alerts') : '—'}</td>
        <td>
          <span className={website.isActive ? 'badge badge-ok' : 'badge badge-muted'}>
            {website.isActive ? t('websites.shown') : t('websites.hidden')}
          </span>
        </td>
        <td className="whitespace-nowrap">
          <button
            type="button"
            onClick={() => onMove(-1)}
            disabled={first || moving}
            aria-label={`${t('websites.moveUp')} ${name}`}
            className="btn-ghost btn-sm"
          >
            ↑
          </button>
          <button
            type="button"
            onClick={() => onMove(1)}
            disabled={last || moving}
            aria-label={`${t('websites.moveDown')} ${name}`}
            className="btn-ghost btn-sm ms-1"
          >
            ↓
          </button>
        </td>
        <td className="text-end">
          <button type="button" className="btn-ghost btn-sm" onClick={onEdit}>
            {editing ? t('websites.close') : t('websites.edit')}
          </button>
        </td>
      </tr>
      {editing && (
        <tr>
          <td colSpan={7} className="row-panel">
            <WebsiteForm website={website} sortOrder={website.sortOrder} onDone={onDone} onCancel={onEdit} />
          </td>
        </tr>
      )}
    </>
  )
}

function WebsiteForm({
  website,
  sortOrder,
  onDone,
  onCancel,
}: {
  website?: Website
  sortOrder: number
  onDone: () => void
  onCancel: () => void
}) {
  const { t } = useTranslation()
  const [draft, setDraft] = useState<Draft>(website ? toDraft(website) : EMPTY)
  const [error, setError] = useState<string | null>(null)
  const set =
    <K extends keyof Draft>(key: K) =>
    (value: Draft[K]) =>
      setDraft((d) => ({ ...d, [key]: value }))

  const save = useMutation({
    meta: { toast: 'saved' },
    mutationFn: () =>
      website ? updateWebsite(website.id, toRequest(draft, sortOrder)) : createWebsite(toRequest(draft, sortOrder)),
    onSuccess: onDone,
    onError: (e) => setError(t(`websites.errors.${errorCodeOf(e)}`)),
  })

  const remove = useMutation({
    meta: { toast: 'deleted' },
    mutationFn: () => deleteWebsite(website!.id),
    onSuccess: onDone,
    onError: (e) => setError(t(`websites.errors.${errorCodeOf(e)}`)),
  })

  const shared = draft.login === 'shared'
  const missing =
    !draft.nameAr.trim() || !draft.nameEn.trim() || !draft.url.trim() || (shared && !draft.username.trim())

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    save.mutate()
  }

  return (
    <form
      onSubmit={onSubmit}
      className="card card-body space-y-4"
      aria-label={website ? t('websites.editHeading') : t('websites.newHeading')}
    >
      <h3 className="text-base font-semibold text-slate-100">
        {website ? t('websites.editHeading') : t('websites.newHeading')}
      </h3>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <label className="field">
          <span className="field-label">{t('websites.nameAr')}</span>
          <input className="input" dir="rtl" value={draft.nameAr} onChange={(e) => set('nameAr')(e.target.value)} />
        </label>
        <label className="field">
          <span className="field-label">{t('websites.nameEn')}</span>
          <input className="input" dir="ltr" value={draft.nameEn} onChange={(e) => set('nameEn')(e.target.value)} />
        </label>
        <div className="field sm:col-span-2">
          <label className="field">
            <span className="field-label">{t('websites.url')}</span>
            <input
              className="input"
              dir="ltr"
              inputMode="url"
              value={draft.url}
              onChange={(e) => set('url')(e.target.value)}
            />
          </label>
          <span className="field-hint">{t('websites.urlHint')}</span>
        </div>
      </div>

      <fieldset className="space-y-2">
        <legend className="field-label">{t('websites.login')}</legend>
        <label className="flex items-start gap-2 text-sm text-slate-200">
          <input
            type="radio"
            name="login"
            checked={draft.login === 'shared'}
            onChange={() => set('login')('shared')}
            className="mt-1 accent-brand-500"
          />
          <span>
            {t('websites.loginShared')}
            <span className="field-hint block">{t('websites.loginSharedHint')}</span>
          </span>
        </label>
        <label className="flex items-start gap-2 text-sm text-slate-200">
          <input
            type="radio"
            name="login"
            checked={draft.login === 'own'}
            onChange={() => set('login')('own')}
            className="mt-1 accent-brand-500"
          />
          <span>
            {t('websites.loginOwn')}
            <span className="field-hint block">{t('websites.loginOwnHint')}</span>
          </span>
        </label>
      </fieldset>

      {shared && (
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="field">
            <span className="field-label">{t('websites.username')}</span>
            <input
              className="input"
              dir="ltr"
              autoComplete="off"
              value={draft.username}
              onChange={(e) => set('username')(e.target.value)}
            />
          </label>
          <div className="field">
            <label className="field">
              <span className="field-label">{t('websites.password')}</span>
              <input
                type="password"
                className="input"
                dir="ltr"
                autoComplete="new-password"
                value={draft.password}
                disabled={draft.removePassword}
                placeholder={website?.hasPassword ? t('websites.passwordKeep') : ''}
                onChange={(e) => set('password')(e.target.value)}
              />
            </label>
            {website?.hasPassword && (
              <label className="mt-1 flex items-center gap-2 text-xs text-slate-400">
                <input
                  type="checkbox"
                  checked={draft.removePassword}
                  onChange={(e) => set('removePassword')(e.target.checked)}
                  className="accent-brand-500"
                />
                {t('websites.passwordRemove')}
              </label>
            )}
          </div>
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <label className="flex items-start gap-2 text-sm text-slate-200">
          <input
            type="checkbox"
            checked={draft.alertsWithSound}
            onChange={(e) => set('alertsWithSound')(e.target.checked)}
            className="mt-1 accent-brand-500"
          />
          <span>
            {t('websites.alertsLabel')}
            <span className="field-hint block">{t('websites.alertsHint')}</span>
          </span>
        </label>
        <label className="flex items-start gap-2 text-sm text-slate-200">
          <input
            type="checkbox"
            checked={draft.isActive}
            onChange={(e) => set('isActive')(e.target.checked)}
            className="mt-1 accent-brand-500"
          />
          <span>
            {t('websites.shownLabel')}
            <span className="field-hint block">{t('websites.shownHint')}</span>
          </span>
        </label>
      </div>

      <div className="field">
        <label className="field">
          <span className="field-label">{t('websites.cartUrl')}</span>
          <input
            className="input"
            dir="ltr"
            inputMode="url"
            value={draft.cartUrl}
            placeholder="https://…/cart/{number}"
            onChange={(e) => set('cartUrl')(e.target.value)}
          />
        </label>
        <span className="field-hint">{t('websites.cartHint')}</span>
      </div>

      {shared && (
        <details className="text-sm text-slate-300">
          <summary className="cursor-pointer">{t('websites.advanced')}</summary>
          <p className="field-hint mt-2">{t('websites.advancedHint')}</p>
          <div className="mt-2 grid gap-4 sm:grid-cols-3">
            <label className="field">
              <span className="field-label">{t('websites.usernameSelector')}</span>
              <input
                className="input"
                dir="ltr"
                value={draft.usernameSelector}
                onChange={(e) => set('usernameSelector')(e.target.value)}
              />
            </label>
            <label className="field">
              <span className="field-label">{t('websites.passwordSelector')}</span>
              <input
                className="input"
                dir="ltr"
                value={draft.passwordSelector}
                onChange={(e) => set('passwordSelector')(e.target.value)}
              />
            </label>
            <label className="field">
              <span className="field-label">{t('websites.submitSelector')}</span>
              <input
                className="input"
                dir="ltr"
                value={draft.submitSelector}
                onChange={(e) => set('submitSelector')(e.target.value)}
              />
            </label>
          </div>
        </details>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <button type="submit" className="btn-primary" disabled={missing || save.isPending}>
          {t('websites.save')}
        </button>
        <button type="button" className="btn-ghost" onClick={onCancel}>
          {t('websites.cancel')}
        </button>
        {website && (
          <span className="ms-auto">
            <ConfirmButton label={t('websites.remove')} onConfirm={() => remove.mutate()} disabled={remove.isPending} />
          </span>
        )}
      </div>
      <p className="field-hint">{t('websites.nextSignIn')}</p>
    </form>
  )
}
