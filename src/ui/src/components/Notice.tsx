import { X } from 'lucide-react'
import { t } from '../i18n/index.ts'

interface NoticeProps {
  text: string
  onDismiss: () => void
}

/** A problem, at the top of the list until dismissed, announced to screen readers at once. */
export default function Notice({ text, onDismiss }: NoticeProps) {
  return (
    <div className="notice notice-error" role="alert">
      <p>{text}</p>
      <button type="button" className="quiet icon-only" onClick={onDismiss} aria-label={t('notice.dismiss')}>
        <X size={18} aria-hidden="true" />
      </button>
    </div>
  )
}
