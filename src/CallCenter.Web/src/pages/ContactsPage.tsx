import { Fragment, useEffect, useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  addPhoneToContact,
  createContact,
  duplicateOf,
  findContactsByName,
  getContact,
  searchContacts,
  updateContact,
} from '../api/contacts'
import type { Contact, ContactFilter, ContactSummary, DuplicateNumber } from '../api/contacts'
import { errorCodeOf } from '../api/users'
import ContactHistory from '../components/ContactHistory'
import FlagDialog from '../components/FlagDialog'
import type { FlagTarget } from '../components/FlagDialog'

/** Whether what was typed is a bare phone number, so it can be flagged unseen (S-45). */
const looksLikeNumber = (query: string) => /^[\d\s+()-]{3,}$/.test(query.trim())

/**
 * The shared contact list (A-60 to A-63).
 *
 * Search takes one box: the server decides whether what was typed is a number
 * or a name, so the supervisor does not pick a mode (A-61).
 *
 * The VIP and Blocked flags are set from the row's Flag button, which asks for
 * a reason (S-45). They are deliberately **not** fields on the contact form:
 * `UpsertContactRequest` carries no flags at all, so saving an address can
 * never clear a block as a side effect. The list of every flagged number that
 * S-45 asks for is this same list with the VIP or Blocked filter applied —
 * filtering and searching compose, and there is one contact list in the app
 * rather than two that can disagree.
 *
 * **A contact opens under its own row**, to edit or to flag, as the menu,
 * delivery, users and calls lists do (21 Sep). Both used to open above the
 * table, and editing waited for the full contact before anything appeared, so
 * double-clicking a row near the bottom made the page jump to the top a moment
 * later. Reported as glitchy on 24 Sep. Only a new contact, and flagging a
 * number nobody has, open above the list: there is no row for them to sit under.
 */
export default function ContactsPage() {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState<ContactFilter>('all')
  const [adding, setAdding] = useState(false)
  const [flagging, setFlagging] = useState<FlagTarget | null>(null)

  const { data: results, isLoading } = useQuery({
    queryKey: ['contacts', query, filter],
    queryFn: () => searchContacts(query, filter),
  })

  const filters: ContactFilter[] = ['all', 'vip', 'blocked']

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-4">
        <div>
          <h2 className="page-title">{t('contacts.heading')}</h2>
          <p className="page-subtitle">{t('contacts.intro')}</p>
        </div>
        <button type="button" onClick={() => setAdding(true)} className="btn-primary">
          {t('contacts.add')}
        </button>
      </div>

      <div className="flex flex-wrap items-center gap-4">
        <input
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={t('contacts.searchPlaceholder')}
          aria-label={t('contacts.search')}
          className="input max-w-md"
        />

        {/* The VIP and blocked lists S-45 asks for, as a filter on this list
            rather than a screen of their own. */}
        <div role="group" aria-label={t('contacts.filter')} className="flex gap-1">
          {filters.map((option) => (
            <button
              key={option}
              type="button"
              onClick={() => setFilter(option)}
              aria-pressed={filter === option}
              className={filter === option ? 'btn-ghost btn-sm' : 'btn-quiet btn-sm'}
            >
              {t(`contacts.filters.${option}`)}
            </button>
          ))}
        </div>
      </div>

      {adding && <ContactForm contact={null} onClose={() => setAdding(false)} />}

      {/* Only a bare number, offered from the empty result below: it has no row. */}
      {flagging && <FlagDialog target={flagging} onClose={() => setFlagging(null)} />}

      {isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : results && results.length > 0 ? (
        <ContactTable contacts={results} />
      ) : (
        /* An empty list and a failed search must not look the same. */
        <div className="card card-body flex flex-col items-center gap-3 text-center">
          <p className="text-slate-300">
            {query ? t('contacts.noMatches') : t('contacts.empty')}
          </p>

          {/* A number nobody has on file is exactly the nuisance caller S-45
              expects to be blockable, so offer it here rather than making the
              supervisor invent a contact for them first. */}
          {looksLikeNumber(query) && (
            <button
              type="button"
              onClick={() => setFlagging({ kind: 'number', number: query.trim() })}
              className="btn-ghost btn-sm"
            >
              {t('contacts.flagThisNumber', { number: query.trim() })}
            </button>
          )}
        </div>
      )}
    </div>
  )
}

/** What is open under a row: its editor, or its flag dialog. */
type Opened = { id: string; mode: 'edit' | 'flag' } | null

