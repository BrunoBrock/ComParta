import type { ShareDto } from './bridge/index.ts'
import { getComPartaUrl } from './appLinks.ts'
import { formatSize, t } from './i18n/index.ts'

/** The friendly invite message, in the person's language; the app puts the link in (AD-16). */
export const inviteMessage = (share: ShareDto) =>
  t('invite.message', {
    sender: t('invite.someone'),
    name: share.name,
    size: formatSize(share.sizeBytes),
    store: getComPartaUrl,
  })
