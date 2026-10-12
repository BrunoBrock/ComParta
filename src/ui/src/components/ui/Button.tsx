import type { LucideIcon } from 'lucide-react'
import type { ButtonHTMLAttributes, ReactNode } from 'react'

export type ButtonVariant = 'primary' | 'secondary' | 'quiet' | 'danger'

interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'type'> {
  variant?: ButtonVariant
  icon?: LucideIcon
  /** Submits its form; every other button is a plain button. */
  submit?: boolean
  children: ReactNode
}

/** An icon and a label; never an icon alone (use IconButton for that). */
export default function Button({ variant = 'secondary', icon: Icon, submit, className, children, ...rest }: ButtonProps) {
  const classes = [variant === 'secondary' ? '' : variant, className].filter(Boolean).join(' ')
  return (
    <button type={submit ? 'submit' : 'button'} className={classes || undefined} {...rest}>
      {Icon && <Icon size={18} aria-hidden="true" />}
      {children}
    </button>
  )
}
