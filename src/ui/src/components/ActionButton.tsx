import { Download, FolderPlus, Plus } from 'lucide-react'
import { t } from '../i18n/index.ts'
import { useMenu } from './ui/useMenu.ts'

interface ActionButtonProps {
  adding: boolean
  onAdd: () => void
  onAddFolder: () => void
  onReceive: () => void
}

/**
 * The floating ＋ on phones and narrow windows: the two primary actions in one place (UX design,
 * Phone). Wider windows show them in the rail instead, so CSS hides this there.
 */
export default function ActionButton({ adding, onAdd, onAddFolder, onReceive }: ActionButtonProps) {
  const { open, menuProps, toggleProps, choose } = useMenu()

  return (
    <div className="fab">
      {open && (
        <ul {...menuProps} aria-label={t('fab.open')} className="fab-menu">
          <li role="none">
            <button type="button" role="menuitem" disabled={adding} onClick={() => choose(onAdd)}>
              <Plus size={20} aria-hidden="true" />
              {t('shares.add')}
            </button>
          </li>
          <li role="none">
            <button type="button" role="menuitem" disabled={adding} onClick={() => choose(onAddFolder)}>
              <FolderPlus size={20} aria-hidden="true" />
              {t('shares.addFolder')}
            </button>
          </li>
          <li role="none">
            <button type="button" role="menuitem" onClick={() => choose(onReceive)}>
              <Download size={20} aria-hidden="true" />
              {t('transfers.open')}
            </button>
          </li>
        </ul>
      )}
      <button {...toggleProps} type="button" className="primary fab-toggle" aria-label={t('fab.open')}>
        <Plus size={28} aria-hidden="true" />
      </button>
    </div>
  )
}
