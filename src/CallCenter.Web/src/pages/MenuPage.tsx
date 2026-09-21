import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  createMenuItem,
  deleteMenuItem,
  listMenuCategories,
  menuImageUrl,
  searchMenu,
  setMenuImage,
  updateMenuItem,
} from '../api/menu'
import type { MenuItem } from '../api/menu'
import { errorCodeOf } from '../api/users'

/**
 * The menu (S-59).
 *
 * The price field is the careful part. The printed menu uses three shapes that
 * mean different things — a price, no price, and an amount added to something
 * else — and the form has to let the supervisor say which without a manual.
 */
export default function MenuPage() {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [editing, setEditing] = useState<MenuItem | 'new' | null>(null)

  const { data: categories } = useQuery({
    queryKey: ['menu-categories'],
    queryFn: listMenuCategories,
  })

  const { data: items, isLoading } = useQuery({
    queryKey: ['menu', query, categoryId],
    queryFn: () => searchMenu(query, categoryId || undefined),
  })

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h2 className="page-title">{t('menu.heading')}</h2>
          <p className="page-subtitle">{t('menu.intro')}</p>
        </div>
        <button type="button" onClick={() => setEditing('new')} className="btn-primary">
          {t('menu.add')}
        </button>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <input
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={t('menu.searchPlaceholder')}
          aria-label={t('menu.search')}
          className="input max-w-md"
        />

        <select
          value={categoryId}
          onChange={(e) => setCategoryId(e.target.value)}
          aria-label={t('menu.category')}
          className="input max-w-xs"
        >
          <option value="">{t('menu.allCategories')}</option>
          {categories?.map((category) => (
            <option key={category.id} value={category.id}>
              {category.name} ({category.itemCount})
            </option>
          ))}
        </select>

        {items && <span className="text-sm text-slate-400">{t('menu.count', { count: items.length })}</span>}
      </div>

      {editing && (
        <ItemForm item={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />
      )}

      {isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : items && items.length > 0 ? (
        <ItemTable items={items} onEdit={setEditing} />
      ) : (
        <div className="card card-body text-center">
          <p className="text-slate-300">{query ? t('menu.noMatches') : t('menu.empty')}</p>
        </div>
      )}
    </div>
  )
}

/** What to show in the price column — the three shapes, told apart. */
function priceLabel(item: MenuItem, t: (key: string) => string): string {
  if (item.price === null) return t('menu.priceNotShown')
  if (item.isSurcharge) return item.price === 0 ? t('menu.free') : `+${item.price}`
  return String(item.price)
}

