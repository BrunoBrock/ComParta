import {
  CircleHelp,
  Download,
  ExternalLink,
  FolderOpen,
  FolderSearch,
  Inbox,
  Pause,
  Play,
  Trash2,
  X,
} from 'lucide-react'
import { useState } from 'react'
import type { Bridge, ErrorCode, TransferDto } from '../bridge/index.ts'
import { formatSize, formatTimeLeft, percentOf, t } from '../i18n/index.ts'
import { inOrder, type SortOrder } from '../order.ts'
import { matches } from '../search.ts'
import DeleteReceivedDialog from './DeleteReceivedDialog.tsx'
import type { DetailsOf } from './DetailsWindow.tsx'
import ItemView, { type ViewMode } from './ItemView.tsx'
import SectionHead from './SectionHead.tsx'
import Button from './ui/Button.tsx'
import EmptyState from './ui/EmptyState.tsx'
import type { ChipTone } from './ui/StatusChip.tsx'

interface TransfersSectionProps {
  bridge: Bridge
  /** null while the first snapshot is loading. */
  transfers: TransferDto[] | null
  query: string
  view: ViewMode
  onViewChange: (view: ViewMode) => void
  sort: SortOrder
  onSortChange: (sort: SortOrder) => void
  onError: (code: ErrorCode) => void
  /** A confirmation that goes by itself, such as a deleted file. */
  onInfo: (text: string) => void
  onStart: () => void
  onClearSearch: () => void
  /** Transfers that have waited long enough to offer the connection help (UX design, "Stuck transfer"). */
  stuck: ReadonlySet<string>
  onHelp: () => void
  /** Receiving is stopped from the speed bar, so every unfinished transfer reads "Stopped". */
  stopped: boolean
  onDetails: (item: DetailsOf) => void
  /** The section's primary action, offered when it is empty. */
  onReceive: () => void
}

const tones: Record<TransferDto['state'], ChipTone> = {
  connecting: 'busy',
  receiving: 'busy',
  paused: 'paused',
  done: 'ok',
  failed: 'warn',
}

const percent = (transfer: TransferDto) => percentOf(transfer.receivedBytes, transfer.sizeBytes)

/** Receiving without a known size reads as still looking for the other computer. */
const shownState = (transfer: TransferDto) =>
  transfer.state === 'receiving' && !transfer.sizeBytes ? 'connecting' : transfer.state

function describe(transfer: TransferDto): string {
  const state = shownState(transfer)
  if (state === 'receiving') {
    const parts = [
      t('transfers.state.receiving', {
        percent: percent(transfer),
        received: formatSize(transfer.receivedBytes),
        total: formatSize(transfer.sizeBytes!),
      }),
    ]
    if (transfer.bytesPerSecond > 0) parts.push(t('transfers.speed', { speed: formatSize(transfer.bytesPerSecond) }))
    if (transfer.secondsLeft !== null) parts.push(formatTimeLeft(transfer.secondsLeft))
    return parts.join(' · ')
  }
  return t(`transfers.state.${state}`)
}

