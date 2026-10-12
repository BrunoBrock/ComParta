import { type ReactNode, useEffect, useRef } from 'react'

interface DialogProps {
  /** The id of the dialog's heading. */
  labelledBy: string
  describedBy?: string
  className?: string
  /** Escape, or a button inside, closes the dialog; the parent stops showing it. */
  onClose: () => void
  /** While something it started is still running, Escape does nothing (UX design, Components). */
  busy?: boolean
  children: ReactNode
}

/**
 * A window over the screen. It opens as soon as it is shown, keeps the keyboard inside it, Escape
 * closes it, and focus goes back to whatever had it before, usually the button that opened it.
 */
export default function Dialog({ labelledBy, describedBy, className, onClose, busy, children }: DialogProps) {
  const dialog = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null
    const element = dialog.current
    if (element && !element.open) element.showModal()
    return () => opener?.focus()
  }, [])

  return (
    <dialog
      ref={dialog}
      className={className}
      aria-labelledby={labelledBy}
      aria-describedby={describedBy}
      onCancel={(event) => {
        event.preventDefault()
        if (!busy) onClose()
      }}
    >
      {children}
    </dialog>
  )
}
