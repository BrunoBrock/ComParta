import { ClipboardCopy } from 'lucide-react'
import { useState } from 'react'
import type { Bridge, ErrorCode } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import Button from './ui/Button.tsx'
import Dialog from './ui/Dialog.tsx'

interface HelpDialogProps {
  bridge: Bridge
  onConnection: () => void
  onError: (code: ErrorCode) => void
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
export default function HelpDialog({ bridge, onConnection, onError, onClose }: HelpDialogProps) {
  // The report as it was copied, shown whole so the person sees there is nothing private in it (epic 12 story 5).
  const [report, setReport] = useState<string | null>(null)

  async function copyReport() {
    const result = await bridge.invoke('support.report')
    if (result.ok) setReport(result.value)
    else onError(result.error.code)
  }

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
      <section className="help-report" aria-labelledby="help-report-title">
        <h3 id="help-report-title">{t('help.report')}</h3>
        <p className="hint">{t('help.reportHint')}</p>
        <Button onClick={() => void copyReport()}>
          <ClipboardCopy size={18} aria-hidden="true" />
          {t('help.report')}
        </Button>
        {report !== null && (
          <>
            <p role="status">{t('help.reportCopied')}</p>
            {/* Scrolls on its own, so it can be reached and read with the keyboard. */}
            <pre className="help-report-text" tabIndex={0} aria-label={t('help.reportText')}>
              {report}
            </pre>
          </>
        )}
      </section>
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
