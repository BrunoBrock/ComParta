import { X } from 'lucide-react'
import { useEffect } from 'react'

interface ToastProps {
  text: string
  closeLabel: string
  onClose: () => void
}

/** How long a confirmation stays (UX design, "Notifications and toasts"). */
export const toastMs = 6000

/**
 * A short confirmation at the bottom of the window, read out politely and gone after six
 * seconds. Errors that need a decision use a Dialog instead.
 */
export default function Toast({ text, closeLabel, onClose }: ToastProps) {
  useEffect(() => {
    const timer = setTimeout(onClose, toastMs)
    return () => clearTimeout(timer)
  }, [text, onClose])

  return (
    <div className="toast" role="status">
      <p>{text}</p>
      <button type="button" className="quiet icon-only" onClick={onClose} aria-label={closeLabel}>
        <X size={18} aria-hidden="true" />
      </button>
    </div>
  )
}
