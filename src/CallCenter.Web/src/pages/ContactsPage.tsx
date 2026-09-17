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
import type { Contact, ContactSummary, DuplicateNumber } from '../api/contacts'
import { errorCodeOf } from '../api/users'

/**
 * The shared contact list (A-60 to A-63).
 *
 * Search takes one box: the server decides whether what was typed is a number
 * or a name, so the supervisor does not pick a mode (A-61).
 *
 * The VIP and Blocked flags are shown but not editable here — they belong to
 * their own screen (S-45), and this form must not be able to clear a block as a
 * side effect of fixing an address.
 */
export default function ContactsPage() {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [editing, setEditing] = useState<Contact | 'new' | null>(null)

  const { data: results, isLoading } = useQuery({
    queryKey: ['contacts', query],
    queryFn: () => searchContacts(query),
  })

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-4">
        <h2 className="text-lg font-semibold">{t('contacts.heading')}</h2>
        <button
          type="button"
          onClick={() => setEditing('new')}
          className="rounded bg-brand-600 px-3 py-2 text-white text-sm"
        >
          {t('contacts.add')}
        </button>
      </div>

      <input
        type="search"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder={t('contacts.searchPlaceholder')}
        aria-label={t('contacts.search')}
        className="w-full max-w-md rounded border border-slate-300 px-3 py-2"
      />

      {editing && (
        <ContactForm
          contact={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
        />
      )}

      {isLoading ? (
        <p className="text-slate-500">{t('app.loading')}</p>
      ) : results && results.length > 0 ? (
        <ContactTable contacts={results} onEdit={setEditing} />
      ) : (
        <p className="text-slate-500">
          {query ? t('contacts.noMatches') : t('contacts.empty')}
        </p>
      )}
    </div>
  )
}

function ContactTable({
  contacts,
  onEdit,
}: {
  contacts: ContactSummary[]
  onEdit: (contact: Contact) => void
}) {
  const { t } = useTranslation()

  // The row is a summary; editing needs the full contact, including every
  // number and the notes the list does not show.
  const open = useMutation({
    mutationFn: getContact,
    onSuccess: onEdit,
  })

  return (
    <div className="overflow-x-auto rounded border border-slate-200 bg-white">
      <table className="w-full text-sm">
        <thead className="bg-slate-50 text-slate-600">
          <tr>
            <th className="px-3 py-2 text-start">{t('contacts.name')}</th>
            <th className="px-3 py-2 text-start">{t('contacts.phones')}</th>
            <th className="px-3 py-2 text-start">{t('contacts.address')}</th>
            <th className="px-3 py-2" />
          </tr>
        </thead>
        <tbody>
          {contacts.map((contact) => (
            <tr key={contact.id} className="border-t border-slate-100">
              <td className="px-3 py-2">
                {contact.name ?? <span className="text-slate-400">{t('contacts.noName')}</span>}
                {contact.isVip && (
                  <span className="ms-2 rounded bg-amber-100 px-1.5 py-0.5 text-xs text-amber-800">
                    {t('contacts.vip')}
                  </span>
                )}
                {contact.isBlocked && (
                  <span className="ms-2 rounded bg-red-100 px-1.5 py-0.5 text-xs text-red-800">
                    {t('contacts.blocked')}
                  </span>
                )}
              </td>
              <td className="px-3 py-2 text-slate-600">{contact.phones.join(' · ')}</td>
              <td className="px-3 py-2 text-slate-600">{contact.address}</td>
              <td className="px-3 py-2 text-end">
                <button
                  type="button"
                  onClick={() => open.mutate(contact.id)}
                  className="rounded border border-slate-300 px-2 py-1 text-xs hover:bg-slate-50"
                >
                  {t('contacts.edit')}
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
    <form onSubmit={submit} className="rounded border border-slate-200 bg-white p-4 space-y-4">
      <h3 className="font-medium">{contact ? t('contacts.editHeading') : t('contacts.addHeading')}</h3>

      {error && (
        <div role="alert" className="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
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
        <div className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm">
          <p className="text-amber-900">{t('contacts.sameNameWarning', { count: sameName.length })}</p>
          <ul className="mt-2 space-y-1">
            {sameName.map((match) => (
              <li key={match.id} className="flex flex-wrap items-center gap-2">
                <span className="text-slate-700">
                  {match.name} · {match.phones.join(' · ')}
                  {match.address ? ` · ${match.address}` : ''}
                </span>
                {firstNumber && (
                  <button
                    type="button"
                    onClick={() => addNumber.mutate(match.id)}
                    disabled={addNumber.isPending}
                    className="rounded border border-amber-400 bg-white px-2 py-0.5 text-xs"
                  >
                    {t('contacts.addNumberToThem')}
                  </button>
                )}
              </li>
            ))}
          </ul>
          <p className="mt-2 text-xs text-amber-800">{t('contacts.sameNameHint')}</p>
        </div>
      )}

      <fieldset className="space-y-2 max-w-md">
        <legend className="text-sm text-slate-600">{t('contacts.phones')}</legend>
        {phones.map((phone, index) => (
          <div key={index} className="flex gap-2">
            <input
              value={phone}
              aria-label={t('contacts.phoneNumber', { index: index + 1 })}
              onChange={(e) =>
                setPhones(phones.map((p, i) => (i === index ? e.target.value : p)))
              }
              className="w-full rounded border border-slate-300 px-3 py-2"
            />
            {phones.length > 1 && (
              <button
                type="button"
                onClick={() => setPhones(phones.filter((_, i) => i !== index))}
                className="rounded border border-slate-300 px-2 text-sm"
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
          className="text-sm text-brand-600"
        >
          {t('contacts.addPhone')}
        </button>
        <p className="text-xs text-slate-500">{t('contacts.phoneHint')}</p>
      </fieldset>

      <div className="grid gap-3 sm:grid-cols-2 max-w-2xl">
        <Field label={t('contacts.notes')} value={notes} onChange={setNotes} />
        <Field label={t('contacts.deliveryNotes')} value={deliveryNotes} onChange={setDeliveryNotes} />
      </div>

      <div className="flex gap-2">
        <button
          type="submit"
          disabled={save.isPending || !hasNumber}
          className="rounded bg-brand-600 px-4 py-2 text-white disabled:opacity-50"
        >
          {t('contacts.save')}
        </button>
        <button type="button" onClick={onClose} className="rounded border border-slate-300 px-4 py-2">
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
    <label className="block space-y-1">
      <span className="text-sm text-slate-600">{label}</span>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded border border-slate-300 px-3 py-2"
      />
    </label>
  )
}
