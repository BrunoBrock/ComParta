import { useEffect, useRef, useState } from 'react'
import type { Bridge, ThumbnailKindDto } from '../../bridge/index.ts'
import { isPicture } from '../../fileNames.ts'
import FileIcon from './FileIcon.tsx'

interface ThumbnailProps {
  bridge: Bridge
  kind: ThumbnailKindDto
  id: string
  name: string
  /** The file is there to be drawn: a share that was found, a received file that has arrived. */
  ready: boolean
  /** The picture's longest side, in pixels. */
  size?: number
}

/**
 * A photo or video's own picture, asked for once the card is on screen; every other file, and
 * any picture the system cannot draw, shows its file icon (UX design, Items; AD-18).
 */
export default function Thumbnail({ bridge, kind, id, name, ready, size = 240 }: ThumbnailProps) {
  const [url, setUrl] = useState<string | null>(null)
  const box = useRef<HTMLDivElement>(null)
  const wanted = ready && isPicture(name)

  useEffect(() => {
    if (!wanted) return
    let active = true
    const load = () => {
      void bridge.invoke('thumbnails.get', { kind, id, size, item: 0 }).then((result) => {
        if (active && result.ok) setUrl(result.value)
      })
    }
    // Without a way to tell what is on screen (older web views), every picture is asked for at once.
    if (typeof IntersectionObserver === 'undefined') {
      load()
      return () => {
        active = false
      }
    }
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        observer.disconnect()
        load()
      }
    })
    observer.observe(box.current!)
    return () => {
      active = false
      observer.disconnect()
    }
  }, [bridge, kind, id, size, wanted])

  return (
    <div ref={box} className="thumbnail">
      {wanted && url ? <img src={url} alt="" draggable={false} /> : <FileIcon name={name} />}
    </div>
  )
}
