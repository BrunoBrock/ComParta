import { t } from '../i18n/index.ts'
import Button from './ui/Button.tsx'
import Dialog from './ui/Dialog.tsx'

interface HelpDialogProps {
  onConnection: () => void
  onClose: () => void
}

/** The whole idea in three steps, and the way to the connection help (UX design, Help). */
export default function HelpDialog({ onConnection, onClose }: HelpDialogProps) {
  return (
    <Dialog labelledBy="help-title" onClose={onClose}>
      <h2 id="help-title">{t('help.title')}</h2>
      <ol className="help-steps">
        <li>{t('help.share')}</li>
        <li>{t('help.invite')}</li>
        <li>{t('help.receive')}</li>
      </ol>
      <div className="dialog-actions">
        <Button variant="quiet" onClick={onConnection}>
          {t('connection.title')}
        </Button>
        <Button onClick={onClose} autoFocus>
          {t('help.close')}
        </Button>
      </div>
    </Dialog>
  )
}
