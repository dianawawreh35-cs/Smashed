import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { fetchMenuImage } from '../api/menu'

/**
 * A menu item's photograph (A-66, S-59).
 *
 * <b>Why this is not just an `<img src>`.</b> It was, and every picture in the
 * supervisor app came back **401** and rendered as an empty grey box — which
 * read as "this item has no photograph" rather than as a fault, so the screen
 * looked plausible while showing nothing. The endpoint requires a signed-in
 * user, and a browser loading an `<img>` issues its own request carrying none of
 * our headers, so the token never arrives. The Agent App was unaffected: it
 * fetches pictures through its API client, which does send the token.
 *
 * So the bytes are fetched with the token attached and handed to the tag as an
 * object URL. The browser's HTTP cache still applies, because this is an
 * ordinary GET — a picture the server marked good for a day is still fetched
 * once.
 *
 * An item with no picture renders as a labelled placeholder rather than an empty
 * box, so "no photograph" can be told apart from "it failed to load" by looking.
 * Five of the 44 seeded items are add-ons the printed menu does not photograph.
 */
export default function MenuImage({
  itemId,
  hasImage,
  stamp,
  className = 'h-12 w-16',
}: {
  itemId: string
  hasImage: boolean
  /** Changes when the item is saved, so a replaced picture is refetched. */
  stamp: number
  className?: string
}) {
  const { t } = useTranslation()

  const { data: blob, isError } = useQuery({
    queryKey: ['menu-image', itemId, stamp],
    queryFn: () => fetchMenuImage(itemId, stamp),
    // Asking for a picture the row says does not exist would be 44 pointless
    // 404s on every search.
    enabled: hasImage,
    retry: false,
    staleTime: Infinity,
  })

  const [url, setUrl] = useState<string | null>(null)

  // Revoked when the blob changes or the row unmounts. An object URL holds its
  // bytes in memory until it is, and a supervisor scrolling a long menu would
  // otherwise accumulate every photograph they had passed.
  useEffect(() => {
    if (!blob) {
      setUrl(null)
      return
    }

    const objectUrl = URL.createObjectURL(blob)
    setUrl(objectUrl)

    return () => URL.revokeObjectURL(objectUrl)
  }, [blob])

  if (url) {
    return <img src={url} alt="" className={`${className} rounded object-cover`} />
  }

  // A picture that was promised and did not arrive says so. Silently showing the
  // same grey box as an item with no photograph is what hid this bug.
  const label = hasImage
    ? isError
      ? t('menu.pictureFailed')
      : t('app.loading')
    : t('menu.noPicture')

  return (
    <div
      className={`${className} flex items-center justify-center rounded bg-ink-800 px-1 text-center text-[10px] leading-tight text-slate-500`}
    >
      {label}
    </div>
  )
}
