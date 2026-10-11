import { LayoutGrid, List } from 'lucide-react'
import { t } from '../i18n/index.ts'
import type { ViewMode } from './ItemView.tsx'

interface ViewToggleProps {
  view: ViewMode
  onChange: (view: ViewMode) => void
}

export default function ViewToggle({ view, onChange }: ViewToggleProps) {
  return (
    <div className="view-toggle" role="group" aria-label={t('view.label')}>
      <button type="button" className="quiet" aria-pressed={view === 'grid'} onClick={() => onChange('grid')}>
        <LayoutGrid size={18} aria-hidden="true" />
        {t('view.grid')}
      </button>
      <button type="button" className="quiet" aria-pressed={view === 'list'} onClick={() => onChange('list')}>
        <List size={18} aria-hidden="true" />
        {t('view.list')}
      </button>
    </div>
  )
}
