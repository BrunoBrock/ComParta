import type { LucideIcon } from 'lucide-react'
import type { ButtonHTMLAttributes } from 'react'

interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'type' | 'children'> {
  icon: LucideIcon
  /** Read out by screen readers and shown as a tooltip, since there is no visible word. */
  label: string
}

/** For toolbars only, where a word would not fit; the label still names it. */
export default function IconButton({ icon: Icon, label, className, ...rest }: IconButtonProps) {
  return (
    <button
      type="button"
      className={['quiet icon-only', className].filter(Boolean).join(' ')}
      aria-label={label}
      title={label}
      {...rest}
    >
      <Icon size={20} aria-hidden="true" />
    </button>
  )
}
