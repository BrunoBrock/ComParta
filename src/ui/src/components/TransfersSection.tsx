import { type FormEvent, useState } from 'react'
import type { Bridge, ErrorCode, TransferDto } from '../bridge/index.ts'
import { formatSize, t } from '../i18n/index.ts'

interface TransfersSectionProps {
  bridge: Bridge
  /** null while the first snapshot is loading. */
  transfers: TransferDto[] | null
  onError: (code: ErrorCode) => void
  onStart: () => void
}

function describe(transfer: TransferDto): string {
  if (transfer.state === 'receiving' && transfer.sizeBytes) {
    return t('transfers.state.receiving', {
      percent: Math.floor((transfer.receivedBytes / transfer.sizeBytes) * 100),
      received: formatSize(transfer.receivedBytes),
      total: formatSize(transfer.sizeBytes),
    })
  }
  return t(`transfers.state.${transfer.state === 'receiving' ? 'connecting' : transfer.state}`)
}

export default function TransfersSection({ bridge, transfers, onError, onStart }: TransfersSectionProps) {
  // View state only: the text being typed and whether a request is on its way.
  const [invite, setInvite] = useState('')
  const [busy, setBusy] = useState(false)

  async function receive(event: FormEvent) {
    event.preventDefault()
    onStart()
    setBusy(true)
    try {
      const result = await bridge.invoke('transfers.receive', { invite })
      if (result.ok) setInvite('')
      else onError(result.error.code)
    } finally {
      setBusy(false)
    }
  }

  async function remove(transfer: TransferDto) {
    onStart()
    const result = await bridge.invoke('transfers.remove', { id: transfer.id })
    if (!result.ok) onError(result.error.code)
  }

  async function openFolder() {
    const result = await bridge.invoke('received.openFolder')
    if (!result.ok) onError(result.error.code)
  }

  return (
    <section aria-labelledby="transfers-heading">
      <div className="section-head">
        <h2 id="transfers-heading">{t('transfers.heading')}</h2>
        <button type="button" onClick={openFolder}>
          {t('transfers.openFolder')}
        </button>
      </div>

      <form className="invite-form" onSubmit={receive}>
        <label htmlFor="invite-input">{t('transfers.inviteLabel')}</label>
        <div className="invite-row">
          <input
            id="invite-input"
            type="text"
            value={invite}
            onChange={(event) => setInvite(event.target.value)}
            autoComplete="off"
            spellCheck={false}
          />
          <button type="submit" className="primary" disabled={busy || invite.trim() === ''}>
            {t('transfers.receive')}
          </button>
        </div>
      </form>

      {transfers !== null && transfers.length === 0 && <p className="empty">{t('transfers.empty')}</p>}

      {transfers !== null && transfers.length > 0 && (
        <ul className="items">
          {transfers.map((transfer) => {
            const name = transfer.name ?? t('transfers.unnamed')
            const finished = transfer.state === 'done'
            return (
              <li key={transfer.id} className={transfer.state === 'failed' ? 'item item-problem' : 'item'}>
                <div className="item-text">
                  <span className="item-name">{name}</span>
                  {transfer.sizeBytes !== null && <span className="item-detail">{formatSize(transfer.sizeBytes)}</span>}
                  <span className={`item-status status-${transfer.state}`}>{describe(transfer)}</span>
                  {transfer.state === 'receiving' && transfer.sizeBytes !== null && (
                    <progress value={transfer.receivedBytes} max={transfer.sizeBytes} aria-label={name} />
                  )}
                  {transfer.state === 'connecting' && <progress aria-label={name} />}
                </div>
                <div className="item-actions">
                  <button
                    type="button"
                    className="quiet"
                    onClick={() => remove(transfer)}
                    aria-label={t(finished ? 'transfers.clearFor' : 'transfers.cancelFor', { name })}
                  >
                    {t(finished ? 'transfers.clear' : 'transfers.cancel')}
                  </button>
                </div>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
