import { Search, X } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import type { Bridge, ErrorCode, InvitePreviewDto, ShareDto, TransferDto } from './bridge/index.ts'
import type { ViewMode } from './components/ItemView.tsx'
import Notice, { type NoticeContent } from './components/Notice.tsx'
import Rail, { type Section } from './components/Rail.tsx'
import ReceiveDialog from './components/ReceiveDialog.tsx'
import SharesSection from './components/SharesSection.tsx'
import TransfersSection from './components/TransfersSection.tsx'
import LanguageMenu from './components/LanguageMenu.tsx'
import { errorMessage, getLanguage, type Language, pickLanguage, setLanguage, t } from './i18n/index.ts'
import { loadView, saveView } from './viewPrefs.ts'

interface AppProps {
  bridge: Bridge
}

/**
 * The whole screen. Shares and transfers are a copy of what the app reports (AD-4): loaded once,
 * then replaced whenever a change event arrives. Nothing here changes them directly; everything
 * else in this component is view state.
 */
export default function App({ bridge }: AppProps) {
  const [shares, setShares] = useState<ShareDto[] | null>(null)
  const [transfers, setTransfers] = useState<TransferDto[] | null>(null)
  const [notice, setNotice] = useState<NoticeContent | null>(null)
  const [section, setSection] = useState<Section>('shared')
  const [view, setView] = useState<ViewMode>(loadView)
  const [query, setQuery] = useState('')
  const [receiving, setReceiving] = useState(false)
  // An invite pasted anywhere in the window, already read, for the confirmation.
  const [pasted, setPasted] = useState<{ text: string; preview: InvitePreviewDto } | undefined>()
  const [adding, setAdding] = useState(false)
  // Mirrors the catalog's current language so the whole screen re-renders when it changes.
  const [language, setShownLanguage] = useState<Language>(getLanguage)

  const showError = useCallback((code: ErrorCode) => setNotice({ kind: 'error', text: errorMessage(code) }), [])
  const showInfo = useCallback((text: string) => setNotice({ kind: 'info', text }), [])
  const clearNotice = useCallback(() => setNotice(null), [])
  const clearSearch = useCallback(() => setQuery(''), [])

  useEffect(() => {
    let active = true
    const applySettings = (settings: { language: string | null; systemLanguage: string }) => {
      const chosen = pickLanguage(settings.language, settings.systemLanguage)
      setLanguage(chosen)
      document.documentElement.lang = chosen
      setShownLanguage(chosen)
    }
    const unsubscribe = bridge.subscribe((event) => {
      if (event.event === 'shares.changed') setShares(event.payload)
      else if (event.event === 'transfers.changed') setTransfers(event.payload)
      else if (event.event === 'settings.changed') applySettings(event.payload)
    })

    void bridge.invoke('settings.get').then((result) => {
      if (active && result.ok) applySettings(result.value)
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

  // Ctrl+V anywhere with an invite or download link opens "Get this file?" (UX design, Receive a file).
  // The app decides what the text is; ordinary text pasted outside a text box does nothing.
  useEffect(() => {
    if (receiving) return
    const onPaste = (event: ClipboardEvent) => {
      const target = event.target as HTMLElement | null
      if (target?.closest('input, textarea, [contenteditable="true"]')) return
      const text = event.clipboardData?.getData('text') ?? ''
      if (text.trim() === '') return
      void bridge.invoke('transfers.preview', { invite: text }).then((result) => {
        if (!result.ok) return
        setNotice(null)
        setPasted({ text, preview: result.value })
        setReceiving(true)
      })
    }
    document.addEventListener('paste', onPaste)
    return () => document.removeEventListener('paste', onPaste)
  }, [bridge, receiving])

  async function chooseLanguage(next: Language) {
    const result = await bridge.invoke('settings.set', { language: next })
    if (!result.ok) showError(result.error.code)
  }

  function changeView(next: ViewMode) {
    setView(next)
    saveView(next)
  }

  async function add() {
    clearNotice()
    setSection('shared')
    setAdding(true)
    try {
      // The app opens the file dialog; a cancel comes back as ok with no value.
      const result = await bridge.invoke('shares.add')
      if (!result.ok) showError(result.error.code)
    } finally {
      setAdding(false)
    }
  }

  function startReceiving() {
    clearNotice()
    setPasted(undefined)
    setReceiving(true)
  }

  function stopReceiving() {
    setReceiving(false)
    setPasted(undefined)
  }

  const shared = (
    <SharesSection
      bridge={bridge}
      shares={shares}
      query={query}
      view={view}
      onViewChange={changeView}
      onError={showError}
      onInfo={showInfo}
      onStart={clearNotice}
      onClearSearch={clearSearch}
    />
  )
  const received = (
    <TransfersSection
      bridge={bridge}
      transfers={transfers}
      query={query}
      view={view}
      onViewChange={changeView}
      onError={showError}
      onStart={clearNotice}
      onClearSearch={clearSearch}
    />
  )

  return (
    <div className="app">
      <header className="topbar">
        <h1 className="brand">
          <span className="brand-mark" aria-hidden="true" />
          {t('app.title')}
        </h1>
        <div className="search" role="search">
          <Search size={20} aria-hidden="true" />
          <input
            type="search"
            aria-label={t('search.label')}
            placeholder={t('search.placeholder')}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
          {query !== '' && (
            <button type="button" className="quiet icon-only" onClick={clearSearch} aria-label={t('search.clear')}>
              <X size={18} aria-hidden="true" />
            </button>
          )}
        </div>
        <LanguageMenu language={language} onChange={chooseLanguage} />
      </header>

      <Rail
        section={section}
        sharedCount={shares?.length ?? null}
        receivedCount={transfers?.length ?? null}
        adding={adding}
        onSection={setSection}
        onAdd={add}
        onReceive={startReceiving}
      />

      <main className="content">
        {notice && <Notice notice={notice} onDismiss={clearNotice} />}
        {section === 'shared' ? shared : received}
      </main>

      {receiving && (
        <ReceiveDialog
          bridge={bridge}
          pasted={pasted}
          onStarted={() => {
            stopReceiving()
            setSection('received')
          }}
          onCancel={stopReceiving}
        />
      )}
    </div>
  )
}
