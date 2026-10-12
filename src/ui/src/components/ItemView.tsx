import type { ReactNode } from 'react'
import { splitName } from '../fileNames.ts'
import FileIcon from './FileIcon.tsx'
import StatusChip, { type ChipTone } from './StatusChip.tsx'

export type ViewMode = 'grid' | 'list'

interface ItemViewProps {
  name: string
  /** Already formatted for the person, or null when not known yet. */
  size: string | null
  tone: ChipTone
  chip: string
  /** A sentence explaining the state, shown when there is something to explain. */
  note?: string
  progress?: { value: number; max: number } | 'unknown'
  actions: ReactNode
}

/**
 * One shared or received file, as a card in grid view or a row in list view. Both views are
 * built from the same parts; CSS decides the shape.
 */
export default function ItemView({ name, size, tone, chip, note, progress, actions }: ItemViewProps) {
  // The ellipsis goes in the middle so the end of the name, with its extension, stays visible.
  const [start, end] = splitName(name)
  return (
    <li className={tone === 'warn' ? 'item item-problem' : 'item'}>
      <div className="item-picture">
        <FileIcon name={name} />
      </div>
      <div className="item-text">
        {start === '' ? (
          <span className="item-name" title={name}>
            <span className="name-start">{name}</span>
          </span>
        ) : (
          <span className="item-name" title={name}>
            <span className="visually-hidden">{name}</span>
            <span className="name-start" aria-hidden="true">
              {start}
            </span>
            <span className="name-end" aria-hidden="true">
              {end}
            </span>
          </span>
        )}
        {size !== null && <span className="item-detail">{size}</span>}
        <StatusChip tone={tone} text={chip} />
        {note && <span className="item-note">{note}</span>}
        {progress === 'unknown' && <progress aria-label={name} />}
        {progress && progress !== 'unknown' && <progress value={progress.value} max={progress.max} aria-label={name} />}
      </div>
      <div className="item-actions">{actions}</div>
    </li>
  )
}
