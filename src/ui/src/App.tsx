import { CircleHelp, Search, Settings, X } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import type {
  ActivityDto,
  Bridge,
  ConnectionHealthDto,
  SetActivityArgs,
  ErrorCode,
  InvitePreviewDto,
  ShareDto,
  TransferDto,
} from './bridge/index.ts'
import ActionButton from './components/ActionButton.tsx'
import ConnectionHelp from './components/ConnectionHelp.tsx'
import DetailsWindow, { type DetailsOf } from './components/DetailsWindow.tsx'
import HelpDialog from './components/HelpDialog.tsx'
import type { ViewMode } from './components/ItemView.tsx'
import Notice, { type NoticeContent } from './components/Notice.tsx'
import Rail, { type Section } from './components/Rail.tsx'
import ReceiveDialog from './components/ReceiveDialog.tsx'
import SettingsDialog from './components/SettingsDialog.tsx'
import SharesSection from './components/SharesSection.tsx'
import SpeedBar from './components/SpeedBar.tsx'
import TransfersSection from './components/TransfersSection.tsx'
import IconButton from './components/ui/IconButton.tsx'
import { errorMessage, getLanguage, type Language, pickLanguage, setLanguage, t } from './i18n/index.ts'
import { loadView, saveView } from './viewPrefs.ts'

interface AppProps {
  bridge: Bridge
}

/** How long a transfer waits for the other computer before the help is offered (UX design, "Stuck transfer"). */
export const stuckAfterMs = 10 * 60 * 1000
const stuckCheckMs = 30 * 1000

/** Waiting for the other computer: still connecting, or "receiving" before the size is known. */
const isWaiting = (transfer: TransferDto) =>
  transfer.state === 'connecting' || (transfer.state === 'receiving' && !transfer.sizeBytes)

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
  const [connection, setConnection] = useState<ConnectionHealthDto | null>(null)
  const [helping, setHelping] = useState(false)
  // Which window the header or rail opened: the short help or the settings.
  const [opened, setOpened] = useState<'help' | 'settings' | null>(null)
  const [activity, setActivity] = useState<ActivityDto | null>(null)
  const [details, setDetails] = useState<DetailsOf | null>(null)
  const [stuck, setStuck] = useState<ReadonlySet<string>>(() => new Set())
  // When this screen first saw each transfer waiting for the other computer.
  const waitingSince = useRef(new Map<string, number>())

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
      else if (event.event === 'connection.changed') setConnection(event.payload.health)
      else if (event.event === 'activity.changed') setActivity(event.payload)
    })

    void bridge.invoke('activity.get').then((result) => {
      if (active && result.ok) setActivity((current) => current ?? result.value)
    })

    void bridge.invoke('connection.get').then((result) => {
      if (active && result.ok) setConnection(result.value.health)
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

  /** Opens "Get this file?" for text the app recognises; quiet ignores anything else. */
  const confirmReceiving = useCallback(
    async (text: string, quiet: boolean) => {
      const result = await bridge.invoke('transfers.preview', { invite: text })
      if (!result.ok) {
        if (!quiet) showError(result.error.code)
        return
      }
      setNotice(null)
      setPasted({ text, preview: result.value })
      setReceiving(true)
    },
    [bridge, showError],
  )

  // Ctrl+V anywhere with an invite or download link opens "Get this file?" (UX design, Receive a file).
  // The app decides what the text is; ordinary text pasted outside a text box does nothing.
  useEffect(() => {
    if (receiving) return
    const onPaste = (event: ClipboardEvent) => {
      const target = event.target as HTMLElement | null
      if (target?.closest('input, textarea, [contenteditable="true"]')) return
      const text = event.clipboardData?.getData('text') ?? ''
      if (text.trim() === '') return
      void confirmReceiving(text, true)
    }
    document.addEventListener('paste', onPaste)
    return () => document.removeEventListener('paste', onPaste)
  }, [confirmReceiving, receiving])

  // A comparta: link clicked in mail or chat reaches the running app, which keeps it until the
  // screen takes it: at start, and whenever the app says one is waiting (AD-14, AD-16).
  useEffect(() => {
    let active = true
    const take = async () => {
      const result = await bridge.invoke('invites.takeOpened')
      if (active && result.ok && result.value) await confirmReceiving(result.value, false)
    }
    void take()
    const unsubscribe = bridge.subscribe((event) => {
      if (event.event === 'invites.changed' && event.payload) void take()
    })
    return () => {
      active = false
      unsubscribe()
    }
  }, [bridge, confirmReceiving])

  // Notes when each transfer started waiting, and which have waited long enough to offer help.
  useEffect(() => {
    const check = () => {
      const now = Date.now()
      const since = waitingSince.current
      const waiting = new Set((transfers ?? []).filter(isWaiting).map((transfer) => transfer.id))
      for (const id of [...since.keys()]) if (!waiting.has(id)) since.delete(id)
      for (const id of waiting) if (!since.has(id)) since.set(id, now)
      const late = [...since].filter(([, start]) => now - start >= stuckAfterMs).map(([id]) => id)
      setStuck((current) =>
        current.size === late.length && late.every((id) => current.has(id)) ? current : new Set(late),
      )
    }
    check()
    const timer = setInterval(check, stuckCheckMs)
    return () => clearInterval(timer)
  }, [transfers])

  async function openConnectionSettings() {
    const result = await bridge.invoke('connection.openSettings')
    if (!result.ok) showError(result.error.code)
  }

  async function setStopped(stopped: SetActivityArgs) {
    const result = await bridge.invoke('activity.set', stopped)
    if (!result.ok) showError(result.error.code)
  }

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
      stopped={activity?.sendingStopped ?? false}
      onDetails={setDetails}
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
      stuck={stuck}
      onHelp={() => setHelping(true)}
      stopped={activity?.receivingStopped ?? false}
      onDetails={setDetails}
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
        <div className="header-tools">
          <IconButton icon={CircleHelp} label={t('nav.help')} onClick={() => setOpened('help')} />
          <IconButton icon={Settings} label={t('nav.settings')} onClick={() => setOpened('settings')} />
        </div>
      </header>

      <SpeedBar activity={activity} onSet={setStopped} />

      <Rail
        section={section}
        sharedCount={shares?.length ?? null}
        receivedCount={transfers?.length ?? null}
        adding={adding}
        connection={connection}
        onConnection={() => setHelping(true)}
        onSection={setSection}
        onAdd={add}
        onReceive={startReceiving}
        onHelp={() => setOpened('help')}
        onSettings={() => setOpened('settings')}
      />

      <ActionButton adding={adding} onAdd={add} onReceive={startReceiving} />

      <main className="content">
        {notice && <Notice notice={notice} onDismiss={clearNotice} />}
        {section === 'shared' ? shared : received}
      </main>

      {details && (
        <DetailsWindow
          bridge={bridge}
          item={details}
          stopped={(details.kind === 'share' ? activity?.sendingStopped : activity?.receivingStopped) ?? false}
          onClose={() => setDetails(null)}
        />
      )}

      {opened === 'help' && (
        <HelpDialog
          onConnection={() => {
            setOpened(null)
            setHelping(true)
          }}
          onClose={() => setOpened(null)}
        />
      )}

      {opened === 'settings' && (
        <SettingsDialog language={language} onLanguage={chooseLanguage} onClose={() => setOpened(null)} />
      )}

      {helping && (
        <ConnectionHelp health={connection} onOpenSettings={openConnectionSettings} onClose={() => setHelping(false)} />
      )}

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