function ItemTable({ items, onEdit }: { items: MenuItem[]; onEdit: (item: MenuItem) => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const remove = useMutation({
    mutationFn: deleteMenuItem,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['menu'] }),
  })

  return (
    <div className="card overflow-x-auto">
      <table className="table">
        <thead>
          <tr>
            <th>{t('menu.picture')}</th>
            <th>{t('menu.item')}</th>
            <th>{t('menu.category')}</th>
            <th>{t('menu.price')}</th>
            <th>{t('menu.mealPrice')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={item.id}>
              <td>
                {item.hasImage ? (
                  <img
                    src={menuImageUrl(item.id)}
                    alt=""
                    className="h-12 w-16 rounded object-cover"
                    loading="lazy"
                  />
                ) : (
                  <div className="h-12 w-16 rounded bg-ink-800" />
                )}
              </td>
              <td className="font-medium text-slate-100">
                {item.name}
                {!item.isActive && <span className="badge-muted ms-2">{t('menu.hidden')}</span>}
                {item.description && (
                  <span className="block max-w-md text-xs font-normal text-slate-500">
                    {item.description}
                  </span>
                )}
              </td>
              <td className="text-slate-400">{item.categoryName}</td>
              <td className="tabular text-slate-300">{priceLabel(item, t)}</td>
              <td className="tabular text-slate-400">{item.mealPrice ?? ''}</td>
              <td className="text-end whitespace-nowrap">
                <button type="button" onClick={() => onEdit(item)} className="btn-ghost btn-sm">
                  {t('menu.edit')}
                </button>
                <button
                  type="button"
                  onClick={() => remove.mutate(item.id)}
                  disabled={remove.isPending}
                  className="btn-ghost btn-sm"
                >
                  {t('menu.remove')}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function ItemForm({ item, onClose }: { item: MenuItem | null; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const fileInput = useRef<HTMLInputElement>(null)

  const { data: categories } = useQuery({
    queryKey: ['menu-categories'],
    queryFn: listMenuCategories,
  })

  const [categoryId, setCategoryId] = useState(item?.categoryId ?? '')
  const [name, setName] = useState(item?.name ?? '')
  const [description, setDescription] = useState(item?.description ?? '')
  const [price, setPrice] = useState(item?.price === null || item === null ? '' : String(item.price))
  const [mealPrice, setMealPrice] = useState(item?.mealPrice != null ? String(item.mealPrice) : '')
  const [isSurcharge, setIsSurcharge] = useState(item?.isSurcharge ?? false)
  const [isActive, setIsActive] = useState(item?.isActive ?? true)
  const [error, setError] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: async () => {
      const request = {
        categoryId,
        name: name.trim(),
        description: description.trim() || null,
        // Blank means "the menu prints no price", which is not zero.
        price: price.trim() === '' ? null : Number(price),
        mealPrice: mealPrice.trim() === '' ? null : Number(mealPrice),
        isSurcharge,
        isActive,
      }

      const saved = item ? await updateMenuItem(item.id, request) : await createMenuItem(request)

      // The picture goes second: it needs the item's id, and a new item has
      // none until it is saved.
      const file = fileInput.current?.files?.[0]
      if (file) await setMenuImage(saved.id, file)

      return saved
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['menu'] })
      void queryClient.invalidateQueries({ queryKey: ['menu-categories'] })
      onClose()
    },
    onError: (e) => setError(t(`menu.errors.${errorCodeOf(e)}`)),
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    save.mutate()
  }

  const canSave = name.trim().length > 0 && categoryId.length > 0

  return (
    <form onSubmit={submit} className="card card-body space-y-4">
      <h3 className="text-base font-semibold text-slate-100">
        {item ? t('menu.editHeading') : t('menu.addHeading')}
      </h3>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      <div className="grid gap-3 sm:grid-cols-2 max-w-3xl">
        <label className="field">
          <span className="field-label">{t('menu.item')}</span>
          <input value={name} onChange={(e) => setName(e.target.value)} className="input" />
        </label>

        <label className="field">
          <span className="field-label">{t('menu.category')}</span>
          <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)} className="input">
            <option value="">{t('menu.chooseCategory')}</option>
            {categories?.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </label>
      </div>

      <label className="field max-w-3xl">
        <span className="field-label">{t('menu.contents')}</span>
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          rows={2}
          className="input"
        />
      </label>

      <div className="grid gap-3 sm:grid-cols-2 max-w-3xl">
        <label className="field">
          <span className="field-label">{t('menu.price')}</span>
          <input
            value={price}
            onChange={(e) => setPrice(e.target.value)}
            inputMode="decimal"
            className="input tabular"
          />
          <span className="field-hint">{t('menu.priceHint')}</span>
        </label>

        <label className="field">
          <span className="field-label">{t('menu.mealPrice')}</span>
          <input
            value={mealPrice}
            onChange={(e) => setMealPrice(e.target.value)}
            inputMode="decimal"
            className="input tabular"
          />
          <span className="field-hint">{t('menu.mealPriceHint')}</span>
        </label>
      </div>

      <label className="field max-w-md">
        <span className="field-label">{t('menu.picture')}</span>
        <input
          ref={fileInput}
          type="file"
          accept="image/png,image/jpeg,image/webp"
          className="input"
        />
        <span className="field-hint">{t('menu.pictureHint')}</span>
      </label>

      <div className="space-y-2">
        <label className="flex items-start gap-2 text-sm text-slate-300">
          <input
            type="checkbox"
            checked={isSurcharge}
            onChange={(e) => setIsSurcharge(e.target.checked)}
            className="mt-1 accent-brand-500"
          />
          <span>
            {t('menu.surchargeLabel')}
            <span className="block field-hint">{t('menu.surchargeHint')}</span>
          </span>
        </label>

        <label className="flex items-center gap-2 text-sm text-slate-300">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
            className="accent-brand-500"
          />
          {t('menu.activeLabel')}
        </label>
      </div>

      <div className="flex gap-2">
        <button type="submit" disabled={save.isPending || !canSave} className="btn-primary">
          {t('menu.save')}
        </button>
        <button type="button" onClick={onClose} className="btn-ghost">
          {t('menu.cancel')}
        </button>
      </div>
    </form>
  )
}