function ContactTable({ contacts }: { contacts: ContactSummary[] }) {
  const { t } = useTranslation()
  const [opened, setOpened] = useState<Opened>(null)

  const toggle = (id: string, mode: 'edit' | 'flag') =>
    setOpened((current) => (current?.id === id && current.mode === mode ? null : { id, mode }))
  const close = () => setOpened(null)

  return (
    <div className="card overflow-x-auto">
      <table className="table">
        <thead>
          <tr>
            <th>{t('contacts.name')}</th>
            <th>{t('contacts.phones')}</th>
            <th>{t('contacts.address')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {contacts.map((contact) => {
            const open = opened?.id === contact.id ? opened.mode : null
            return (
            <Fragment key={contact.id}>
            {/* Double-click the row to open it, as well as the Edit button. The
                button stays: it is what makes the action discoverable, and a
                double-click is unreachable from the keyboard. */}
            <tr
              onDoubleClick={() => toggle(contact.id, 'edit')}
              className={`cursor-pointer ${open ? 'bg-ink-800/40' : ''}`}
            >
              <td className="font-medium text-slate-100">
                {contact.name ?? <span className="text-slate-500">{t('contacts.noName')}</span>}
                {contact.isVip && <span className="badge-vip ms-2">{t('contacts.vip')}</span>}
                {contact.isBlocked && (
                  <span className="badge-blocked ms-2">{t('contacts.blocked')}</span>
                )}
                {/* Why, under the name: scanning the blocked list for the
                    reasons should not mean opening every row (S-45). */}
                {contact.flagReason && (
                  <span className="block text-xs font-normal text-slate-500">
                    {contact.flagReason}
                  </span>
                )}
              </td>
              <td className="tabular text-slate-400">{contact.phones.join(' · ')}</td>
              <td className="text-slate-400">{contact.address}</td>
              {/* The double-click stops here: a quick double press of Edit
                  would otherwise open the contact and shut it again. */}
              <td className="text-end whitespace-nowrap" onDoubleClick={(e) => e.stopPropagation()}>
                <button
                  type="button"
                  onClick={() => toggle(contact.id, 'edit')}
                  aria-expanded={open === 'edit'}
                  className="btn-ghost btn-sm"
                >
                  {t('contacts.edit')}
                </button>
                <button
                  type="button"
                  onClick={() => toggle(contact.id, 'flag')}
                  aria-expanded={open === 'flag'}
                  className="btn-ghost btn-sm"
                >
                  {t('contacts.flag')}
                </button>
              </td>
            </tr>

            {/* Where the supervisor is already looking, not at the top of a list
                they have scrolled past. */}
            {open && (
              <tr>
                <td colSpan={4} className="bg-ink-950/60 p-3">
                  {/* w-0 min-w-full: as wide as the table and never wider, so
                      opening a row cannot make every column jump. */}
                  <div className="w-0 min-w-full">
                    <InPlace>
                      {open === 'edit' ? (
                        <OpenContact id={contact.id} onClose={close} />
                      ) : (
                        <FlagDialog target={{ kind: 'contact', ...contact }} onClose={close} />
                      )}
                    </InPlace>
                  </div>
                </td>
              </tr>
            )}
            </Fragment>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

/**
 * Fades in and, opened near the bottom of the window, slides itself into view.
 * "Nearest", so a panel already on screen does not move at all.
 */
function InPlace({ children }: { children: ReactNode }) {
  const box = useRef<HTMLDivElement>(null)
  useEffect(() => {
    box.current?.scrollIntoView?.({ block: 'nearest', behavior: 'smooth' })
  }, [])
  return (
    <div ref={box} className="animate-fade-in">
      {children}
    </div>
  )
}

/**
 * The editor for one contact. The row is a summary, and editing needs the full
 * contact, every number and the notes. So the panel opens at once, holding the
 * form's space, and the form takes its place when the contact arrives. It used
 * to wait unseen and then appear all at once.
 */
function OpenContact({ id, onClose }: { id: string; onClose: () => void }) {
  const { t } = useTranslation()
  const contact = useQuery({ queryKey: ['contacts', 'one', id], queryFn: () => getContact(id) })

  if (contact.isError) return <p className="notice-error">{t('contacts.errors.server_error')}</p>
  if (!contact.data) {
    return <div className="card h-72 animate-pulse bg-ink-800/60" aria-label={t('app.loading')} />
  }
  return <ContactForm contact={contact.data} onClose={onClose} />
}

function ContactForm({ contact, onClose }: { contact: Contact | null; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const [name, setName] = useState(contact?.name ?? '')
  const [address, setAddress] = useState(contact?.address ?? '')
  const [notes, setNotes] = useState(contact?.notes ?? '')
  const [deliveryNotes, setDeliveryNotes] = useState(contact?.deliveryNotes ?? '')

  // One box per number, so a contact with a mobile and a landline is obvious
  // rather than hidden behind a separator the agent has to guess (A-60).
  const [phones, setPhones] = useState<string[]>(
    contact?.phones.map((p) => p.raw) ?? [''],
  )

  const [error, setError] = useState<string | null>(null)
  const [duplicate, setDuplicate] = useState<DuplicateNumber | null>(null)

  // Contacts that already carry this name (A-63). Looked up as the name is
  // typed, so the agent is told before saving rather than afterwards. Never
  // blocks the save - common names are common, and two customers may genuinely
  // share one.
  const { data: sameName } = useQuery({
    queryKey: ['contacts', 'by-name', name, contact?.id],
    queryFn: () => findContactsByName(name.trim(), contact?.id),
    enabled: name.trim().length > 0,
  })

  const firstNumber = phones.map((p) => p.trim()).find(Boolean) ?? ''

  const addNumber = useMutation({
    mutationFn: (contactId: string) => addPhoneToContact(contactId, firstNumber),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['contacts'] })
      onClose()
    },
    onError: (e) => {
      setDuplicate(duplicateOf(e))
      setError(t(`contacts.errors.${errorCodeOf(e)}`))
    },
  })

  const save = useMutation({
    mutationFn: () => {
      const request = {
        name: name.trim() || null,
        address: address.trim() || null,
        notes: notes.trim() || null,
        deliveryNotes: deliveryNotes.trim() || null,
        phones: phones.map((p) => p.trim()).filter(Boolean),
      }
      return contact ? updateContact(contact.id, request) : createContact(request)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['contacts'] })
      onClose()
    },
    onError: (e) => {
      setDuplicate(duplicateOf(e))
      setError(t(`contacts.errors.${errorCodeOf(e)}`))
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setDuplicate(null)
    save.mutate()
  }

  const hasNumber = phones.some((p) => p.trim())

  return (
    <form onSubmit={submit} className="card card-body space-y-5">
      <h3 className="text-base font-semibold text-slate-100">
        {contact ? t('contacts.editHeading') : t('contacts.addHeading')}
      </h3>

      {error && (
        <div role="alert" className="notice-error">
          <p>{error}</p>
          {/* The server says which contact already holds the number, so the
              answer can be "open that one" rather than a dead end (A-63). */}
          {duplicate && (
            <p className="mt-1">
              {t('contacts.duplicateHolder', {
                name: duplicate.existingContactName ?? t('contacts.noName'),
                number: duplicate.number,
              })}
            </p>
          )}
        </div>
      )}

      <div className="grid gap-3 sm:grid-cols-2 max-w-2xl">
        <Field label={t('contacts.name')} value={name} onChange={setName} />
        <Field label={t('contacts.address')} value={address} onChange={setAddress} />
      </div>

      {/* A matching name is a prompt to look, not an obstacle (A-63). */}
      {sameName && sameName.length > 0 && (
        <div className="notice-warning">
          <p className="font-medium">{t('contacts.sameNameWarning', { count: sameName.length })}</p>
          <ul className="mt-2 space-y-1">
            {sameName.map((match) => (
              <li key={match.id} className="flex flex-wrap items-center gap-2">
                <span className="text-slate-300">
                  {match.name} · {match.phones.join(' · ')}
                  {match.address ? ` · ${match.address}` : ''}
                </span>
                {firstNumber && (
                  <button
                    type="button"
                    onClick={() => addNumber.mutate(match.id)}
                    disabled={addNumber.isPending}
                    className="btn-ghost btn-sm border-amber-400"
                  >
                    {t('contacts.addNumberToThem')}
                  </button>
                )}
              </li>
            ))}
          </ul>
          <p className="mt-2 text-xs text-amber-200/80">{t('contacts.sameNameHint')}</p>
        </div>
      )}

      <fieldset className="space-y-2 max-w-md">
        <legend className="field-label">{t('contacts.phones')}</legend>
        {phones.map((phone, index) => (
          <div key={index} className="flex gap-2">
            <input
              value={phone}
              aria-label={t('contacts.phoneNumber', { index: index + 1 })}
              onChange={(e) =>
                setPhones(phones.map((p, i) => (i === index ? e.target.value : p)))
              }
              className="input tabular"
            />
            {phones.length > 1 && (
              <button
                type="button"
                onClick={() => setPhones(phones.filter((_, i) => i !== index))}
                className="btn-ghost btn-sm"
                aria-label={t('contacts.removePhone')}
              >
                ×
              </button>
            )}
          </div>
        ))}
        <button
          type="button"
          onClick={() => setPhones([...phones, ''])}
          className="text-sm font-medium text-brand-500 hover:text-brand-700"
        >
          {t('contacts.addPhone')}
        </button>
        <p className="field-hint">{t('contacts.phoneHint')}</p>
      </fieldset>

      <div className="grid gap-3 sm:grid-cols-2 max-w-2xl">
        <Field label={t('contacts.notes')} value={notes} onChange={setNotes} />
        <Field label={t('contacts.deliveryNotes')} value={deliveryNotes} onChange={setDeliveryNotes} />
      </div>

      <div className="flex gap-2">
        <button type="submit" disabled={save.isPending || !hasNumber} className="btn-primary">
          {t('contacts.save')}
        </button>
        <button type="button" onClick={onClose} className="btn-ghost">
          {t('contacts.cancel')}
        </button>
      </div>

      {/* A-62: the customer's history, under their details. Only for a contact
          that exists — a new one has none, and an empty table on the "add"
          form would just be noise. */}
      {contact && (
        <div className="border-t border-ink-700 pt-4">
          <p className="field-label mb-2">{t('history.heading')}</p>
          <ContactHistory contactId={contact.id} />
        </div>
      )}
    </form>
  )
}

function Field({
  label,
  value,
  onChange,
}: {
  label: string
  value: string
  onChange: (value: string) => void
}) {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      <input value={value} onChange={(e) => onChange(e.target.value)} className="input" />
    </label>
  )
}
