import { Fragment, useEffect, useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  createMenuCategory,
  createMenuItem,
  deleteMenuCategory,
  deleteMenuItem,
  listMenuCategories,
  searchMenu,
  setMenuImage,
  updateMenuCategory,
  updateMenuItem,
} from '../api/menu'
import type { MenuCategory, MenuItem } from '../api/menu'
import { errorCodeOf } from '../api/users'
import MenuImage from '../components/MenuImage'

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
  const [managingCategories, setManagingCategories] = useState(false)

  // Bumped whenever an item is saved, and appended to every picture URL. The
  // server marks pictures good for a day, so without this a supervisor who
  // replaced a photograph would go on seeing the old one until tomorrow.
  const [imageStamp, setImageStamp] = useState(() => Date.now())

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
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => setManagingCategories((open) => !open)}
            className="btn-ghost"
          >
            {t('menu.manageCategories')}
          </button>
          <button type="button" onClick={() => setEditing('new')} className="btn-primary">
            {t('menu.add')}
          </button>
        </div>
      </div>

      {managingCategories && <CategoryManager onClose={() => setManagingCategories(false)} />}

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

      {/* A new item has no row to sit under, so it opens here, where the
          button that asked for it is. An edit opens beside the row being
          edited - see ItemTable. */}
      {editing === 'new' && (
        <ItemForm
          item={null}
          imageStamp={imageStamp}
          onSaved={() => setImageStamp(Date.now())}
          onClose={() => setEditing(null)}
        />
      )}

      {isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : items && items.length > 0 ? (
        <ItemTable
          items={items}
          imageStamp={imageStamp}
          editing={editing === 'new' ? null : editing}
          onEdit={setEditing}
          renderEditor={(item) => (
            <ItemForm
              item={item}
              imageStamp={imageStamp}
              onSaved={() => setImageStamp(Date.now())}
              onClose={() => setEditing(null)}
            />
          )}
        />
      ) : (
        <div className="card card-body text-center">
          <p className="text-slate-300">{query ? t('menu.noMatches') : t('menu.empty')}</p>
        </div>
      )}
    </div>
  )
}

/**
 * The categories that group the menu (S-59).
 *
 * Kept behind a button rather than on the page: categories change once a
 * season, items change weekly, and a screen that shows both at once makes the
 * rare thing as loud as the common one.
 *
 * Hiding is offered beside removing because removing is usually refused — a
 * category holding items cannot be deleted, and "we are not selling these this
 * month" is what the supervisor actually means.
 */
