import { ChevronLeft, ChevronRight, X, ZoomIn, ZoomOut } from 'lucide-react'
import { type KeyboardEvent, type PointerEvent, useEffect, useRef, useState } from 'react'
import type { Bridge, ErrorCode, MediaKindDto } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import Dialog from './ui/Dialog.tsx'

export type ViewedItem = { kind: 'share' | 'transfer'; id: string; name: string }

interface MediaViewerProps {
  bridge: Bridge
  /** The files the person can move between, in the order the screen shows them. */
  items: ViewedItem[]
  /** The one opened first. */
  start: number
  onClose: () => void
  onError: (code: ErrorCode) => void
}

/** How far each zoom step goes, and the limit (UX design, viewer). */
const zoomStep = 1.5
const largestZoom = 8
/** A swipe shorter than this is a tap, not a move to the next file. */
export const swipePixels = 50
/** How far the arrow keys move a song or video. */
export const seekSeconds = 5

/**
 * A picture, song or video inside ComParta, over the home (epic 18 stories 2 and 3, AD-26). The
 * app gives an address that reads this one file, never its path, and the web view's own player
 * plays it with the controls everyone knows.
 *
 * Pictures: zoom with the buttons, + and - or the wheel; the arrow keys or a swipe move between files.
 * Songs and videos: Space plays and pauses, the arrow keys go back or forward a few seconds,
 * M turns the sound off and on, F fills the screen with a video. Page Up and Page Down move between
 * files. Close or Esc goes back, and whatever was playing stops.
 */
export default function MediaViewer({ bridge, items, start, onClose, onError }: MediaViewerProps) {
  const [index, setIndex] = useState(start)
  const [opened, setOpened] = useState<{ url: string; kind: MediaKindDto } | null>(null)
  const [zoom, setZoom] = useState(1)
  // The file cannot be shown or played here, or stopped playing part way (epic 18 story 4).
  const [cannotPlay, setCannotPlay] = useState(false)
  const swipeFrom = useRef<number | null>(null)
  const player = useRef<HTMLMediaElement | null>(null)
  const item = items[index]

  useEffect(() => {
    let current = true
    setOpened(null)
    setZoom(1)
    setCannotPlay(false)
    void bridge.invoke('media.open', { kind: item.kind, id: item.id }).then((result) => {
      if (!current) return
      if (result.ok) setOpened({ url: result.value.url, kind: result.value.kind })
      else if (result.error.code === 'media.notPlayable') setCannotPlay(true)
      else {
        onError(result.error.code)
        onClose()
      }
    })
    return () => {
      current = false
    }
  }, [bridge, item.kind, item.id, onError, onClose])

  // Open the folder: the file selected in Explorer, or the phone's files app; then back home.
  async function openFolder() {
    const result = await bridge.invoke(item.kind === 'share' ? 'shares.reveal' : 'received.reveal', { id: item.id })
    if (!result.ok) onError(result.error.code)
    onClose()
  }

  // Playback stops with the viewer, and when the person moves to another file.
  useEffect(() => {
    const element = player.current
    return () => element?.pause()
  }, [opened])

  const hasPrevious = index > 0
  const hasNext = index < items.length - 1
  const previous = () => hasPrevious && setIndex(index - 1)
  const next = () => hasNext && setIndex(index + 1)
  const zoomIn = () => setZoom((z) => Math.min(largestZoom, z * zoomStep))
  const zoomOut = () => setZoom((z) => Math.max(1, z / zoomStep))
  const playing = opened?.kind === 'song' || opened?.kind === 'video'

  function seek(seconds: number) {
    const element = player.current
    if (!element) return
    const end = Number.isFinite(element.duration) ? element.duration : Infinity
    element.currentTime = Math.min(end, Math.max(0, element.currentTime + seconds))
  }

  function playOrPause() {
    const element = player.current
    if (!element) return
    if (element.paused) void element.play()?.catch(() => undefined)
    else element.pause()
  }

  function onKeyDown(event: KeyboardEvent) {
    // Keys pressed on the player's own controls are left to them.
    if (event.target instanceof HTMLMediaElement) return
    const actions: Record<string, () => void> = playing
      ? {
          ' ': playOrPause,
          k: playOrPause,
          ArrowLeft: () => seek(-seekSeconds),
          ArrowRight: () => seek(seekSeconds),
          m: () => {
            if (player.current) player.current.muted = !player.current.muted
          },
          f: () => {
            if (opened?.kind === 'video') void player.current?.requestFullscreen?.()?.catch(() => undefined)
          },
          PageUp: previous,
          PageDown: next,
        }
      : {
          ArrowLeft: previous,
          ArrowRight: next,
          PageUp: previous,
          PageDown: next,
          '+': zoomIn,
          '=': zoomIn,
          '-': zoomOut,
          '0': () => setZoom(1),
        }
    const action = actions[event.key.length === 1 ? event.key.toLowerCase() : event.key]
    if (!action) return
    event.preventDefault()
    action()
  }

  function onPointerUp(event: PointerEvent) {
    const from = swipeFrom.current
    swipeFrom.current = null
    // A zoomed picture is moved with the scroll bars or a finger; only a whole picture swipes.
    if (from === null || zoom > 1 || playing) return
    const moved = event.clientX - from
    if (moved <= -swipePixels) next()
    else if (moved >= swipePixels) previous()
  }

  function shown() {
    if (cannotPlay)
      return (
        <div className="viewer-cannot" role="alert">
          <p>{t('error.media.notPlayable')}</p>
          <div className="dialog-actions">
            <button type="button" onClick={onClose}>
              {t('viewer.cancel')}
            </button>
            <button type="button" className="primary" onClick={() => void openFolder()}>
              {t('viewer.openFolder')}
            </button>
          </div>
        </div>
      )
    if (!opened)
      return (
        <p className="viewer-waiting" aria-busy="true">
          {t('viewer.opening')}
        </p>
      )
    if (opened.kind === 'video')
      return <video onError={() => setCannotPlay(true)} ref={(element) => { player.current = element }} src={opened.url} controls autoPlay aria-label={item.name} />
    if (opened.kind === 'song')
      return <audio onError={() => setCannotPlay(true)} ref={(element) => { player.current = element }} src={opened.url} controls autoPlay aria-label={item.name} />
    return (
      <img
        onError={() => setCannotPlay(true)}
        src={opened.url}
        alt={item.name}
        style={{ width: `${zoom * 100}%`, height: zoom > 1 ? 'auto' : undefined }}
        draggable={false}
      />
    )
  }

  return (
    <Dialog labelledBy="viewer-title" className="viewer" onClose={onClose}>
      <div className="viewer-frame" onKeyDown={onKeyDown}>
        <header className="viewer-bar">
          <h2 id="viewer-title">{item.name}</h2>
          {items.length > 1 && (
            <span className="viewer-count">{t('viewer.count', { number: index + 1, total: items.length })}</span>
          )}
          {!playing && !cannotPlay && (
            <>
              <button type="button" onClick={zoomOut} disabled={zoom === 1} aria-label={t('viewer.zoomOut')}>
                <ZoomOut size={20} aria-hidden="true" />
              </button>
              <button type="button" onClick={zoomIn} disabled={zoom >= largestZoom} aria-label={t('viewer.zoomIn')}>
                <ZoomIn size={20} aria-hidden="true" />
              </button>
            </>
          )}
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
          onWheel={(event) => {
            if (playing) return
            if (event.deltaY < 0) zoomIn()
            else zoomOut()
          }}
        >
          {shown()}
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
