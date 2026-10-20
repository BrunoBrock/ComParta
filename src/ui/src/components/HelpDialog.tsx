import { t } from '../i18n/index.ts'
import Button from './ui/Button.tsx'
import Dialog from './ui/Dialog.tsx'

interface HelpDialogProps {
  onConnection: () => void
  onClose: () => void
}

/** The questions people ask, in the order they usually ask them (epic 12 story 4). */
export const helpQuestions = [
  'what',
  'needsApp',
  'where',
  'copied',
  'looking',
  'howLong',
  'safe',
  'strangers',
  'stop',
  'getHelp',
] as const

/**
 * The whole idea in three steps, the questions people ask with their answers, and the way to the
 * connection help (UX design, Help). Everything is in the app, so it works with no internet.
 */
export default function HelpDialog({ onConnection, onClose }: HelpDialogProps) {
  return (
    <Dialog labelledBy="help-title" className="help" onClose={onClose}>
      <h2 id="help-title">{t('help.title')}</h2>
      <ol className="help-steps">
        <li>{t('help.share')}</li>
        <li>{t('help.invite')}</li>
        <li>{t('help.receive')}</li>
      </ol>
      <h3>{t('help.questions')}</h3>
      <div className="help-questions">
        {helpQuestions.map((question) => (
          <details key={question}>
            <summary>{t(`help.q.${question}`)}</summary>
            <p>{t(`help.a.${question}`)}</p>
          </details>
        ))}
      </div>
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
