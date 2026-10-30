import jsQR from 'jsqr'
import { useEffect, useRef, useState } from 'react'
import { t } from '../i18n/index.ts'

interface QrScannerProps {
  /** The text in the first QR code the camera sees; the camera is already off. */
  onFound: (text: string) => void
}

/** How often a picture from the camera is read: often enough to feel instant, without heating the phone. */
export const scanEveryMs = 150
/** Pictures are read at most this wide; a QR code held up to the camera fills most of it anyway. */
const readWidth = 640

/**
 * The phone's back camera, read for a QR code (epic 7 story 9). The camera is asked for only now,
 * because the person chose "Scan a QR code"; the phone asks them the first time. It is turned off
 * as soon as a code is read or the person leaves.
 */
export default function QrScanner({ onFound }: QrScannerProps) {
  const video = useRef<HTMLVideoElement>(null)
  const [noCamera, setNoCamera] = useState(false)
  const found = useRef(onFound)
  useEffect(() => {
    found.current = onFound
  })

  useEffect(() => {
    let active = true
    let stream: MediaStream | null = null
    let timer: ReturnType<typeof setTimeout> | undefined
    const canvas = document.createElement('canvas')
    const stop = () => {
      active = false
      clearTimeout(timer)
      stream?.getTracks().forEach((track) => track.stop())
    }

    const look = () => {
      if (!active) return
      const element = video.current
      const context = canvas.getContext('2d', { willReadFrequently: true })
      if (element && context && element.videoWidth > 0 && element.readyState >= element.HAVE_CURRENT_DATA) {
        const scale = Math.min(1, readWidth / element.videoWidth)
        canvas.width = Math.round(element.videoWidth * scale)
        canvas.height = Math.round(element.videoHeight * scale)
        context.drawImage(element, 0, 0, canvas.width, canvas.height)
        const picture = context.getImageData(0, 0, canvas.width, canvas.height)
        const code = jsQR(picture.data, picture.width, picture.height)
        if (code?.data) {
          stop()
          found.current(code.data)
          return
        }
      }
      timer = setTimeout(look, scanEveryMs)
    }

    navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false }).then(
      (camera) => {
        if (!active) {
          camera.getTracks().forEach((track) => track.stop())
          return
        }
        stream = camera
        if (video.current) {
          video.current.srcObject = camera
          // The video only shows what the camera sees; reading goes on even if it never plays.
          Promise.resolve(video.current.play()).catch(() => {})
        }
        look()
      },
      () => {
        if (active) setNoCamera(true)
      },
    )
    return stop
  }, [])

  return (
    <>
      {noCamera ? (
        <p className="notice notice-error" role="alert">
          {t('scan.noCamera')}
        </p>
      ) : (
        <video ref={video} className="qr-camera" aria-label={t('scan.camera')} muted playsInline />
      )}
      <p className="hint">{t('scan.hint')}</p>
    </>
  )
}