function CategoryManager({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [newName, setNewName] = useState('')

  const { data: categories } = useQuery({
    queryKey: ['menu-categories'],
    queryFn: listMenuCategories,
  })

  // Both lists are invalidated on every change: an item's row prints its
  // category's name, so renaming one leaves the table wrong until it refetches.
  function refresh() {
    void queryClient.invalidateQueries({ queryKey: ['menu-categories'] })
    void queryClient.invalidateQueries({ queryKey: ['menu'] })
  }

  const fail = (e: unknown) => setError(t(`menu.errors.${errorCodeOf(e)}`))

  const add = useMutation({
    mutationFn: () =>
      createMenuCategory({
        name: newName.trim(),
        // Added at the end of the printed order. There is no drag-and-drop
        // here; the order is nudged with the arrows below.
        sortOrder: (categories?.length ?? 0) * 10,
        isActive: true,
      }),
    onSuccess: () => {
      setNewName('')
      refresh()
    },
    onError: fail,
  })

  const save = useMutation({
    mutationFn: ({ id, ...request }: MenuCategory & { name: string }) =>
      updateMenuCategory(id, {
        name: request.name,
        sortOrder: request.sortOrder,
        isActive: request.isActive,
      }),
    onSuccess: refresh,
    onError: fail,
  })

  const remove = useMutation({
    mutationFn: deleteMenuCategory,
    onSuccess: refresh,
    onError: fail,
  })

  /** Swaps a category with its neighbour in the printed order. */
  function move(index: number, by: number) {
    const list = categories ?? []
    const here = list[index]
    const there = list[index + by]
    if (!here || !there) return

    save.mutate({ ...here, sortOrder: there.sortOrder })
    save.mutate({ ...there, sortOrder: here.sortOrder })
  }

  const pending = add.isPending || save.isPending || remove.isPending

  return (
    <div className="card card-body space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-base font-semibold text-slate-100">{t('menu.categories')}</h3>
        <button type="button" onClick={onClose} className="btn-ghost btn-sm">
          {t('menu.close')}
        </button>
      </div>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      <ul className="divide-y divide-ink-800">
        {categories?.map((category, index) => (
          <li key={category.id} className="flex flex-wrap items-center gap-2 py-2">
            <input
              defaultValue={category.name}
              aria-label={t('menu.categoryName')}
              // Saved on leaving the field rather than on every keystroke: a
              // rename is one edit, not one per letter.
              onBlur={(e) => {
                const name = e.target.value.trim()
                if (name && name !== category.name) save.mutate({ ...category, name })
              }}
              className="input max-w-xs"
            />

            <span className="text-sm text-slate-500">
              {t('menu.itemCount', { count: category.itemCount })}
            </span>

            <div className="ms-auto flex items-center gap-2">
              <button
                type="button"
                onClick={() => move(index, -1)}
                disabled={index === 0 || pending}
                aria-label={t('menu.moveUp')}
                className="btn-ghost btn-sm"
              >
                ↑
              </button>
              <button
                type="button"
                onClick={() => move(index, 1)}
                disabled={index === (categories?.length ?? 0) - 1 || pending}
                aria-label={t('menu.moveDown')}
                className="btn-ghost btn-sm"
              >
                ↓
              </button>

              <label className="flex items-center gap-2 text-sm text-slate-300">
                <input
                  type="checkbox"
                  checked={category.isActive}
                  onChange={(e) => save.mutate({ ...category, isActive: e.target.checked })}
                  className="accent-brand-500"
                />
                {t('menu.shown')}
              </label>

              <button
                type="button"
                onClick={() => remove.mutate(category.id)}
                disabled={pending}
                className="btn-ghost btn-sm"
              >
                {t('menu.remove')}
              </button>
            </div>
          </li>
        ))}
      </ul>

      <div className="flex flex-wrap items-end gap-2">
        <label className="field max-w-xs">
          <span className="field-label">{t('menu.newCategory')}</span>
          <input
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            className="input"
          />
        </label>
        <button
          type="button"
          onClick={() => {
            setError(null)
            add.mutate()
          }}
          disabled={newName.trim().length === 0 || pending}
          className="btn-primary"
        >
          {t('menu.addCategory')}
        </button>
      </div>
    </div>
  )
}

/** What to show in the price column — the three shapes, told apart. */
function priceLabel(item: MenuItem, t: (key: string) => string): string {
  if (item.price === null) return t('menu.priceNotShown')
  if (item.isSurcharge) return item.price === 0 ? t('menu.free') : `+${item.price}`
  return String(item.price)
}

function ItemTable({
  items,
  imageStamp,
  editing,
  onEdit,
  renderEditor,
}: {
  items: MenuItem[]
  imageStamp: number
  /** The item being edited, so its form can open under its own row. */
  editing: MenuItem | null
  onEdit: (item: MenuItem) => void
  renderEditor: (item: MenuItem) => ReactNode
}) {
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
            <Fragment key={item.id}>
            {/* Double-click opens the editor, as it does on the contacts list.
                The Edit button stays: a double-click is a shortcut, never the
                only way in. */}
            <tr
              onDoubleClick={() => onEdit(item)}
              className={editing?.id === item.id ? 'bg-ink-800/40' : undefined}
            >
              <td>
                <MenuImage itemId={item.id} hasImage={item.hasImage} stamp={imageStamp} />
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
              {/* The double-click must not reach here: two quick clicks on
                  Remove would delete the row and then open an editor for it. */}
              <td className="text-end whitespace-nowrap" onDoubleClick={(e) => e.stopPropagation()}>
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

            {/* The editor, where the supervisor is already looking rather than
                at the top of a list they have scrolled past. */}
            {editing?.id === item.id && (
              <tr>
                <td colSpan={6} className="bg-ink-900/60">
                  {renderEditor(item)}
                </td>
              </tr>
            )}
            </Fragment>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function ItemForm({
  item,
  imageStamp,
  onSaved,
  onClose,
}: {
  item: MenuItem | null
  imageStamp: number
  onSaved: () => void
  onClose: () => void
}) {
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

  // The picture chosen but not yet saved, shown so the supervisor can see they
  // picked the right photograph before committing to it.
  const [preview, setPreview] = useState<string | null>(null)
  const [removePicture, setRemovePicture] = useState(false)

  // Revoked when it is replaced or the form closes: an object URL holds the
  // file in memory until it is, and a supervisor editing twenty items would
  // otherwise leave twenty photographs behind.
  useEffect(() => () => {
    if (preview) URL.revokeObjectURL(preview)
  }, [preview])

  function chooseFile() {
    const file = fileInput.current?.files?.[0]

    if (preview) URL.revokeObjectURL(preview)
    setPreview(file ? URL.createObjectURL(file) : null)
    setError(null)

    // Picking a picture is the opposite of removing one; asking for both would
    // save whichever the code happened to check first.
    if (file) setRemovePicture(false)
  }

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

      if (file) {
        await setMenuImage(saved.id, file)
      } else if (removePicture && item?.hasImage) {
        await setMenuImage(saved.id, null)
      }

      return saved
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['menu'] })
      void queryClient.invalidateQueries({ queryKey: ['menu-categories'] })
      onSaved()
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

      <div className="flex items-start gap-4">
        {/* What the item looks like now, or will look like once saved. Without
            it the supervisor cannot tell whether an item already has a picture,
            and so cannot tell whether they are adding or replacing one. */}
        {preview ? (
          <img src={preview} alt="" className="h-24 w-32 rounded object-cover" />
        ) : (
          <MenuImage
            itemId={item?.id ?? ''}
            hasImage={(item?.hasImage ?? false) && !removePicture}
            stamp={imageStamp}
            className="h-24 w-32"
          />
        )}

        {/* A div, not a label, because the checkbox below needs its own and
            labels do not nest. */}
        <div className="field max-w-md">
          <label className="field">
            <span className="field-label">{t('menu.picture')}</span>
            <input
              ref={fileInput}
              type="file"
              accept="image/png,image/jpeg,image/webp"
              onChange={chooseFile}
              className="input"
            />
          </label>
          <span className="field-hint">{t('menu.pictureHint')}</span>

          {item?.hasImage && !preview && (
            <label className="mt-2 flex items-center gap-2 text-sm text-slate-300">
              <input
                type="checkbox"
                checked={removePicture}
                onChange={(e) => setRemovePicture(e.target.checked)}
                className="accent-brand-500"
              />
              {t('menu.removePicture')}
            </label>
          )}
        </div>
      </div>

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
