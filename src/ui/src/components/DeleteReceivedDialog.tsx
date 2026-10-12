import { t } from '../i18n/index.ts'
import Dialog from './ui/Dialog.tsx'

interface DeleteReceivedDialogProps {
  name: string
  onConfirm: () => void
  onCancel: () => void
}

/** Asks before deleting a received file; keeping it is the safe, focused choice. */
export default function DeleteReceivedDialog({ name, onConfirm, onCancel }: DeleteReceivedDialogProps) {
  return (
    <Dialog labelledBy="delete-received-title" describedBy="delete-received-body" onClose={onCancel}>
      <h2 id="delete-received-title">{t('deleteReceived.title', { name })}</h2>
      <p id="delete-received-body">{t('deleteReceived.body')}</p>
      <div className="dialog-actions">
        <button type="button" onClick={onCancel} autoFocus>
          {t('deleteReceived.cancel')}
        </button>
        <button type="button" className="danger" onClick={onConfirm}>
          {t('deleteReceived.confirm')}
        </button>
      </div>
    </Dialog>
  )
}
