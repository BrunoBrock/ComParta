import type { ShareDto, TransferDto } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import type { ItemToggle } from './ItemView.tsx'

/** What a file's own pause or play button does; the same on its card and in its details window. */
export interface ToggleActions {
  /** Pauses the file on its own (true), or sends or receives it again (false). */
  setPaused: (paused: boolean) => void
  /** Continues everything on this side, when the file was stopped from the speed bar. */
  continueAll: () => void
}

/**
 * A shared file can be paused once it is ready. Stopped from the speed bar, its play button
 * continues all sending, since that is what holds it still.
 */
export function shareToggle(share: ShareDto, sendingStopped: boolean, actions: ToggleActions): ItemToggle | undefined {
  if (share.status !== 'ready') return undefined
  if (sendingStopped) return { paused: true, label: t('stopped.continueSending'), onToggle: actions.continueAll }
  return share.paused
    ? { paused: true, label: t('shares.continueFor', { name: share.name }), onToggle: () => actions.setPaused(false) }
    : { paused: false, label: t('shares.pauseFor', { name: share.name }), onToggle: () => actions.setPaused(true) }
}

/** A file on its way can be paused and continued; one that arrived or ended has no button. */
export function transferToggle(
  transfer: TransferDto,
  receivingStopped: boolean,
  actions: ToggleActions,
): ItemToggle | undefined {
  const name = transfer.name ?? t('transfers.unnamed')
  if (['done', 'failed', 'blocked'].includes(transfer.state)) return undefined
  if (receivingStopped) return { paused: true, label: t('stopped.continueReceiving'), onToggle: actions.continueAll }
  return transfer.state === 'paused'
    ? { paused: true, label: t('transfers.continueFor', { name }), onToggle: () => actions.setPaused(false) }
    : { paused: false, label: t('transfers.pauseFor', { name }), onToggle: () => actions.setPaused(true) }
}
