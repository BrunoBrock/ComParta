import type { ShareDto } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import Dialog from './ui/Dialog.tsx'

interface RemoveShareDialogProps {
  share: ShareDto
  onConfirm: () => void
  onCancel: () => void
}

/** Asks before removing and says plainly that the original file is kept. */
export default function RemoveShareDialog({ share, onConfirm, onCancel }: RemoveShareDialogProps) {

  return (
    <Dialog labelledBy="remove-title" describedBy="remove-body" onClose={onCancel}>
      <h2 id="remove-title">{t('remove.title', { name: share.name })}</h2>
      <p id="remove-body">{t('remove.body')}</p>
      <div className="dialog-actions">
        <button type="button" onClick={onCancel} autoFocus>
          {t('remove.cancel')}
        </button>
        <button type="button" className="danger" onClick={onConfirm}>
          {t('remove.confirm')}
        </button>
      </div>
    </Dialog>
  )
}
