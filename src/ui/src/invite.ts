import type { ShareDto } from './bridge/index.ts'
import { getComPartaUrl } from './appLinks.ts'
import { formatSize, t } from './i18n/index.ts'

// The name the person gave in Settings; until then the invite says "Someone".
let senderName: string | null = null

/** Called whenever the stored settings arrive or change. */
export function setSenderName(name: string | null) {
  senderName = name?.trim() || null
}

/** The friendly invite message, in the person's language; the app puts the link in (AD-16). */
export const inviteMessage = (share: Pick<ShareDto, 'name' | 'sizeBytes'>) =>
  t('invite.message', {
    sender: senderName ?? t('invite.someone'),
    name: share.name,
    size: formatSize(share.sizeBytes),
    store: getComPartaUrl,
  })
