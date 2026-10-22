import { FileDown } from 'lucide-react'
import { type FormEvent, type ReactNode, useEffect, useRef, useState } from 'react'
import type { Bridge, InvitePreviewDto } from '../bridge/index.ts'
import { errorMessage, formatSize, t } from '../i18n/index.ts'
import FileIcon from './ui/FileIcon.tsx'
import Dialog from './ui/Dialog.tsx'

interface ReceiveDialogProps {
  bridge: Bridge
  /**
   * Text pasted elsewhere in the window, or a download file opened in Windows: the dialog opens
   * straight on the confirmation.
   */
  pasted?: { text: string; preview: InvitePreviewDto; openedFile?: boolean }
  /** The transfer started; the dialog closes. */
  onStarted: () => void
  onCancel: () => void
}

/**
 * One large box for an invite or a download link, like attaching something in a chat, and a
 * second way in for a download file saved from a website (AD-21). Nothing is received until the
 * person has seen what it is and chosen "Get the file" (UX design, Receive a file).
 */
export default function ReceiveDialog({ bridge, pasted, onStarted, onCancel }: ReceiveDialogProps) {
  // View state only: the text, a request on its way, the last problem, and what the invite says.
  const [invite, setInvite] = useState(pasted?.text ?? '')
  const [found, setFound] = useState(false)
  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)
  const [preview, setPreview] = useState<InvitePreviewDto | null>(pasted?.preview ?? null)
  const typed = useRef(false)

  useEffect(() => {
    if (pasted) return
    let active = true
    // The clipboard is read only now, because the person opened this dialog (AD-16).
    void bridge.invoke('clipboard.findInvite').then((result) => {
      if (!active || !result.ok || !result.value || typed.current) return
      setInvite(result.value)
      setFound(true)
    })
    return () => {
      active = false
    }
  }, [bridge, pasted])

  async function run<T>(action: () => Promise<T>) {
    setProblem(null)
    setBusy(true)
    try {
      return await action()
    } finally {
      setBusy(false)
    }
  }

  async function look(event: FormEvent) {
    event.preventDefault()
    await run(async () => {
      const result = await bridge.invoke('transfers.preview', { invite })
      if (result.ok) setPreview(result.value)
      else setProblem(errorMessage(result.error.code))
    })
  }

  async function receive() {
    await run(async () => {
      const result = pasted?.openedFile
        ? await bridge.invoke('transfers.receiveOpenedFile')
        : await bridge.invoke('transfers.receive', { invite })
      if (result.ok) onStarted()
      else setProblem(errorMessage(result.error.code))
    })
  }

  async function openFile() {
    await run(async () => {
      const result = await bridge.invoke('transfers.openFile')
      // null means the person closed the file dialog; the dialog stays as it was.
      if (result.ok && result.value !== null) onStarted()
      else if (!result.ok) setProblem(errorMessage(result.error.code))
    })
  }

  const alert = problem && (
    <p className="notice notice-error" role="alert">
      {problem}
    </p>
  )

  return (
    <Dialog labelledBy="receive-title" onClose={onCancel}>
      {preview ? (
        <Confirmation preview={preview} busy={busy} alert={alert} onReceive={receive} onCancel={onCancel} />
      ) : (
        <form onSubmit={look}>
          <h2 id="receive-title">{t('receive.title')}</h2>
          <label htmlFor="invite-input">{t('transfers.inviteLabel')}</label>
          <textarea
            id="invite-input"
            value={invite}
            onChange={(event) => {
              typed.current = true
              setFound(false)
              setInvite(event.target.value)
            }}
            rows={4}
            autoComplete="off"
            spellCheck={false}
            autoFocus
          />
          <p className="hint" role={found ? 'status' : undefined}>
            {found ? t('receive.found') : t('receive.hint')}
          </p>
          <div className="receive-file">
            <span className="hint">{t('receive.or')}</span>
            <button type="button" onClick={openFile} disabled={busy}>
              <FileDown size={18} aria-hidden="true" />
              {t('receive.openFile')}
            </button>
            <span className="hint">{t('receive.openFileHint')}</span>
          </div>
          {alert}
          <div className="dialog-actions">
            <button type="button" onClick={onCancel}>
              {t('receive.cancel')}
            </button>
            <button type="submit" className="primary" disabled={busy || invite.trim() === ''}>
              {t('receive.continue')}
            </button>
          </div>
        </form>
      )}
    </Dialog>
  )
}

interface ConfirmationProps {
  preview: InvitePreviewDto
  busy: boolean
  alert: ReactNode
  onReceive: () => void
  onCancel: () => void
}

/** "Get this file?": what the invite says, where it goes, and whether there is room. */
function Confirmation({ preview, busy, alert, onReceive, onCancel }: ConfirmationProps) {
  const name = preview.name ?? t('confirm.unnamed')
  const noRoom =
    preview.sizeBytes !== null && preview.freeBytes !== null && preview.sizeBytes > preview.freeBytes
  const details = [
    preview.sizeBytes !== null ? formatSize(preview.sizeBytes) : null,
    preview.senderName ? t('confirm.from', { sender: preview.senderName }) : null,
  ].filter(Boolean)

  return (
    <div className="confirm">
      <h2 id="receive-title">{t('confirm.title')}</h2>
      <div className="confirm-item">
        <FileIcon name={name} />
        <div>
          <p className="confirm-name">{name}</p>
          {details.length > 0 && <p className="hint">{details.join(' · ')}</p>}
          <p className="hint">{t(preview.isInvite ? 'confirm.fromInvite' : 'confirm.fromLink')}</p>
        </div>
      </div>
      <p>{t('confirm.where')}</p>
      {noRoom ? (
        <p className="notice notice-error" role="alert">
          {t('confirm.noRoom', { needed: formatSize(preview.sizeBytes! - preview.freeBytes!) })}
        </p>
      ) : (
        preview.freeBytes !== null && <p>{t('confirm.free', { free: formatSize(preview.freeBytes) })}</p>
      )}
      {alert}
      <div className="dialog-actions">
        <button type="button" onClick={onCancel}>
          {t('receive.cancel')}
        </button>
        <button type="button" className="primary" onClick={onReceive} disabled={busy || noRoom} autoFocus>
          {t('transfers.receive')}
        </button>
      </div>
    </div>
  )
}
