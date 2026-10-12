import { t } from '../i18n/index.ts'
import Dialog from './ui/Dialog.tsx'

interface QuitDialogProps {
  /** The files that would stop moving. */
  moving: readonly string[]
  onConfirm: () => void
  onCancel: () => void
}

/** Asks before quitting would cut someone off (AD-14); keeping ComParta running is the safe, focused choice. */
export default function QuitDialog({ moving, onConfirm, onCancel }: QuitDialogProps) {
  return (
    <Dialog labelledBy="quit-title" describedBy="quit-body" onClose={onCancel}>
      <h2 id="quit-title">{t('quit.title')}</h2>
      <p id="quit-body">{t('quit.body')}</p>
      <ul className="quit-moving">
        {moving.map((name, index) => (
          <li key={`${index}-${name}`}>{name}</li>
        ))}
      </ul>
      <div className="dialog-actions">
        <button type="button" onClick={onCancel} autoFocus>
          {t('quit.cancel')}
        </button>
        <button type="button" className="danger" onClick={onConfirm}>
          {t('quit.confirm')}
        </button>
      </div>
    </Dialog>
  )
}
