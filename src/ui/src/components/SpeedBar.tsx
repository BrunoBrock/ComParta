import { ArrowDown, ArrowUp, ChevronDown, Play, Square } from 'lucide-react'
import type { ActivityDto, SetActivityArgs } from '../bridge/index.ts'
import { formatRate, t } from '../i18n/index.ts'
import { useMenu } from './ui/useMenu.ts'

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
  const { open: menuOpen, menuProps, toggleProps, choose: chooseItem } = useMenu()

  const receivingStopped = activity?.receivingStopped ?? false
  const sendingStopped = activity?.sendingStopped ?? false
  const anyStopped = receivingStopped || sendingStopped

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
          {...toggleProps}
          type="button"
          className="icon-only"
          aria-label={t('stop.menu')}
          disabled={activity === null}
        >
          <ChevronDown size={18} aria-hidden="true" />
        </button>
        {menuOpen && (
          <ul {...menuProps} aria-label={t('stop.menu')} className="stop-menu">
            {choices.map((choice) => (
              <li key={choice.key} role="none">
                <button type="button" role="menuitem" className="quiet" onClick={() => chooseItem(() => onSet(choice.stopped))}>
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
