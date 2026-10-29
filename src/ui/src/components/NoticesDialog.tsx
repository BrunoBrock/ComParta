import { useEffect, useState } from 'react'
import type { Bridge, ErrorCode } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import Button from './ui/Button.tsx'
import Dialog from './ui/Dialog.tsx'

interface NoticesDialogProps {
  bridge: Bridge
  onError: (code: ErrorCode) => void
  onClose: () => void
}

/**
 * The licenses of the parts ComParta uses, opened from Settings, About (epic 5 story 1). The text
 * is the legal notices file shipped with the app, shown as it is; the screen never knows where it lives (AD-7).
 */
export default function NoticesDialog({ bridge, onError, onClose }: NoticesDialogProps) {
  const [text, setText] = useState<string | null>(null)

  useEffect(() => {
    let shown = true
    void bridge.invoke('about.notices').then((result) => {
      if (!shown) return
      if (result.ok) setText(result.value)
      else {
        onError(result.error.code)
        onClose()
      }
    })
    return () => {
      shown = false
    }
  }, [bridge, onError, onClose])

  return (
    <Dialog labelledBy="notices-title" className="notices" onClose={onClose}>
      <h2 id="notices-title">{t('notices.title')}</h2>
      <p>{t('notices.intro')}</p>
      {/* Scrolls on its own, so it can be reached and read with the keyboard. */}
      <pre className="notices-text" role="region" tabIndex={0} aria-labelledby="notices-title" aria-busy={text === null}>
        {text}
      </pre>
      <div className="dialog-actions">
        <Button onClick={onClose} autoFocus>
          {t('settings.close')}
        </Button>
      </div>
    </Dialog>
  )
}
