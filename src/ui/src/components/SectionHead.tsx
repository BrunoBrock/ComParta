import type { ReactNode } from 'react'
import type { ViewMode } from './ItemView.tsx'
import ViewToggle from './ViewToggle.tsx'

interface SectionHeadProps {
  id: string
  title: string
  view: ViewMode
  onViewChange: (view: ViewMode) => void
  children?: ReactNode
}

export default function SectionHead({ id, title, view, onViewChange, children }: SectionHeadProps) {
  return (
    <div className="section-head">
      <h2 id={id}>{title}</h2>
      <div className="section-tools">
        {children}
        <ViewToggle view={view} onChange={onViewChange} />
      </div>
    </div>
  )
}