export default function TransfersSection(props: TransfersSectionProps) {
  const {
    bridge,
    transfers,
    query,
    view,
    onViewChange,
    sort,
    onSortChange,
    onError,
    onInfo,
    onStart,
    onClearSearch,
    stuck,
    onHelp,
    stopped,
    onDetails,
    onReceive,
  } = props

  // The received file waiting for the person to confirm its deletion.
  const [deleting, setDeleting] = useState<{ id: string; name: string } | null>(null)

  async function remove(transfer: TransferDto) {
    onStart()
    const result = await bridge.invoke('transfers.remove', { id: transfer.id })
    if (!result.ok) onError(result.error.code)
  }

  async function setPaused(transfer: TransferDto, paused: boolean) {
    onStart()
    const result = await bridge.invoke(paused ? 'transfers.pause' : 'transfers.resume', { id: transfer.id })
    if (!result.ok) onError(result.error.code)
  }

  async function runOnReceived(command: 'received.open' | 'received.reveal', transfer: TransferDto) {
    const result = await bridge.invoke(command, { id: transfer.id })
    if (!result.ok) onError(result.error.code)
  }

  async function confirmDelete() {
    const target = deleting!
    setDeleting(null)
    onStart()
    const result = await bridge.invoke('received.delete', { id: target.id })
    if (result.ok) onInfo(t('received.deleted', { name: target.name }))
    else onError(result.error.code)
  }

  async function openFolder() {
    const result = await bridge.invoke('received.openFolder')
    if (!result.ok) onError(result.error.code)
  }

  const found = transfers?.filter((transfer) => matches(transfer.name ?? '', query))
  const shown = found
    ? inOrder(
        found,
        sort,
        (transfer) => transfer.name ?? '',
        (transfer) => transfer.sizeBytes,
      )
    : null
  // Files on their way are pinned on top, the ones that arrived follow (UX design, "On the way").
  const groups = shown
    ? [
        { key: 'onTheWay' as const, items: shown.filter((transfer) => transfer.state !== 'done') },
        { key: 'arrived' as const, items: shown.filter((transfer) => transfer.state === 'done') },
      ].filter((group) => group.items.length > 0)
    : []

  return (
    <section aria-labelledby="transfers-heading" className="content-section">
      <SectionHead
        id="transfers-heading"
        title={t('transfers.heading')}
        view={view}
        onViewChange={onViewChange}
        sort={sort}
        onSortChange={onSortChange}
      >
        <button type="button" onClick={openFolder}>
          <FolderOpen size={18} aria-hidden="true" />
          {t('transfers.openFolder')}
        </button>
      </SectionHead>

      {transfers !== null && transfers.length === 0 && (
        <EmptyState
          icon={Inbox}
          text={t('transfers.empty')}
          action={
            <Button variant="primary" icon={Download} onClick={onReceive}>
              {t('transfers.open')}
            </Button>
          }
        />
      )}
      {transfers !== null && transfers.length > 0 && shown!.length === 0 && (
        <p className="empty">
          {t('search.none', { query: query.trim() })}{' '}
          <button type="button" className="link" onClick={onClearSearch}>
            {t('search.clear')}
          </button>
        </p>
      )}

      {groups.map((group) => (
        <div key={group.key} className="item-group">
          <h3 id={`transfers-${group.key}`} className="group-heading">
            {t(`transfers.${group.key}`)}
          </h3>
          <ul className={`items items-${view}`} aria-labelledby={`transfers-${group.key}`}>
            {group.items.map((transfer) => {
              const name = transfer.name ?? t('transfers.unnamed')
              const state = shownState(transfer)
              const finished = transfer.state === 'done'
              const canPause = transfer.state === 'connecting' || transfer.state === 'receiving'
              const paused = transfer.state === 'paused'
              const stoppedHere = stopped && !finished && transfer.state !== 'failed'
              const shownProgress = stoppedHere
                ? transfer.sizeBytes
                  ? { value: transfer.receivedBytes, max: transfer.sizeBytes }
                  : undefined
                : state === 'receiving' || (state === 'paused' && transfer.sizeBytes)
                  ? { value: transfer.receivedBytes, max: transfer.sizeBytes! }
                  : state === 'connecting'
                    ? 'unknown'
                    : undefined
              return (
                <ItemView
                  key={transfer.id}
                  name={name}
                  size={transfer.sizeBytes !== null ? formatSize(transfer.sizeBytes) : null}
                  tone={stoppedHere ? 'paused' : tones[state]}
                  chip={stoppedHere ? t('stopped.chip') : t(`transfers.chip.${state}`, { percent: percent(transfer) })}
                  note={stoppedHere ? t('stopped.note') : describe(transfer)}
                  progress={shownProgress}
                  onDetails={() => onDetails({ kind: 'transfer', id: transfer.id, name })}
                  actions={
                    <>
                      {finished && (
                        <>
                          <button
                            type="button"
                            className="primary"
                            onClick={() => runOnReceived('received.open', transfer)}
                            aria-label={t('received.openFor', { name })}
                          >
                            <ExternalLink size={18} aria-hidden="true" />
                            {t('received.open')}
                          </button>
                          <button
                            type="button"
                            className="quiet"
                            onClick={() => runOnReceived('received.reveal', transfer)}
                            aria-label={t('received.revealFor', { name })}
                          >
                            <FolderSearch size={18} aria-hidden="true" />
                            {t('received.reveal')}
                          </button>
                        </>
                      )}
                      {!stoppedHere && state === 'connecting' && stuck.has(transfer.id) && (
                        <button
                          type="button"
                          className="quiet"
                          onClick={onHelp}
                          aria-label={t('transfers.helpFor', { name })}
                        >
                          <CircleHelp size={18} aria-hidden="true" />
                          {t('transfers.help')}
                        </button>
                      )}
                      {!stoppedHere && (canPause || paused) && (
                        <button
                          type="button"
                          className="quiet"
                          onClick={() => setPaused(transfer, canPause)}
                          aria-label={t(paused ? 'transfers.continueFor' : 'transfers.pauseFor', { name })}
                        >
                          {paused ? <Play size={18} aria-hidden="true" /> : <Pause size={18} aria-hidden="true" />}
                          {t(paused ? 'transfers.continue' : 'transfers.pause')}
                        </button>
                      )}
                      <button
                        type="button"
                        className="quiet"
                        onClick={() => remove(transfer)}
                        aria-label={t(finished ? 'transfers.clearFor' : 'transfers.cancelFor', { name })}
                      >
                        <X size={18} aria-hidden="true" />
                        {t(finished ? 'transfers.clear' : 'transfers.cancel')}
                      </button>
                      {finished && (
                        <button
                          type="button"
                          className="quiet danger-text"
                          onClick={() => setDeleting({ id: transfer.id, name })}
                          aria-label={t('received.deleteFor', { name })}
                        >
                          <Trash2 size={18} aria-hidden="true" />
                          {t('received.delete')}
                        </button>
                      )}
                    </>
                  }
                />
              )
            })}
          </ul>
        </div>
      ))}

      {deleting && (
        <DeleteReceivedDialog name={deleting.name} onConfirm={confirmDelete} onCancel={() => setDeleting(null)} />
      )}
    </section>
  )
}
