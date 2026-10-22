import { t } from '../i18n/index.ts'
import Dialog from './ui/Dialog.tsx'

interface RiskyOpenDialogProps {
  name: string
  onConfirm: () => void
  onCancel: () => void
}

/** Asks once more before opening a file that can run a program; cancelling is the safe, focused choice. */
export default function RiskyOpenDialog({ name, onConfirm, onCancel }: RiskyOpenDialogProps) {
  return (
    <Dialog labelledBy="risky-open-title" describedBy="risky-open-body" onClose={onCancel}>
      <h2 id="risky-open-title">{t('riskyOpen.title', { name })}</h2>
      <p id="risky-open-body">{t('riskyOpen.body')}</p>
      <div className="dialog-actions">
        <button type="button" onClick={onCancel} autoFocus>
          {t('riskyOpen.cancel')}
        </button>
        <button type="button" className="danger" onClick={onConfirm}>
          {t('riskyOpen.confirm')}
        </button>
      </div>
    </Dialog>
  )
}
