import { ArrowDownUp, Check } from 'lucide-react'
import { t } from '../i18n/index.ts'
import { type SortOrder, sortOrders } from '../order.ts'
import { useMenu } from './ui/useMenu.ts'

interface SortMenuProps {
  order: SortOrder
  onChange: (order: SortOrder) => void
}

/** The ⇅ menu beside Grid and List: newest first, by name, or largest first (UX design, Layout). */
export default function SortMenu({ order, onChange }: SortMenuProps) {
  const { open, menuProps, toggleProps, choose } = useMenu()
  return (
    <div className="sort-menu">
      <button {...toggleProps} type="button" aria-label={t('sort.label', { order: t(`sort.${order}`) })}>
        <ArrowDownUp size={18} aria-hidden="true" />
        <span className="sort-current">{t(`sort.${order}`)}</span>
      </button>
      {open && (
        <ul {...menuProps} aria-label={t('sort.menu')} className="sort-options">
          {sortOrders.map((option) => (
            <li key={option} role="none">
              <button
                type="button"
                role="menuitemradio"
                aria-checked={option === order}
                className="quiet"
                onClick={() => choose(() => onChange(option))}
              >
                <Check size={18} aria-hidden="true" className={option === order ? undefined : 'invisible'} />
                {t(`sort.${option}`)}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
