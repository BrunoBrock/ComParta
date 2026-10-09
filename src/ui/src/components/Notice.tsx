import { t } from '../i18n/index.ts'

export interface NoticeContent {
  kind: 'error' | 'info'
  text: string
}

interface NoticeProps {
  notice: NoticeContent
  onDismiss: () => void
}

/** One message at a time, announced to screen readers. */
export default function Notice({ notice, onDismiss }: NoticeProps) {
  return (
    <div className={`notice notice-${notice.kind}`} role={notice.kind === 'error' ? 'alert' : 'status'}>
      <p>{notice.text}</p>
      <button type="button" className="quiet" onClick={onDismiss} aria-label={t('notice.dismiss')}>
        ×
      </button>
    </div>
  )
}
