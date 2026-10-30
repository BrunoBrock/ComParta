import { useEffect, useRef, useState } from 'react'
import type { Bridge, ErrorCode } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import { drawQr, type QrDrawing } from '../qr.ts'
import Dialog from './ui/Dialog.tsx'

interface QrCodeDialogProps {
  bridge: Bridge
  id: string
  name: string
  onClose: () => void
  onError: (code: ErrorCode) => void
}

/**
 * The invite as a QR code (epic 7 story 9): another phone scans it in Receive, or with its own
 * camera app, and "Get this file?" opens there. Only the link is in the code, as Copy link copies it.
 */
export default function QrCodeDialog({ bridge, id, name, onClose, onError }: QrCodeDialogProps) {
  // undefined while the link is on its way; null when it is too long for a QR code.
  const [drawing, setDrawing] = useState<QrDrawing | null | undefined>(undefined)
  // The details window passes new functions each second as it refreshes; the link is asked for once.
  const answer = useRef({ onClose, onError })
  useEffect(() => {
    answer.current = { onClose, onError }
  })

  useEffect(() => {
    let active = true
    void bridge.invoke('shares.inviteLink', { id }).then((result) => {
      if (!active) return
      if (result.ok) setDrawing(drawQr(result.value))
      else {
        answer.current.onError(result.error.code)
        answer.current.onClose()
      }
    })
    return () => {
      active = false
    }
  }, [bridge, id])

  return (
    <Dialog labelledBy="qr-title" className="qr" onClose={onClose}>
      <h2 id="qr-title">{t('qr.title', { name })}</h2>
      {drawing === null ? (
        <p className="notice notice-error" role="alert">
          {t('qr.tooLong')}
        </p>
      ) : (
        <div className="qr-code" aria-busy={drawing === undefined}>
          {drawing && (
            <svg
              role="img"
              aria-label={t('qr.label', { name })}
              viewBox={`0 0 ${drawing.size} ${drawing.size}`}
              shapeRendering="crispEdges"
            >
              <rect width={drawing.size} height={drawing.size} fill="#fff" />
              <path d={drawing.path} fill="#000" />
            </svg>
          )}
        </div>
      )}
      <p className="hint">{t('qr.hint', { receive: t('transfers.open'), scan: t('receive.scan') })}</p>
      <div className="dialog-actions">
        <button type="button" className="primary" onClick={onClose} autoFocus>
          {t('details.close')}
        </button>
      </div>
    </Dialog>
  )
}
