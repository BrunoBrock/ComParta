import { ChevronLeft, ChevronRight, X, ZoomIn, ZoomOut } from 'lucide-react'
import { type KeyboardEvent, type PointerEvent, useEffect, useRef, useState } from 'react'
import type { Bridge, ErrorCode } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import Dialog from './ui/Dialog.tsx'

export type ViewedItem = { kind: 'share' | 'transfer'; id: string; name: string }

interface PictureViewerProps {
  bridge: Bridge
  /** The pictures the person can move between, in the order the screen shows them. */
  pictures: ViewedItem[]
  /** The one opened first. */
  start: number
  onClose: () => void
  onError: (code: ErrorCode) => void
}

/** How far each zoom step goes, and the limits (UX design, picture viewer). */
const zoomStep = 1.5
const largestZoom = 8
/** A swipe shorter than this is a tap, not a move to the next picture. */
export const swipePixels = 50

/**
 * A picture full size over the home (epic 18 story 2, AD-26): the app gives an address that reads
 * this one file, never its path. Zoom with the buttons, + and - or the mouse wheel; move between
 * pictures with the arrows, the arrow keys or a swipe; Close or Esc goes back.
 */
export default function PictureViewer({ bridge, pictures, start, onClose, onError }: PictureViewerProps) {
  const [index, setIndex] = useState(start)
  const [address, setAddress] = useState<string | null>(null)
  const [zoom, setZoom] = useState(1)
  const swipeFrom = useRef<number | null>(null)
  const picture = pictures[index]

  useEffect(() => {
    let current = true
    setAddress(null)
    setZoom(1)
    void bridge.invoke('media.open', { kind: picture.kind, id: picture.id }).then((result) => {
      if (!current) return
      if (result.ok) setAddress(result.value.url)
      else onError(result.error.code)
    })
    return () => {
      current = false
    }
  }, [bridge, picture.kind, picture.id, onError])

  const hasPrevious = index > 0
  const hasNext = index < pictures.length - 1
  const previous = () => hasPrevious && setIndex(index - 1)
  const next = () => hasNext && setIndex(index + 1)
  const zoomIn = () => setZoom((z) => Math.min(largestZoom, z * zoomStep))
  const zoomOut = () => setZoom((z) => Math.max(1, z / zoomStep))

  function onKeyDown(event: KeyboardEvent) {
    const actions: Record<string, () => void> = {
      ArrowLeft: previous,
      ArrowRight: next,
      '+': zoomIn,
      '=': zoomIn,
      '-': zoomOut,
      '0': () => setZoom(1),
    }
    const action = actions[event.key]
    if (!action) return
    event.preventDefault()
    action()
  }

  function onPointerUp(event: PointerEvent) {
    const from = swipeFrom.current
    swipeFrom.current = null
    // A zoomed picture is moved with the scroll bars or a finger; only a whole picture swipes.
    if (from === null || zoom > 1) return
    const moved = event.clientX - from
    if (moved <= -swipePixels) next()
    else if (moved >= swipePixels) previous()
  }

  return (
    <Dialog labelledBy="viewer-title" className="viewer" onClose={onClose}>
      <div className="viewer-frame" onKeyDown={onKeyDown}>
        <header className="viewer-bar">
          <h2 id="viewer-title">{picture.name}</h2>
          {pictures.length > 1 && (
            <span className="viewer-count">{t('viewer.count', { number: index + 1, total: pictures.length })}</span>
          )}
          <button type="button" onClick={zoomOut} disabled={zoom === 1} aria-label={t('viewer.zoomOut')}>
            <ZoomOut size={20} aria-hidden="true" />
          </button>
          <button type="button" onClick={zoomIn} disabled={zoom >= largestZoom} aria-label={t('viewer.zoomIn')}>
            <ZoomIn size={20} aria-hidden="true" />
          </button>
          <button type="button" onClick={onClose} aria-label={t('viewer.close')} autoFocus>
            <X size={20} aria-hidden="true" />
          </button>
        </header>
        <div
          className="viewer-stage"
          data-zoomed={zoom > 1}
          onPointerDown={(event) => {
            swipeFrom.current = event.clientX
          }}
          onPointerUp={onPointerUp}
          onWheel={(event) => (event.deltaY < 0 ? zoomIn() : zoomOut())}
        >
          {address ? (
            <img src={address} alt={picture.name} style={{ width: `${zoom * 100}%`, height: zoom > 1 ? 'auto' : undefined }} draggable={false} />
          ) : (
            <p className="viewer-waiting" aria-busy="true">
              {t('viewer.opening')}
            </p>
          )}
        </div>
        {hasPrevious && (
          <button type="button" className="viewer-turn viewer-previous" onClick={previous} aria-label={t('viewer.previous')}>
            <ChevronLeft size={28} aria-hidden="true" />
          </button>
        )}
        {hasNext && (
          <button type="button" className="viewer-turn viewer-next" onClick={next} aria-label={t('viewer.next')}>
            <ChevronRight size={28} aria-hidden="true" />
          </button>
        )}
      </div>
    </Dialog>
  )
}
