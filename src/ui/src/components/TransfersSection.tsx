import { FolderOpen, X } from 'lucide-react'
import type { Bridge, ErrorCode, TransferDto } from '../bridge/index.ts'
import { formatSize, t } from '../i18n/index.ts'
import { matches } from '../search.ts'
import ItemView, { type ViewMode } from './ItemView.tsx'
import SectionHead from './SectionHead.tsx'
import type { ChipTone } from './StatusChip.tsx'

interface TransfersSectionProps {
  bridge: Bridge
  /** null while the first snapshot is loading. */
  transfers: TransferDto[] | null
  query: string
  view: ViewMode
  onViewChange: (view: ViewMode) => void
  onError: (code: ErrorCode) => void
  onStart: () => void
  onClearSearch: () => void
}

const tones: Record<TransferDto['state'], ChipTone> = { connecting: 'busy', receiving: 'busy', done: 'ok', failed: 'warn' }

const percentOf = (transfer: TransferDto) =>
  transfer.sizeBytes ? Math.floor((transfer.receivedBytes / transfer.sizeBytes) * 100) : 0

/** Receiving without a known size reads as still looking for the other computer. */
const shownState = (transfer: TransferDto) =>
  transfer.state === 'receiving' && !transfer.sizeBytes ? 'connecting' : transfer.state

function describe(transfer: TransferDto): string {
  const state = shownState(transfer)
  if (state === 'receiving') {
    return t('transfers.state.receiving', {
      percent: percentOf(transfer),
      received: formatSize(transfer.receivedBytes),
      total: formatSize(transfer.sizeBytes!),
    })
  }
  return t(`transfers.state.${state}`)
}

/** Files on their way are pinned on top, finished ones follow (UX design, "On the way"). */
const order = (a: TransferDto, b: TransferDto) => Number(a.state === 'done') - Number(b.state === 'done')

export default function TransfersSection(props: TransfersSectionProps) {
  const { bridge, transfers, query, view, onViewChange, onError, onStart, onClearSearch } = props

  async function remove(transfer: TransferDto) {
    onStart()
    const result = await bridge.invoke('transfers.remove', { id: transfer.id })
    if (!result.ok) onError(result.error.code)
  }

  async function openFolder() {
    const result = await bridge.invoke('received.openFolder')
    if (!result.ok) onError(result.error.code)
  }

  const shown = transfers?.filter((transfer) => matches(transfer.name ?? '', query)).sort(order) ?? null

  return (
    <section aria-labelledby="transfers-heading" className="content-section">
      <SectionHead id="transfers-heading" title={t('transfers.heading')} view={view} onViewChange={onViewChange}>
        <button type="button" onClick={openFolder}>
          <FolderOpen size={18} aria-hidden="true" />
          {t('transfers.openFolder')}
        </button>
      </SectionHead>

      {transfers !== null && transfers.length === 0 && <p className="empty">{t('transfers.empty')}</p>}
      {transfers !== null && transfers.length > 0 && shown!.length === 0 && (
        <p className="empty">
          {t('search.none', { query: query.trim() })}{' '}
          <button type="button" className="link" onClick={onClearSearch}>
            {t('search.clear')}
          </button>
        </p>
      )}

      {shown !== null && shown.length > 0 && (
        <ul className={`items items-${view}`}>
          {shown.map((transfer) => {
            const name = transfer.name ?? t('transfers.unnamed')
            const state = shownState(transfer)
            const finished = transfer.state === 'done'
            return (
              <ItemView
                key={transfer.id}
                name={name}
                size={transfer.sizeBytes !== null ? formatSize(transfer.sizeBytes) : null}
                tone={tones[state]}
                chip={t(`transfers.chip.${state}`, { percent: percentOf(transfer) })}
                note={describe(transfer)}
                progress={
                  state === 'receiving'
                    ? { value: transfer.receivedBytes, max: transfer.sizeBytes! }
                    : state === 'connecting'
                      ? 'unknown'
                      : undefined
                }
                actions={
                  <button
                    type="button"
                    className="quiet"
                    onClick={() => remove(transfer)}
                    aria-label={t(finished ? 'transfers.clearFor' : 'transfers.cancelFor', { name })}
                  >
                    <X size={18} aria-hidden="true" />
                    {t(finished ? 'transfers.clear' : 'transfers.cancel')}
                  </button>
                }
              />
            )
          })}
        </ul>
      )}
    </section>
  )
}
