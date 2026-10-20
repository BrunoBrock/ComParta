import { useEffect, useRef, useState } from 'react'
import type { Bridge } from '../../bridge/index.ts'
import FileIcon from './FileIcon.tsx'

interface FolderPicturesProps {
  bridge: Bridge
  id: string
  name: string
  /** How many files the folder holds; no more pictures are asked for than that. */
  itemCount: number
  /** The files are there to be drawn. */
  ready: boolean
  /** Each picture's longest side, in pixels. */
  size?: number
}

/** The most pictures a folder's card shows (UX design, Items). */
export const folderPictures = 4

/**
 * A shared folder's card: up to four of its photos and videos side by side, asked for once the
 * card is on screen, or the folder icon when it has none (epic share-a-folder story 5).
 */
export default function FolderPictures({ bridge, id, name, itemCount, ready, size = 120 }: FolderPicturesProps) {
  const [urls, setUrls] = useState<string[]>([])
  const box = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!ready) return
    let active = true
    const load = () => {
      const asked = Array.from({ length: Math.min(folderPictures, itemCount) }, (_, item) =>
        bridge.invoke('thumbnails.get', { kind: 'share', id, size, item }),
      )
      void Promise.all(asked).then((results) => {
        if (active) setUrls(results.flatMap((result) => (result.ok && result.value ? [result.value] : [])))
      })
    }
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
  }, [bridge, id, itemCount, ready, size])

  const shown = ready ? urls : []
  return (
    <div ref={box} className={shown.length > 1 ? 'thumbnail folder-pictures' : 'thumbnail'} data-count={shown.length}>
      {shown.length === 0 ? (
        <FileIcon name={name} folder />
      ) : (
        shown.map((url, i) => <img key={i} src={url} alt="" draggable={false} />)
      )}
    </div>
  )
}
