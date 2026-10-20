import { CircleHelp, Download, FolderPlus, Inbox, Plus, Settings, Upload } from 'lucide-react'
import type { ConnectionHealthDto } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import ConnectionIndicator from './ConnectionIndicator.tsx'

export type Section = 'shared' | 'received'

interface RailProps {
  section: Section
  sharedCount: number | null
  receivedCount: number | null
  adding: boolean
  connection: ConnectionHealthDto | null
  /** Others get the files through the helper computer. */
  throughHelper?: boolean
  onConnection: () => void
  onSection: (section: Section) => void
  onAdd: () => void
  onAddFolder: () => void
  onReceive: () => void
  onHelp: () => void
  onSettings: () => void
}

/**
 * The two things a person comes to do, always in reach, the two places their files live, and Help
 * and Settings. A column on a wide window, icons with small labels between 600 and 900 px, and a
 * bar along the bottom on a narrow one, where the floating ＋ takes the two actions (UX design,
 * Layout).
 */
export default function Rail(props: RailProps) {
  const { section, sharedCount, receivedCount, adding, connection, onConnection, onSection, onAdd, onReceive } = props
  const { onHelp, onSettings, throughHelper, onAddFolder } = props
  const places = [
    { id: 'shared' as const, label: t('nav.shared'), count: sharedCount, Icon: Upload },
    { id: 'received' as const, label: t('nav.received'), count: receivedCount, Icon: Inbox },
  ]

  return (
    <aside className="rail">
      <div className="rail-actions">
        <button type="button" className="primary big" onClick={onAdd} disabled={adding}>
          <Plus size={22} aria-hidden="true" />
          <span>{t('shares.add')}</span>
        </button>
        <button type="button" className="quiet rail-folder" onClick={onAddFolder} disabled={adding}>
          <FolderPlus size={20} aria-hidden="true" />
          <span>{t('shares.addFolder')}</span>
        </button>
        <button type="button" className="big" onClick={onReceive}>
          <Download size={22} aria-hidden="true" />
          <span>{t('transfers.open')}</span>
        </button>
      </div>

      <nav aria-label={t('nav.label')}>
        <ul>
          {places.map(({ id, label, count, Icon }) => (
            <li key={id}>
              <button
                type="button"
                className="nav-item"
                aria-current={section === id ? 'page' : undefined}
                onClick={() => onSection(id)}
              >
                <Icon size={20} aria-hidden="true" />
                <span className="nav-label">{label}</span>
                {count !== null && count > 0 && (
                  <>
                    {' '}
                    <span className="nav-count">{count}</span>
                  </>
                )}
              </button>
            </li>
          ))}
        </ul>
        <ul className="rail-more">
          <li className="rail-help">
            <button type="button" className="nav-item" onClick={onHelp}>
              <CircleHelp size={20} aria-hidden="true" />
              <span className="nav-label">{t('nav.help')}</span>
            </button>
          </li>
          <li>
            <button type="button" className="nav-item" onClick={onSettings}>
              <Settings size={20} aria-hidden="true" />
              <span className="nav-label">{t('nav.settings')}</span>
            </button>
          </li>
        </ul>
      </nav>

      <p className="rail-tagline">{t('app.tagline')}</p>
      <ConnectionIndicator health={connection} throughHelper={throughHelper} onOpen={onConnection} />
    </aside>
  )
}
