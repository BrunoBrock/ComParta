import { Copy, Trash2 } from 'lucide-react'
import { useState } from 'react'
import type { Bridge, ErrorCode, ShareDto } from '../bridge/index.ts'
import { formatSize, t } from '../i18n/index.ts'
import { matches } from '../search.ts'
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
}

const tones: Record<ShareDto['status'], ChipTone> = { preparing: 'busy', ready: 'ok', unavailable: 'warn' }

export default function SharesSection(props: SharesSectionProps) {
  const { bridge, shares, query, view, onViewChange, onError, onInfo, onStart, onClearSearch } = props
  // View state only: which share the confirmation is open for.
  const [removing, setRemoving] = useState<ShareDto | null>(null)

  async function copyInvite(share: ShareDto) {
    onStart()
    const result = await bridge.invoke('shares.copyInvite', { id: share.id })
    if (result.ok) onInfo(t('shares.inviteCopied', { name: share.name }))
    else onError(result.error.code)
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
          {shown.map((share) => (
            <ItemView
              key={share.id}
              name={share.name}
              size={formatSize(share.sizeBytes)}
              tone={tones[share.status]}
              chip={t(`shares.chip.${share.status}`)}
              note={share.status === 'ready' ? undefined : t(`shares.status.${share.status}`)}
              progress={share.status === 'preparing' ? 'unknown' : undefined}
              actions={
                <>
                  {share.status === 'ready' && (
                    <button
                      type="button"
                      className="primary"
                      onClick={() => copyInvite(share)}
                      aria-label={t('shares.copyInviteFor', { name: share.name })}
                    >
                      <Copy size={18} aria-hidden="true" />
                      {t('shares.copyInvite')}
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
          ))}
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
