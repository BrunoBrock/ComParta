import { Check, Copy, Plus, Trash2, Upload } from 'lucide-react'
import { useEffect, useState } from 'react'
import { inviteMessage } from '../invite.ts'
import type { Bridge, ErrorCode, ShareDto } from '../bridge/index.ts'
import { formatSize, formatTimeLeft, t } from '../i18n/index.ts'
import { inOrder, type SortOrder } from '../order.ts'
import { matches } from '../search.ts'
import type { DetailsOf } from './DetailsWindow.tsx'
import Thumbnail from './ui/Thumbnail.tsx'
import { moveBetweenItems } from './itemKeys.ts'
import ItemView, { type ViewMode } from './ItemView.tsx'
import type { ChipTone } from './ui/StatusChip.tsx'
import RemoveShareDialog from './RemoveShareDialog.tsx'
import SectionHead from './SectionHead.tsx'
import Button from './ui/Button.tsx'
import EmptyState from './ui/EmptyState.tsx'

interface SharesSectionProps {
  bridge: Bridge
  /** null while the first snapshot is loading. */
  shares: ShareDto[] | null
  query: string
  view: ViewMode
  onViewChange: (view: ViewMode) => void
  sort: SortOrder
  onSortChange: (sort: SortOrder) => void
  onError: (code: ErrorCode) => void
  onInfo: (text: string) => void
  /** Called when the person starts a new action, so an old message can go away. */
  onStart: () => void
  onClearSearch: () => void
  /** Sending is stopped from the speed bar, so every ready share reads "Stopped". */
  stopped: boolean
  onDetails: (item: DetailsOf) => void
  /** The section's primary action, offered when it is empty. */
  adding: boolean
  onAdd: () => void
}

const tones: Record<ShareDto['status'], ChipTone> = { preparing: 'busy', ready: 'ok', unavailable: 'warn' }

/** A ready share's chip and sentence say whether someone is getting it or has it (CAP-8). */
function chipOf(share: ShareDto): string {
  if (share.status === 'ready' && share.activity === 'sending')
    return t('shares.chip.sending', { count: share.receiverCount })
  if (share.status === 'ready' && share.activity === 'sent')
    return share.finishedCount === 1 ? t('shares.chip.sentOnce') : t('shares.chip.sent', { count: share.finishedCount })
  return t(`shares.chip.${share.status}`)
}

function noteOf(share: ShareDto): string | undefined {
  if (share.status === 'ready' && share.activity === 'sending') {
    const count = share.receiverCount
    const parts = [
      count === 1
        ? t('shares.status.sendingOne', { percent: share.percent })
        : t('shares.status.sending', { count, percent: share.percent }),
    ]
    if (share.secondsLeft !== null) parts.push(formatTimeLeft(share.secondsLeft))
    return `${parts.join(' · ')}. ${t('shares.status.keepOpen')}`
  }
  if (share.status === 'ready' && share.activity === 'sent')
    return share.finishedCount === 1
      ? t('shares.status.sentOnce')
      : t('shares.status.sent', { count: share.finishedCount })
  return share.status === 'ready' ? undefined : t(`shares.status.${share.status}`)
}

export default function SharesSection(props: SharesSectionProps) {
  const {
    bridge,
    shares,
    query,
    view,
    onViewChange,
    sort,
    onSortChange,
    onError,
    onInfo,
    onStart,
    onClearSearch,
    stopped,
    onDetails,
    adding,
    onAdd,
  } = props
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
    const result = await bridge.invoke('shares.copyInvite', { id: share.id, message: inviteMessage(share) })
    if (result.ok) {
      setCopied(share.id)
      onInfo(t('shares.inviteCopied', { name: share.name }))
    } else onError(result.error.code)
  }

  async function confirmRemove(share: ShareDto) {
    setRemoving(null)
    onStart()
    const result = await bridge.invoke('shares.remove', { id: share.id })
    if (result.ok) onInfo(t('shares.removed', { name: share.name }))
    else onError(result.error.code)
  }

  const found = shares?.filter((share) => matches(share.name, query))
  const shown = found
    ? inOrder(
        found,
        sort,
        (share) => share.name,
        (share) => share.sizeBytes,
      )
    : null

  return (
    <section aria-labelledby="shares-heading" className="content-section">
      <SectionHead
        id="shares-heading"
        title={t('shares.heading')}
        view={view}
        onViewChange={onViewChange}
        sort={sort}
        onSortChange={onSortChange}
      />

      {shares !== null && shares.length === 0 && (
        <EmptyState
          icon={Upload}
          text={t('shares.empty')}
          action={
            <Button variant="primary" icon={Plus} onClick={onAdd} disabled={adding}>
              {t('shares.add')}
            </Button>
          }
        />
      )}
      {shares !== null && shares.length > 0 && shown!.length === 0 && (
        <p className="empty">
          {t('search.none', { query: query.trim() })}{' '}
          <button type="button" className="link" onClick={onClearSearch}>
            {t('search.clear')}
          </button>
        </p>
      )}

      {shown !== null && shown.length > 0 && (
        <ul className={`items items-${view}`} onKeyDown={moveBetweenItems}>
          {shown.map((share) => {
            const stoppedHere = stopped && share.status === 'ready'
            const sending = share.status === 'ready' && share.activity === 'sending'
            return (
              <ItemView
                key={share.id}
                name={share.name}
                size={formatSize(share.sizeBytes)}
                tone={stoppedHere ? 'paused' : sending ? 'busy' : tones[share.status]}
                chip={stoppedHere ? t('stopped.chip') : chipOf(share)}
                note={stoppedHere ? t('stopped.note') : noteOf(share)}
                progress={
                  share.status === 'preparing'
                    ? 'unknown'
                    : sending && !stoppedHere
                      ? { value: share.percent, max: 100 }
                      : undefined
                }
                onDetails={() => onDetails({ kind: 'share', id: share.id, name: share.name })}
                onMain={share.status === 'ready' ? () => copyInvite(share) : undefined}
                onRemove={() => setRemoving(share)}
                picture={
                  <Thumbnail
                    bridge={bridge}
                    kind="share"
                    id={share.id}
                    name={share.name}
                    ready={share.status !== 'unavailable'}
                  />
                }
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
