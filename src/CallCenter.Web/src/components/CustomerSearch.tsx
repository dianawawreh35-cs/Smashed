import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { searchDir } from '../lib/bidi'

/** Whether a key press landed in something that takes typing, where `/` is just a character. */
function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  return target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)
}

/**
 * Find a customer from any page (S-71): a number or a name, typed in the
 * header and opened on the Contacts page, whose list opens each one to its
 * calls and messages. The Contacts page's own box does the searching (A-61),
 * so there is one search with one set of rules, not a second one here.
 *
 * `/` puts the cursor in the box from anywhere on the page, unless the
 * supervisor is already typing in a field.
 */
export default function CustomerSearch() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [text, setText] = useState('')
  const box = useRef<HTMLInputElement>(null)

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key !== '/' || event.ctrlKey || event.metaKey || event.altKey || isTyping(event.target)) return
      event.preventDefault()
      box.current?.focus()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  function submit(event: FormEvent) {
    event.preventDefault()
    const query = text.trim()
    if (!query) return
    navigate(`/contacts?q=${encodeURIComponent(query)}`)
    setText('')
    box.current?.blur()
  }

  return (
    <form role="search" onSubmit={submit} className="min-w-[12rem] flex-1 sm:max-w-xs">
      <input
        ref={box}
        type="search"
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder={t('header.findPlaceholder')}
        aria-label={t('header.find')}
        title={t('header.findHint')}
        // A number is typed left to right in Arabic too (M-W01).
        dir={searchDir(text)}
        className="input py-1.5"
      />
    </form>
  )
}
