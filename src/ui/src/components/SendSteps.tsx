import { Check, Copy } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import type { Bridge, ErrorCode, ShareDto } from '../bridge/index.ts'
import { t } from '../i18n/index.ts'
import { inviteMessage } from '../invite.ts'
import Dialog from './ui/Dialog.tsx'

interface SendStepsProps {
  bridge: Bridge
  /** The list as the app reports it, to follow the new share from getting ready to ready. */
  shares: ShareDto[] | null
  onError: (code: ErrorCode) => void
  onInfo: (text: string) => void
  /** shared: a file was shared on the way; hide: the person asked not to see the steps again. */
  onClose: (outcome: { shared: boolean; hide: boolean }) => void
}

/** How the invite looks in the preview; the app puts the real link in when it copies. */
const linkPreview = 'comparta://receive/…'

/**
 * The guided first send (UX design, "Send a file, first time"): choose, getting ready, send the
 * invite. The file dialog opens as soon as this does; cancelling it closes the steps.
 */
export default function SendSteps({ bridge, shares, onError, onInfo, onClose }: SendStepsProps) {
  const [id, setId] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const [hide, setHide] = useState(false)
  const picking = useRef(false)

  useEffect(() => {
    // Once only, even when React runs effects twice in development.
    if (picking.current) return
    picking.current = true
    void bridge.invoke('shares.add').then((result) => {
      if (!result.ok) {
        onError(result.error.code)
        onClose({ shared: false, hide: false })
      } else if (result.value === null) onClose({ shared: false, hide: false })
      else setId(result.value.id)
    })
  }, [bridge, onClose, onError])

  const share = id === null ? undefined : shares?.find((candidate) => candidate.id === id)
  const step = id === null ? 1 : share?.status === 'ready' ? 3 : 2
  const close = () => onClose({ shared: id !== null, hide })

  useEffect(() => {
    // The file went missing while getting ready: the list explains it, so the steps end.
    if (share?.status === 'unavailable') onClose({ shared: true, hide })
  }, [share?.status, hide, onClose])

  async function copy(ready: ShareDto) {
    const result = await bridge.invoke('shares.copyInvite', { id: ready.id, message: inviteMessage(ready) })
    if (result.ok) {
      setCopied(true)
      onInfo(t('shares.inviteCopied', { name: ready.name }))
    } else onError(result.error.code)
  }

  const titles = { 1: t('steps.choose'), 2: t('steps.ready'), 3: t('steps.send') } as const

  return (
    <Dialog labelledBy="steps-title" className="steps" onClose={close} busy={step === 1}>
      <p className="steps-count">{t('steps.count', { step })}</p>
      <h2 id="steps-title">{titles[step]}</h2>
      <ol className="steps-track" aria-label={t('steps.label')}>
        {([1, 2, 3] as const).map((n) => (
          <li
            key={n}
            className={n <= step ? 'steps-dot steps-dot-on' : 'steps-dot'}
            aria-current={n === step ? 'step' : undefined}
          >
            <span className="visually-hidden">{titles[n]}</span>
          </li>
        ))}
      </ol>

      {step === 1 && <p>{t('steps.choosing')}</p>}

      {step === 2 && (
        <>
          <p>{t('steps.reading', { name: share?.name ?? '' })}</p>
          <progress aria-label={t('steps.ready')} />
        </>
      )}

      {step === 3 && share && (
        <>
          <p id="steps-message-label" className="steps-message-label">
            {t('steps.messageLabel')}
          </p>
          <p className="steps-message" aria-labelledby="steps-message-label">
            {inviteMessage(share).replace('{link}', linkPreview)}
          </p>
          <p>{t('steps.after')}</p>
        </>
      )}

      {step > 1 && (
        <label className="steps-hide">
          <input type="checkbox" checked={hide} onChange={(event) => setHide(event.target.checked)} />
          {t('steps.hide')}
        </label>
      )}

      <div className="dialog-actions">
        {step === 3 && share ? (
          <>
            <button type="button" onClick={close}>
              {t('steps.done')}
            </button>
            <button type="button" className="primary" onClick={() => copy(share)} autoFocus>
              {copied ? <Check size={18} aria-hidden="true" /> : <Copy size={18} aria-hidden="true" />}
              {copied ? t('steps.copied') : t('steps.copy')}
            </button>
          </>
        ) : (
          <button type="button" onClick={close} disabled={step === 1}>
            {t('steps.close')}
          </button>
        )}
      </div>
    </Dialog>
  )
}
