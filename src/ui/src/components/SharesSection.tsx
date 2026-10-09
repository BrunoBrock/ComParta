import { useState } from 'react'
import type { Bridge, ErrorCode, ShareDto } from '../bridge/index.ts'
import { formatSize, t } from '../i18n/index.ts'
import RemoveShareDialog from './RemoveShareDialog.tsx'

interface SharesSectionProps {
  bridge: Bridge
  /** null while the first snapshot is loading. */
  shares: ShareDto[] | null
  onError: (code: ErrorCode) => void
  onInfo: (text: string) => void
  /** Called when the person starts a new action, so an old message can go away. */
  onStart: () => void
}

export default function SharesSection({ bridge, shares, onError, onInfo, onStart }: SharesSectionProps) {
  const [adding, setAdding] = useState(false)
  // View state only: which share the confirmation is open for.
  const [removing, setRemoving] = useState<ShareDto | null>(null)

  async function add() {
    onStart()
    setAdding(true)
    try {
      // The app opens the file dialog; a cancel comes back as ok with no value.
      const result = await bridge.invoke('shares.add')
      if (!result.ok) onError(result.error.code)
    } finally {
      setAdding(false)
    }
  }

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

  return (
    <section aria-labelledby="shares-heading">
      <div className="section-head">
        <h2 id="shares-heading">{t('shares.heading')}</h2>
        <button type="button" className="primary" onClick={add} disabled={adding}>
          {t('shares.add')}
        </button>
      </div>

      {shares !== null && shares.length === 0 && <p className="empty">{t('shares.empty')}</p>}

      {shares !== null && shares.length > 0 && (
        <ul className="items">
          {shares.map((share) => (
            <li key={share.id} className={share.status === 'unavailable' ? 'item item-problem' : 'item'}>
              <div className="item-text">
                <span className="item-name">{share.name}</span>
                <span className="item-detail">{formatSize(share.sizeBytes)}</span>
                <span className={`item-status status-${share.status}`}>{t(`shares.status.${share.status}`)}</span>
              </div>
              <div className="item-actions">
                {share.status === 'ready' && (
                  <button
                    type="button"
                    onClick={() => copyInvite(share)}
                    aria-label={t('shares.copyInviteFor', { name: share.name })}
                  >
                    {t('shares.copyInvite')}
                  </button>
                )}
                <button
                  type="button"
                  className="quiet"
                  onClick={() => setRemoving(share)}
                  aria-label={t('shares.removeFor', { name: share.name })}
                >
                  {t('shares.remove')}
                </button>
              </div>
            </li>
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
