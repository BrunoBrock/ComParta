import { type ReactNode, useEffect, useRef } from 'react'

interface ModalProps {
  /** The id of the dialog's heading. */
  labelledBy: string
  describedBy?: string
  className?: string
  /** Escape, or a button inside, closes the dialog; the parent stops showing it. */
  onClose: () => void
  children: ReactNode
}

/**
 * A window over the screen. It opens as soon as it is shown, Escape closes it, and focus goes
 * back to whatever had it before, usually the button that opened it.
 */
export default function Modal({ labelledBy, describedBy, className, onClose, children }: ModalProps) {
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
        onClose()
      }}
    >
      {children}
    </dialog>
  )
}
