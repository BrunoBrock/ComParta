import { FolderLock, MonitorUp, Send, ShieldCheck } from 'lucide-react'
import { type FormEvent, useState } from 'react'
import { t } from '../i18n/index.ts'
import { maxNameLength } from './SettingsDialog.tsx'
import Button from './ui/Button.tsx'
import Dialog from './ui/Dialog.tsx'

export interface WelcomeChoices {
  yourName: string
  startWithWindows: boolean
}

interface WelcomeProps {
  /** Finished with the person's choices, or skipped (null): nothing else changes then. */
  onDone: (choices: WelcomeChoices | null) => void
}

const cards = [
  { Icon: Send, text: 'welcome.card1' },
  { Icon: FolderLock, text: 'welcome.card2' },
  { Icon: MonitorUp, text: 'welcome.card3' },
] as const

/**
 * The first-run welcome (epic 12 story 3, UX design, First run): three cards, then the name and
 * start-with-Windows form, then a word about the network question Windows asks. Skip goes straight
 * to the app.
 */
export default function Welcome({ onDone }: WelcomeProps) {
  // 0 to 2 are the cards, 3 the form, 4 the note about Windows' question.
  const [step, setStep] = useState(0)
  const [yourName, setYourName] = useState('')
  const [startWithWindows, setStartWithWindows] = useState(true)
  const skip = () => onDone(null)

  function next(event?: FormEvent) {
    event?.preventDefault()
    if (step < 4) setStep(step + 1)
    else onDone({ yourName: yourName.trim(), startWithWindows })
  }

  function shown() {
    if (step < cards.length) {
      const { Icon, text } = cards[step]
      return (
        <>
          <Icon className="welcome-picture" size={64} strokeWidth={1.5} aria-hidden="true" />
          <h2 id="welcome-title">{t(text)}</h2>
        </>
      )
    }
    if (step === 3)
      return (
        <>
          <h2 id="welcome-title">{t('welcome.formTitle')}</h2>
          <label htmlFor="welcome-name">{t('welcome.name')}</label>
          <input
            id="welcome-name"
            className="settings-select"
            type="text"
            autoComplete="name"
            maxLength={maxNameLength}
            value={yourName}
            placeholder={t('invite.someone')}
            aria-describedby="welcome-name-hint"
            onChange={(event) => setYourName(event.target.value)}
          />
          <p id="welcome-name-hint" className="hint">
            {t('welcome.nameHint')}
          </p>
          <label className="settings-check">
            <input type="checkbox" checked={startWithWindows} onChange={(event) => setStartWithWindows(event.target.checked)} />
            {t('settings.startWithWindows')}
          </label>
        </>
      )
    return (
      <>
        <ShieldCheck className="welcome-picture" size={64} strokeWidth={1.5} aria-hidden="true" />
        <h2 id="welcome-title">{t('welcome.networkTitle')}</h2>
        <p>{t('welcome.network')}</p>
      </>
    )
  }

  return (
    <Dialog labelledBy="welcome-title" className="welcome" onClose={skip}>
      <form onSubmit={next}>
        <p className="steps-count">{t('welcome.count', { step: step + 1, total: 5 })}</p>
        {shown()}
        <div className="dialog-actions">
          {step < 4 && (
            <button type="button" className="link" onClick={skip}>
              {t('welcome.skip')}
            </button>
          )}
          <Button submit variant="primary" autoFocus>
            {t(step < 4 ? 'welcome.next' : 'welcome.start')}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}
