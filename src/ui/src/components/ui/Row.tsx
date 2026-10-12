import type { HTMLAttributes } from 'react'

/** One line of a list: content on the left, actions on the right, a divider between rows. */
export default function Row({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div className={['row', className].filter(Boolean).join(' ')} {...rest} />
}
