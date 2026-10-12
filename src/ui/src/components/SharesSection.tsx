import { Check, Copy, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { getComPartaUrl } from '../appLinks.ts'
import type { Bridge, ErrorCode, ShareDto } from '../bridge/index.ts'
import { formatSize, t } from '../i18n/index.ts'
import { matches } from '../search.ts'
import type { DetailsOf } from './DetailsWindow.tsx'
import ItemView, { type ViewMode } from './ItemView.tsx'
import type { ChipTone } from './StatusChip.tsx'
import RemoveShareDialog from './RemoveShareDialog.tsx'
import SectionHead from './SectionHead.tsx'

interface SharesSectionProps {
  bridge: Bridge
  /** null while the first snapshot is loading. */
  shares: ShareDto[] | null
  query: string
  view: ViewMode
  onViewChange: (view: ViewMode) => void
  onError: (code: ErrorCode) => void
  onInfo: (text: string) => void
  /** Called when the person starts a new action, so an old message can go away. */
  onStart: () => void
  onClearSearch: () => void
  /** Sending is stopped from the speed bar, so every ready share reads "Stopped". */
  stopped: boolean
  onDetails: (item: DetailsOf) => void
}

const tones: Record<ShareDto['status'], ChipTone> = { preparing: 'busy', ready: 'ok', unavailable: 'warn' }

export default function SharesSection(props: SharesSectionProps) {
  const { bridge, shares, query, view, onViewChange, onError, onInfo, onStart, onClearSearch, stopped, onDetails } =
    props
  // View state only: which share the confirmation is open for.
  const [removing, setRemoving] = useState<ShareDto | null>(null)
  // The share whose button says "Copied" for a moment.
  const [copied, setCopied] = useState<string | null>(null)

  useEffect(() => {
    if (copied === null) return
    const timer = setTimeout(() => setCopied(null), 3000)
    return () => clearTimeout(timer)
  }, [copied])

  async function copyInvite(share: ShareDto) {
    onStart()
    // The friendly message is written here, in the person's language; the app puts the link in (AD-16).
    const message = t('invite.message', {
      sender: t('invite.someone'),
      name: share.name,
      size: formatSize(share.sizeBytes),
      store: getComPartaUrl,
    })
    const result = await bridge.invoke('shares.copyInvite', { id: share.id, message })
    if (result.ok) {
      setCopied(share.id)
      onInfo(t('shares.inviteCopied', { name: share.name }))
    } else onError(result.error.code)
  }

  async function confirmRemove(share: ShareDto) {
    setRemoving(null)
    onStart()
    const result = await bridge.invoke('shares.remove', { id: share.id })
    if (!result.ok) onError(result.error.code)
  }

  const shown = shares?.filter((share) => matches(share.name, query)) ?? null

  return (
    <section aria-labelledby="shares-heading" className="content-section">
      <SectionHead id="shares-heading" title={t('shares.heading')} view={view} onViewChange={onViewChange} />

      {shares !== null && shares.length === 0 && <p className="empty">{t('shares.empty')}</p>}
      {shares !== null && shares.length > 0 && shown!.length === 0 && (
        <p className="empty">
          {t('search.none', { query: query.trim() })}{' '}
          <button type="button" className="link" onClick={onClearSearch}>
            {t('search.clear')}
          </button>
        </p>
      )}

      {shown !== null && shown.length > 0 && (
        <ul className={`items items-${view}`}>
          {shown.map((share) => {
            const stoppedHere = stopped && share.status === 'ready'
            return (
              <ItemView
                key={share.id}
                name={share.name}
                size={formatSize(share.sizeBytes)}
                tone={stoppedHere ? 'paused' : tones[share.status]}
                chip={stoppedHere ? t('stopped.chip') : t(`shares.chip.${share.status}`)}
                note={
                  stoppedHere
                    ? t('stopped.note')
                    : share.status === 'ready'
                      ? undefined
                      : t(`shares.status.${share.status}`)
                }
                progress={share.status === 'preparing' ? 'unknown' : undefined}
                onDetails={() => onDetails({ kind: 'share', id: share.id, name: share.name })}
                actions={
                  <>
                    {share.status === 'ready' && (
                      <button
                        type="button"
                        className="primary"
                        onClick={() => copyInvite(share)}
                        aria-label={t('shares.copyInviteFor', { name: share.name })}
                      >
                        {copied === share.id ? (
                          <Check size={18} aria-hidden="true" />
                        ) : (
                          <Copy size={18} aria-hidden="true" />
                        )}
                        {t(copied === share.id ? 'shares.copied' : 'shares.copyInvite')}
                      </button>
                    )}
                    <button
                      type="button"
                      className="quiet"
                      onClick={() => setRemoving(share)}
                      aria-label={t('shares.removeFor', { name: share.name })}
                    >
                      <Trash2 size={18} aria-hidden="true" />
                      {t('shares.remove')}
                    </button>
                  </>
                }
              />
            )
          })}
        </ul>
      )}

      {removing && (
        <RemoveShareDialog
          share={removing}
          onConfirm={() => confirmRemove(removing)}
          onCancel={() => setRemoving(null)}
        />
      )}
    </section>
  )
}
