import type { HTMLAttributes } from 'react'

/** A raised surface with the standard border, radius and shadow. */
export default function Card({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div className={['card', className].filter(Boolean).join(' ')} {...rest} />
}
