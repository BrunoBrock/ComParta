import { Inbox, Mail, Plus, Upload } from 'lucide-react'
import { t } from '../i18n/index.ts'

export type Section = 'shared' | 'received'

interface RailProps {
  section: Section
  sharedCount: number | null
  receivedCount: number | null
  adding: boolean
  onSection: (section: Section) => void
  onAdd: () => void
  onReceive: () => void
}

/**
 * The two things a person comes to do, always in reach, and the two places their files live.
 * A column on a wide window; a bar along the bottom on a narrow one (UX design, Layout).
 */
export default function Rail({ section, sharedCount, receivedCount, adding, onSection, onAdd, onReceive }: RailProps) {
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
        <button type="button" className="big" onClick={onReceive}>
          <Mail size={22} aria-hidden="true" />
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
      </nav>

      <p className="rail-tagline">{t('app.tagline')}</p>
    </aside>
  )
}
