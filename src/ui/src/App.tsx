import { useCallback, useEffect, useState } from 'react'
import type { Bridge, ErrorCode, ShareDto, TransferDto } from './bridge/index.ts'
import Notice, { type NoticeContent } from './components/Notice.tsx'
import SharesSection from './components/SharesSection.tsx'
import TransfersSection from './components/TransfersSection.tsx'
import { errorMessage, t } from './i18n/index.ts'

interface AppProps {
  bridge: Bridge
}

/**
 * The whole screen. Shares and transfers are a copy of what the app reports (AD-4): loaded once,
 * then replaced whenever a change event arrives. Nothing here changes them directly.
 */
export default function App({ bridge }: AppProps) {
  const [shares, setShares] = useState<ShareDto[] | null>(null)
  const [transfers, setTransfers] = useState<TransferDto[] | null>(null)
  const [notice, setNotice] = useState<NoticeContent | null>(null)

  const showError = useCallback((code: ErrorCode) => setNotice({ kind: 'error', text: errorMessage(code) }), [])
  const showInfo = useCallback((text: string) => setNotice({ kind: 'info', text }), [])
  const clearNotice = useCallback(() => setNotice(null), [])

  useEffect(() => {
    let active = true
    const unsubscribe = bridge.subscribe((event) => {
      if (event.event === 'shares.changed') setShares(event.payload)
      else if (event.event === 'transfers.changed') setTransfers(event.payload)
    })

    void bridge.invoke('shares.list').then((result) => {
      if (!active) return
      if (result.ok) setShares((current) => current ?? result.value)
      else showError(result.error.code)
    })
    void bridge.invoke('transfers.list').then((result) => {
      if (!active) return
      if (result.ok) setTransfers((current) => current ?? result.value)
      else showError(result.error.code)
    })

    return () => {
      active = false
      unsubscribe()
    }
  }, [bridge, showError])

  return (
    <main>
      <header>
        <h1>{t('app.title')}</h1>
        <p>{t('app.tagline')}</p>
      </header>

      {notice && <Notice notice={notice} onDismiss={clearNotice} />}

      <SharesSection
        bridge={bridge}
        shares={shares}
        onError={showError}
        onInfo={showInfo}
        onStart={clearNotice}
      />
      <TransfersSection bridge={bridge} transfers={transfers} onError={showError} onStart={clearNotice} />
    </main>
  )
}
