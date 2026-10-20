import { type FormEvent, useState } from 'react'
import type { Bridge, ErrorCode } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import Button from './ui/Button.tsx'
import Dialog from './ui/Dialog.tsx'

interface NameFilesDialogProps {
  bridge: Bridge
  /** How many files were chosen together. */
  count: number
  /** Called once they are shared, or forgotten when the person cancels. */
  onClose: (shared: boolean) => void
  onError: (code: ErrorCode) => void
}

/**
 * Several files chosen together become one share, and the person names it; the other computer
 * gets them in a folder of that name (epic share-a-folder story 2, AD-19).
 */
export default function NameFilesDialog({ bridge, count, onClose, onError }: NameFilesDialogProps) {
  const [name, setName] = useState(() => t('shares.nameDefault', { count }))
  const [invalid, setInvalid] = useState(false)
  const [saving, setSaving] = useState(false)

  async function share(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    const result = await bridge.invoke('shares.addPicked', { name })
    setSaving(false)
    if (result.ok) return onClose(result.value !== null)
    if (result.error.code === 'share.nameInvalid') return setInvalid(true)
    onError(result.error.code)
    onClose(false)
  }

  async function cancel() {
    await bridge.invoke('shares.addPicked', { name: null })
    onClose(false)
  }

  return (
    <Dialog labelledBy="name-files-title" describedBy="name-files-hint" onClose={() => void cancel()} busy={saving}>
      <form onSubmit={(event) => void share(event)}>
        <h2 id="name-files-title">{t('shares.nameTitle', { count })}</h2>
        <p id="name-files-hint" className="hint">
          {t('shares.nameHint')}
        </p>
        <label htmlFor="name-files">{t('shares.nameLabel')}</label>
        <input
          id="name-files"
          className="settings-select"
          type="text"
          maxLength={100}
          autoFocus
          value={name}
          aria-invalid={invalid}
          onChange={(event) => {
            setName(event.target.value)
            setInvalid(false)
          }}
        />
        {invalid && (
          <p role="alert" className="settings-problem">
            {t('error.share.nameInvalid')}
          </p>
        )}
        <div className="dialog-actions">
          <Button onClick={() => void cancel()} disabled={saving}>
            {t('shares.nameCancel')}
          </Button>
          <Button submit variant="primary" disabled={saving}>
            {t('shares.nameShare')}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}
