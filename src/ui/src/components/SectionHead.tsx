import type { ReactNode } from 'react'
import type { SortOrder } from '../order.ts'
import type { ViewMode } from './ItemView.tsx'
import SortMenu from './SortMenu.tsx'
import ViewToggle from './ViewToggle.tsx'

interface SectionHeadProps {
  id: string
  title: string
  view: ViewMode
  onViewChange: (view: ViewMode) => void
  sort: SortOrder
  onSortChange: (sort: SortOrder) => void
  children?: ReactNode
}

export default function SectionHead({ id, title, view, onViewChange, sort, onSortChange, children }: SectionHeadProps) {
  return (
    <div className="section-head">
      <h2 id={id}>{title}</h2>
      <div className="section-tools">
        {children}
        <ViewToggle view={view} onChange={onViewChange} />
        <SortMenu order={sort} onChange={onSortChange} />
      </div>
    </div>
  )
}
