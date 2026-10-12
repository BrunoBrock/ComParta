import type { ConnectionHealthDto } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import Dialog from './ui/Dialog.tsx'

interface ConnectionHelpProps {
  health: ConnectionHealthDto | null
  onOpenSettings: () => void
  onClose: () => void
}

/**
 * What this computer's connection means and three things to try, in plain words (UX design,
 * "Stuck transfer"). When Windows is in the way, one button opens the place to fix it.
 */
export default function ConnectionHelp({ health, onOpenSettings, onClose }: ConnectionHelpProps) {

  return (
    <Dialog labelledBy="connection-title" onClose={onClose}>
      <h2 id="connection-title">{t('connection.title')}</h2>
      {health !== null && (
        <p className={`connection connection-${health} connection-state`}>
          <span className="connection-dot" aria-hidden="true" />
          <span>{t(`connection.state.${health}`)}</span>
        </p>
      )}
      {health === 'blocked' && (
        <div className="connection-fix">
          <p>{t('connection.fix.blocked')}</p>
          <button type="button" className="primary" onClick={onOpenSettings}>
            {t('connection.openSettings')}
          </button>
        </div>
      )}
      <h3>{t('connection.tryHeading')}</h3>
      <ol className="connection-tries">
        <li>{t('connection.try.open')}</li>
        <li>{t('connection.try.later')}</li>
        <li>{t('connection.try.network')}</li>
      </ol>
      <div className="dialog-actions">
        <button type="button" onClick={onClose} autoFocus>
          {t('connection.close')}
        </button>
      </div>
    </Dialog>
  )
}
