import { FileDown, Link, Pause, Play } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { Bridge, ErrorCode, ShareDetailsDto, TransferDetailsDto } from '../bridge/index.ts'
import { formatDateTime, formatRate, formatSize, formatTimeLeft, type MessageKey, percentOf, t } from '../i18n/index.ts'
import { inviteMessage } from '../invite.ts'
import type { ItemToggle } from './ItemView.tsx'
import Dialog from './ui/Dialog.tsx'

export type DetailsOf = { kind: 'share' | 'transfer'; id: string; name: string }

interface DetailsWindowProps {
  bridge: Bridge
  item: DetailsOf
  /** Stopped from the speed bar: the state row says so, as the item does. */
  stopped: boolean
  /** The file's own pause or play button, as on its card. */
  toggle?: ItemToggle
  onClose: () => void
  onError: (code: ErrorCode) => void
  onInfo: (text: string) => void
}

/** How often the open window asks again (UX design, "Details window"). */
export const detailsRefreshMs = 1000

type Details = { kind: 'share'; value: ShareDetailsDto } | { kind: 'transfer'; value: TransferDetailsDto }

function shareRows(share: ShareDetailsDto, stopped: boolean): [MessageKey, string][] {
  return [
    ['details.size', formatSize(share.sizeBytes)],
    ['details.state', stopped && share.status === 'ready' ? t('stopped.chip') : t(`shares.chip.${share.status}`)],
    ['details.where', share.folder],
    ['details.added', formatDateTime(share.addedAt)],
    ['details.sendingNow', formatRate(share.sendingBytesPerSecond)],
    ['details.connections', String(share.connections)],
    ['details.sent', formatSize(share.bytesSent)],
  ]
}

function transferRows(transfer: TransferDetailsDto, stopped: boolean): [MessageKey, string][] {
  const size = transfer.sizeBytes
    ? t('details.sizeReceived', {
        total: formatSize(transfer.sizeBytes),
        received: formatSize(transfer.receivedBytes),
        percent: percentOf(transfer.receivedBytes, transfer.sizeBytes),
      })
    : t('details.unknown')
  const finished = ['done', 'failed', 'blocked'].includes(transfer.state)
  const state =
    stopped && !finished
      ? t('stopped.chip')
      : transfer.state === 'receiving'
        ? t('details.state.receiving')
        : t(`transfers.chip.${transfer.state}`, { percent: 0 })
  const receiving = [formatRate(transfer.receivingBytesPerSecond)]
  if (transfer.secondsLeft !== null && !stopped) receiving.push(formatTimeLeft(transfer.secondsLeft))
  return [
    ['details.size', size],
    ['details.state', state],
    ['details.where', t('details.whereReceived')],
    ['details.added', formatDateTime(transfer.addedAt)],
    ['details.source', t(`details.source.${transfer.source}`)],
    ['details.sendingNow', formatRate(transfer.sendingBytesPerSecond)],
    ['details.receivingNow', receiving.join(', ')],
    ['details.connections', String(transfer.connections)],
    ['details.sent', formatSize(transfer.bytesSent)],
  ]
}

/**
 * Everything ComParta knows about one file, refreshed every second while open (CAP-26). Escape or
 * Close shuts it, and focus goes back to the button that opened it.
 */
export default function DetailsWindow({ bridge, item, stopped, toggle, onClose, onError, onInfo }: DetailsWindowProps) {
  const [details, setDetails] = useState<Details | null>(null)
  const [gone, setGone] = useState(false)

  useEffect(() => {
    let active = true
    const load = async () => {
      const result =
        item.kind === 'share'
          ? await bridge.invoke('shares.details', { id: item.id })
          : await bridge.invoke('transfers.details', { id: item.id })
      if (!active) return
      if (result.ok) setDetails({ kind: item.kind, value: result.value } as Details)
      else if (result.error.code === 'share.notFound' || result.error.code === 'transfer.notFound') setGone(true)
    }
    void load()
    const timer = setInterval(load, detailsRefreshMs)
    return () => {
      active = false
      clearInterval(timer)
    }
  }, [bridge, item])

  // The main action sits on top of the window's buttons (UX design, Details panel).
  async function copyInvite(share: ShareDetailsDto) {
    const message = inviteMessage({ name: item.name, sizeBytes: share.sizeBytes })
    const result = await bridge.invoke('shares.copyInvite', { id: item.id, message })
    if (result.ok) onInfo(t('shares.inviteCopied', { name: item.name }))
    else onError(result.error.code)
  }

  // For other apps (CAP-28): the link alone, or the download file for apps that do not take links.
  async function copyLink() {
    const result = await bridge.invoke('shares.copyInvite', { id: item.id, message: null })
    if (result.ok) onInfo(t('shares.linkCopied', { name: item.name }))
    else onError(result.error.code)
  }

  async function saveDownloadFile() {
    const result = await bridge.invoke('shares.saveDownloadFile', { id: item.id })
    if (result.ok) onInfo(t('shares.downloadFileSaved', { name: item.name }))
    else onError(result.error.code)
  }

  async function runOnReceived(command: 'received.open' | 'received.reveal') {
    const result = await bridge.invoke(command, { id: item.id })
    if (!result.ok) onError(result.error.code)
  }

  const rows =
    gone || details === null
      ? []
      : details.kind === 'share'
        ? shareRows(details.value, stopped)
        : transferRows(details.value, stopped)

  return (
    <Dialog labelledBy="details-title" className="details" onClose={onClose}>
      <h2 id="details-title">{t('details.title', { name: item.name })}</h2>
      {gone ? (
        <p>{t('details.gone')}</p>
      ) : (
        <dl className="details-rows" aria-busy={details === null}>
          {rows.map(([key, value]) => (
            <div key={key} className="details-row">
              <dt>{t(key)}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      )}
      <div className="dialog-actions">
        <button type="button" onClick={onClose} autoFocus>
          {t('details.close')}
        </button>
        {!gone && toggle && (
          <button type="button" onClick={toggle.onToggle} aria-label={toggle.label}>
            {toggle.paused ? <Play size={18} aria-hidden="true" /> : <Pause size={18} aria-hidden="true" />}
            {t(toggle.paused ? 'transfers.continue' : 'transfers.pause')}
          </button>
        )}
        {!gone && details?.kind === 'transfer' && details.value.state === 'done' && (
          <>
            <button type="button" onClick={() => runOnReceived('received.reveal')}>
              {t('received.reveal')}
            </button>
            <button type="button" className="primary" onClick={() => runOnReceived('received.open')}>
              {t('received.open')}
            </button>
          </>
        )}
        {!gone && details?.kind === 'share' && details.value.status === 'ready' && (
          <>
            <button type="button" onClick={copyLink}>
              <Link size={18} aria-hidden="true" />
              {t('shares.copyLink')}
            </button>
            <button type="button" onClick={saveDownloadFile}>
              <FileDown size={18} aria-hidden="true" />
              {t('shares.saveDownloadFile')}
            </button>
            <button type="button" className="primary" onClick={() => copyInvite(details.value)}>
              {t('shares.copyInvite')}
            </button>
          </>
        )}
      </div>
    </Dialog>
  )
}
