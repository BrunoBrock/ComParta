import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'

interface EmptyStateProps {
  icon: LucideIcon
  text: string
  /** The section's primary action, usually one Button. */
  action?: ReactNode
}

/** What an empty section says, with a picture and the one thing to do next. */
export default function EmptyState({ icon: Icon, text, action }: EmptyStateProps) {
  return (
    <div className="empty-state">
      <Icon size={48} strokeWidth={1.5} aria-hidden="true" />
      <p>{text}</p>
      {action}
    </div>
  )
}
