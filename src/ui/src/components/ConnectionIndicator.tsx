import type { ConnectionHealthDto } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'

interface ConnectionIndicatorProps {
  health: ConnectionHealthDto | null
  onOpen: () => void
}

/**
 * This computer's connection at the bottom of the rail: a coloured dot and a few words, which open
 * the connection help (UX design, Layout; AD-15).
 */
export default function ConnectionIndicator({ health, onOpen }: ConnectionIndicatorProps) {
  if (health === null) return null
  const label = t(`connection.${health}`)
  return (
    <button
      type="button"
      className={`connection connection-${health}`}
      onClick={onOpen}
      aria-label={t('connection.open', { state: label })}
    >
      <span className="connection-dot" aria-hidden="true" />
      <span className="connection-label">{label}</span>
    </button>
  )
}
