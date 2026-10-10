import { FileDown } from 'lucide-react'
import { type FormEvent, useEffect, useRef, useState } from 'react'
import type { Bridge } from '../bridge/index.ts'
import { errorMessage, t } from '../i18n/index.ts'

interface ReceiveDialogProps {
  bridge: Bridge
  /** The transfer started; the dialog closes. */
  onStarted: () => void
  onCancel: () => void
}

/**
 * One large box for an invite or a download link, like attaching something in a chat, and a
 * second way in for a download file saved from a website (AD-21).
 */
export default function ReceiveDialog({ bridge, onStarted, onCancel }: ReceiveDialogProps) {
  const dialog = useRef<HTMLDialogElement>(null)
  // View state only: the text being typed, a request on its way, and the last problem.
  const [invite, setInvite] = useState('')
  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)

  useEffect(() => {
    const element = dialog.current
    if (element && !element.open) element.showModal()
  }, [])

  async function receive(event: FormEvent) {
    event.preventDefault()
    setProblem(null)
    setBusy(true)
    try {
      const result = await bridge.invoke('transfers.receive', { invite })
      if (result.ok) onStarted()
      else setProblem(errorMessage(result.error.code))
    } finally {
      setBusy(false)
    }
  }

  async function openFile() {
    setProblem(null)
    setBusy(true)
    try {
      const result = await bridge.invoke('transfers.openFile')
      // null means the person closed the file dialog; the dialog stays as it was.
      if (result.ok && result.value !== null) onStarted()
      else if (!result.ok) setProblem(errorMessage(result.error.code))
    } finally {
      setBusy(false)
    }
  }

  return (
    <dialog
      ref={dialog}
      aria-labelledby="receive-title"
      onCancel={(event) => {
        event.preventDefault()
        onCancel()
      }}
    >
      <form onSubmit={receive}>
        <h2 id="receive-title">{t('receive.title')}</h2>
        <label htmlFor="invite-input">{t('transfers.inviteLabel')}</label>
        <textarea
          id="invite-input"
          value={invite}
          onChange={(event) => setInvite(event.target.value)}
          rows={4}
          autoComplete="off"
          spellCheck={false}
          autoFocus
        />
        <p className="hint">{t('receive.hint')}</p>
        <div className="receive-file">
          <span className="hint">{t('receive.or')}</span>
          <button type="button" onClick={openFile} disabled={busy}>
            <FileDown size={18} aria-hidden="true" />
            {t('receive.openFile')}
          </button>
          <span className="hint">{t('receive.openFileHint')}</span>
        </div>
        {problem && (
          <p className="notice notice-error" role="alert">
            {problem}
          </p>
        )}
        <div className="dialog-actions">
          <button type="button" onClick={onCancel}>
            {t('receive.cancel')}
          </button>
          <button type="submit" className="primary" disabled={busy || invite.trim() === ''}>
            {t('transfers.receive')}
          </button>
        </div>
      </form>
    </dialog>
  )
}
