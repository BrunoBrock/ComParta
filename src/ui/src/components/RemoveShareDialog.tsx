import { useEffect, useRef } from 'react'
import type { ShareDto } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'

interface RemoveShareDialogProps {
  share: ShareDto
  onConfirm: () => void
  onCancel: () => void
}

/** Asks before removing and says plainly that the original file is kept. */
export default function RemoveShareDialog({ share, onConfirm, onCancel }: RemoveShareDialogProps) {
  const dialog = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const element = dialog.current
    if (element && !element.open) element.showModal()
  }, [])

  return (
    <dialog
      ref={dialog}
      aria-labelledby="remove-title"
      aria-describedby="remove-body"
      onCancel={(event) => {
        event.preventDefault()
        onCancel()
      }}
    >
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
    </dialog>
  )
}
