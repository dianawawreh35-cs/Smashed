import { useState } from 'react'
import type { FormEvent } from 'react'
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
 */
export default function ContactsPage() {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState<ContactFilter>('all')
  const [editing, setEditing] = useState<Contact | 'new' | null>(null)
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
        <button type="button" onClick={() => setEditing('new')} className="btn-primary">
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

      {editing && (
        <ContactForm
          contact={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
        />
      )}

      {flagging && <FlagDialog target={flagging} onClose={() => setFlagging(null)} />}

      {isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : results && results.length > 0 ? (
        <ContactTable contacts={results} onEdit={setEditing} onFlag={setFlagging} />
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

function ContactTable({
  contacts,
  onEdit,
  onFlag,
}: {
  contacts: ContactSummary[]
  onEdit: (contact: Contact) => void
  onFlag: (target: FlagTarget) => void
}) {
  const { t } = useTranslation()

  // The row is a summary; editing needs the full contact, including every
  // number and the notes the list does not show.
  const open = useMutation({
    mutationFn: getContact,
    onSuccess: onEdit,
  })

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
          {contacts.map((contact) => (
            /* Double-click the row to open it, as well as the Edit button. The
               button stays: it is what makes the action discoverable, and a
               double-click is unreachable from the keyboard. */
            <tr
              key={contact.id}
              onDoubleClick={() => open.mutate(contact.id)}
              className="cursor-pointer"
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
              <td className="text-end whitespace-nowrap">
                <button
                  type="button"
                  onClick={() => open.mutate(contact.id)}
                  className="btn-ghost btn-sm"
                >
                  {t('contacts.edit')}
                </button>
                <button
                  type="button"
                  onClick={() => onFlag({ kind: 'contact', ...contact })}
                  className="btn-ghost btn-sm"
                >
                  {t('contacts.flag')}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
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
