import { ArrowDown, ArrowUp, ChevronDown, Play, Square } from 'lucide-react'
import { type KeyboardEvent, useEffect, useRef, useState } from 'react'
import type { ActivityDto, SetActivityArgs } from '../bridge/index.ts'
import { formatRate, t } from '../i18n/index.ts'

interface SpeedBarProps {
  /** null until the app has answered. */
  activity: ActivityDto | null
  onSet: (stopped: SetActivityArgs) => void
}

const choices: { key: 'stop.menuBoth' | 'stop.menuSending' | 'stop.menuReceiving'; stopped: SetActivityArgs }[] = [
  { key: 'stop.menuBoth', stopped: { sendingStopped: true, receivingStopped: true } },
  { key: 'stop.menuSending', stopped: { sendingStopped: true, receivingStopped: false } },
  { key: 'stop.menuReceiving', stopped: { sendingStopped: false, receivingStopped: true } },
]

/**
 * Everything ComParta is receiving and sending right now, and one place to stop it all (UX design,
 * "Speed bar"). Stop stops both; its menu stops one side only; Continue starts what was stopped.
 */
export default function SpeedBar({ activity, onSet }: SpeedBarProps) {
  const [menuOpen, setMenuOpen] = useState(false)
  const menu = useRef<HTMLUListElement>(null)
  const toggle = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!menuOpen) return
    menu.current?.querySelector<HTMLButtonElement>('button')?.focus()
    const onPointer = (event: PointerEvent) => {
      const target = event.target as Node
      if (!menu.current?.contains(target) && !toggle.current?.contains(target)) setMenuOpen(false)
    }
    document.addEventListener('pointerdown', onPointer)
    return () => document.removeEventListener('pointerdown', onPointer)
  }, [menuOpen])

  const receivingStopped = activity?.receivingStopped ?? false
  const sendingStopped = activity?.sendingStopped ?? false
  const anyStopped = receivingStopped || sendingStopped

  function choose(stopped: SetActivityArgs) {
    setMenuOpen(false)
    toggle.current?.focus()
    onSet(stopped)
  }

  function onMenuKey(event: KeyboardEvent<HTMLUListElement>) {
    const items = [...(menu.current?.querySelectorAll<HTMLButtonElement>('button') ?? [])]
    const at = items.indexOf(document.activeElement as HTMLButtonElement)
    if (event.key === 'Escape') {
      event.preventDefault()
      setMenuOpen(false)
      toggle.current?.focus()
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      const step = event.key === 'ArrowDown' ? 1 : -1
      items[(at + step + items.length) % items.length]?.focus()
    }
  }

  return (
    <section className={anyStopped ? 'speed-bar speed-bar-stopped' : 'speed-bar'} aria-label={t('speed.label')}>
      <p className={receivingStopped ? 'speed speed-off' : 'speed'}>
        <ArrowDown size={18} aria-hidden="true" />
        <span>
          {receivingStopped ? (
            t('speed.receivingStopped')
          ) : (
            <>
              <span className="speed-word">{t('speed.receiving')} </span>
              {formatRate(activity?.receivingBytesPerSecond ?? 0)}
            </>
          )}
        </span>
      </p>
      <p className={sendingStopped ? 'speed speed-off' : 'speed'}>
        <ArrowUp size={18} aria-hidden="true" />
        <span>
          {sendingStopped ? (
            t('speed.sendingStopped')
          ) : (
            <>
              <span className="speed-word">{t('speed.sending')} </span>
              {formatRate(activity?.sendingBytesPerSecond ?? 0)}
            </>
          )}
        </span>
      </p>

      <div className="stop-control">
        {anyStopped ? (
          <button
            type="button"
            className="primary"
            onClick={() => onSet({ sendingStopped: false, receivingStopped: false })}
          >
            <Play size={18} aria-hidden="true" />
            {t('stop.continue')}
          </button>
        ) : (
          <button
            type="button"
            disabled={activity === null}
            onClick={() => onSet({ sendingStopped: true, receivingStopped: true })}
          >
            <Square size={16} aria-hidden="true" />
            {t('stop.both')}
          </button>
        )}
        <button
          ref={toggle}
          type="button"
          className="icon-only"
          aria-label={t('stop.menu')}
          aria-haspopup="menu"
          aria-expanded={menuOpen}
          disabled={activity === null}
          onClick={() => setMenuOpen((open) => !open)}
        >
          <ChevronDown size={18} aria-hidden="true" />
        </button>
        {menuOpen && (
          <ul ref={menu} role="menu" aria-label={t('stop.menu')} className="stop-menu" onKeyDown={onMenuKey}>
            {choices.map((choice) => (
              <li key={choice.key} role="none">
                <button type="button" role="menuitem" className="quiet" onClick={() => choose(choice.stopped)}>
                  {t(choice.key)}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  )
}
