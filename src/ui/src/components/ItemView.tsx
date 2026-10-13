import { ArrowDown, ArrowUp, Info, Pause, Play } from 'lucide-react'
import type { ReactNode } from 'react'
import { splitName } from '../fileNames.ts'
import { formatRate, t } from '../i18n/index.ts'
import FileIcon from './ui/FileIcon.tsx'
import StatusChip, { type ChipTone } from './ui/StatusChip.tsx'

export type ViewMode = 'grid' | 'list'

/** How fast the file moves now, in bytes per second each way. */
export interface ItemSpeed {
  receiving: number
  sending: number
}

/** The file's own pause or play button, over its picture (UX design, "Items"). */
export interface ItemToggle {
  /** True shows play, false shows pause. */
  paused: boolean
  /** Says what the button does, with the file's name. */
  label: string
  onToggle: () => void
}

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
  /** Opens the details window for this file (CAP-26). */
  onDetails: () => void
  /** The file's main action, on double-click: copy the invite, or open what arrived. */
  onMain?: () => void
  /** Asks to stop sharing or remove it, on the Delete key. */
  onRemove: () => void
  /** The file's own picture; the file icon when not given. */
  picture?: ReactNode
  /** Shown while the file moves; nothing when both are zero. */
  speed?: ItemSpeed
  toggle?: ItemToggle
}

/**
 * One shared or received file, as a card in grid view or a row in list view. Both views are
 * built from the same parts; CSS decides the shape.
 */
export default function ItemView({
  name,
  size,
  tone,
  chip,
  note,
  progress,
  actions,
  onDetails,
  onMain,
  onRemove,
  picture,
  speed,
  toggle,
}: ItemViewProps) {
  // The ellipsis goes in the middle so the end of the name, with its extension, stays visible.
  const [start, end] = splitName(name)
  return (
    // The file itself takes focus: Enter opens its details, Delete asks to remove it, and a
    // double-click does its main action (UX design, Items). Its buttons keep their own keys.
    <li
      className={['item', tone === 'warn' && 'item-problem', toggle?.paused && 'item-paused'].filter(Boolean).join(' ')}
      tabIndex={0}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget) return
        if (event.key === 'Enter') {
          event.preventDefault()
          onDetails()
        } else if (event.key === 'Delete') {
          event.preventDefault()
          onRemove()
        }
      }}
      onDoubleClick={(event) => {
        if (!(event.target as HTMLElement).closest('button')) onMain?.()
      }}
    >
      <div className="item-picture">{picture ?? <FileIcon name={name} />}</div>
      {toggle && (
        <button
          type="button"
          className="icon-only item-toggle"
          onClick={toggle.onToggle}
          aria-label={toggle.label}
          title={toggle.label}
        >
          {toggle.paused ? <Play size={20} aria-hidden="true" /> : <Pause size={20} aria-hidden="true" />}
        </button>
      )}
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
        {speed && (speed.receiving > 0 || speed.sending > 0) && (
          <span className="item-speed">
            {speed.receiving > 0 && (
              <span>
                <ArrowDown size={14} aria-hidden="true" />
                <span className="visually-hidden">{t('speed.receiving')} </span>
                {formatRate(speed.receiving)}
              </span>
            )}
            {speed.sending > 0 && (
              <span>
                <ArrowUp size={14} aria-hidden="true" />
                <span className="visually-hidden">{t('speed.sending')} </span>
                {formatRate(speed.sending)}
              </span>
            )}
          </span>
        )}
        {note && <span className="item-note">{note}</span>}
        {progress === 'unknown' && <progress aria-label={name} />}
        {progress && progress !== 'unknown' && <progress value={progress.value} max={progress.max} aria-label={name} />}
      </div>
      <div className="item-actions">{actions}</div>
      <button
        type="button"
        className="quiet icon-only item-info"
        onClick={onDetails}
        aria-label={t('details.openFor', { name })}
        title={t('details.open')}
      >
        <Info size={20} aria-hidden="true" />
      </button>
    </li>
  )
}
