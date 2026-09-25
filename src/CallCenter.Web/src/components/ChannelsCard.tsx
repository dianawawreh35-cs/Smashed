import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { createChannel, listChannels, updateChannel } from '../api/channels'
import type { Channel } from '../api/channels'
import { errorCodeOf } from '../api/users'

/**
 * The channels a message can arrive on (S-41, A-70): add, rename, reorder,
 * hide. On the Settings page, because a channel list is a setting the
 * restaurant changes once a year, not a screen anyone works in.
 *
 * **Nothing is deleted.** A channel with messages under it is hidden instead,
 * so the history and the reports keep their channel; and Phone, which every
 * call is filed under, can only be moved — the server refuses the rest
 * (`system_channel`), and the row says so before anyone tries.
 */
export default function ChannelsCard() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [newName, setNewName] = useState('')

  const { data: channels, isLoading } = useQuery({
    queryKey: ['channels', 'all'],
    queryFn: () => listChannels(true),
  })

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['channels'] })
  const fail = (e: unknown) => setError(t(`channels.errors.${errorCodeOf(e)}`))

  const add = useMutation({
    mutationFn: () =>
      createChannel({ name: newName.trim(), sortOrder: (channels?.length ?? 0) * 10, isActive: true }),
    onSuccess: () => {
      setNewName('')
      refresh()
    },
    onError: fail,
  })

  const save = useMutation({
    mutationFn: (channel: Channel) =>
      updateChannel(channel.id, { name: channel.name, sortOrder: channel.sortOrder, isActive: channel.isActive }),
    onSuccess: refresh,
    onError: fail,
  })

  /**
   * Swaps a channel with its neighbour, then renumbers every row that is out
   * of step: the seeded orders may be equal, and swapping two equal numbers
   * moves nothing.
   */
  const move = useMutation({
    mutationFn: async ({ index, by }: { index: number; by: number }) => {
      const list = [...(channels ?? [])]
      const target = index + by
      if (target < 0 || target >= list.length) return
      ;[list[index], list[target]] = [list[target], list[index]]
      for (const [i, channel] of list.entries()) {
        const sortOrder = i * 10
        if (channel.sortOrder !== sortOrder) {
          await updateChannel(channel.id, { name: channel.name, sortOrder, isActive: channel.isActive })
        }
      }
    },
    onSuccess: refresh,
    onError: fail,
  })

  return (
    <section className="card card-body space-y-4" aria-label={t('channels.heading')}>
      <div>
        <h3 className="text-base font-semibold text-slate-100">{t('channels.heading')}</h3>
        <p className="field-hint">{t('channels.intro')}</p>
      </div>

      {error && (
        <div role="alert" className="notice-error">
          {error}
        </div>
      )}

      {isLoading ? (
        <p className="text-slate-400">{t('app.loading')}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="table">
            <thead>
              <tr>
                <th>{t('channels.name')}</th>
                <th>{t('channels.order')}</th>
                <th>{t('channels.shown')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {(channels ?? []).map((channel, index) => (
                <tr key={channel.id}>
                  <td>
                    {channel.isSystem ? (
                      <span className="text-slate-200">{channel.name}</span>
                    ) : (
                      <input
                        defaultValue={channel.name}
                        aria-label={`${t('channels.name')} ${channel.name}`}
                        onBlur={(e) => {
                          const name = e.target.value.trim()
                          if (name && name !== channel.name) {
                            setError(null)
                            save.mutate({ ...channel, name })
                          }
                        }}
                        className="input max-w-[14rem]"
                      />
                    )}
                  </td>
                  <td className="whitespace-nowrap">
                    <button
                      type="button"
                      onClick={() => move.mutate({ index, by: -1 })}
                      disabled={index === 0 || move.isPending}
                      aria-label={`${t('channels.moveUp')} ${channel.name}`}
                      className="btn-ghost btn-sm"
                    >
                      ↑
                    </button>
                    <button
                      type="button"
                      onClick={() => move.mutate({ index, by: 1 })}
                      disabled={index === (channels?.length ?? 0) - 1 || move.isPending}
                      aria-label={`${t('channels.moveDown')} ${channel.name}`}
                      className="btn-ghost btn-sm ms-1"
                    >
                      ↓
                    </button>
                  </td>
                  <td>
                    {channel.isSystem ? (
                      <span className="text-sm text-slate-300">{t('channels.offered')}</span>
                    ) : (
                      <label className="flex items-center gap-2 text-sm text-slate-300">
                        <input
                          type="checkbox"
                          checked={channel.isActive}
                          aria-label={`${t('channels.shown')} ${channel.name}`}
                          onChange={(e) => {
                            setError(null)
                            save.mutate({ ...channel, isActive: e.target.checked })
                          }}
                          className="accent-brand-500"
                        />
                        {channel.isActive ? t('channels.offered') : t('channels.hidden')}
                      </label>
                    )}
                  </td>
                  <td className="text-xs text-slate-500">
                    {channel.isSystem ? t('channels.phoneHint') : channel.inUse ? t('channels.inUse') : ''}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="flex flex-wrap items-end gap-2">
        <label className="field max-w-[14rem]">
          <span className="field-label">{t('channels.newName')}</span>
          <input value={newName} onChange={(e) => setNewName(e.target.value)} className="input" />
        </label>
        <button
          type="button"
          onClick={() => {
            setError(null)
            add.mutate()
          }}
          disabled={newName.trim().length === 0 || add.isPending}
          className="btn-primary"
        >
          {t('channels.add')}
        </button>
      </div>
      <p className="field-hint">{t('channels.hiddenHint')}</p>
    </section>
  )
}
