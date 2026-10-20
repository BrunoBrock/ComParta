import { CircleHelp, Search, Settings, X } from 'lucide-react'
import { type DragEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type {
  ActivityDto,
  Bridge,
  ConnectionHealthDto,
  SetActivityArgs,
  SetSettingsArgs,
  SettingsDto,
  ErrorCode,
  InvitePreviewDto,
  ShareDto,
  TransferDto,
} from './bridge/index.ts'
import ActionButton from './components/ActionButton.tsx'
import ConnectionHelp from './components/ConnectionHelp.tsx'
import DetailsWindow, { type DetailsOf } from './components/DetailsWindow.tsx'
import HelpDialog from './components/HelpDialog.tsx'
import type { ItemSpeed, ViewMode } from './components/ItemView.tsx'
import { shareToggle, transferToggle } from './components/itemToggles.ts'
import Notice from './components/Notice.tsx'
import MediaViewer, { type ViewedItem } from './components/MediaViewer.tsx'
import Rail, { type Section } from './components/Rail.tsx'
import ReceiveDialog from './components/ReceiveDialog.tsx'
import SendSteps from './components/SendSteps.tsx'
import QuitDialog from './components/QuitDialog.tsx'
import SettingsDialog, { type Theme } from './components/SettingsDialog.tsx'
import SharesSection from './components/SharesSection.tsx'
import SpeedBar from './components/SpeedBar.tsx'
import TransfersSection from './components/TransfersSection.tsx'
import IconButton from './components/ui/IconButton.tsx'
import Toast from './components/ui/Toast.tsx'
import { errorMessage, getLanguage, type Language, pickLanguage, setLanguage, t } from './i18n/index.ts'
import { playableKind } from './fileNames.ts'
import { setSenderName } from './invite.ts'
import { inOrder, type SortOrder } from './order.ts'

interface AppProps {
  bridge: Bridge
}

/** How long a transfer waits for the other computer before the help is offered (UX design, "Stuck transfer"). */
/** The guided send runs for the first three sends (UX design, "Send a file, first time"). */
export const sendStepsShown = 3

export const stuckAfterMs = 10 * 60 * 1000
const stuckCheckMs = 30 * 1000

/** Waiting for the other computer: still connecting, or "receiving" before the size is known. */
const isWaiting = (transfer: TransferDto) =>
  transfer.state === 'connecting' ||
  transfer.state === 'waiting' ||
  (transfer.state === 'receiving' && !transfer.sizeBytes)

/**
 * The whole screen. Shares and transfers are a copy of what the app reports (AD-4): loaded once,
 * then replaced whenever a change event arrives. Nothing here changes them directly; everything
 * else in this component is view state.
 */
export default function App({ bridge }: AppProps) {
  const [shares, setShares] = useState<ShareDto[] | null>(null)
  const [transfers, setTransfers] = useState<TransferDto[] | null>(null)
  // Errors stay until dismissed; confirmations are toasts that go by themselves (UX design, Toasts).
  const [notice, setNotice] = useState<string | null>(null)
  const [toast, setToast] = useState<{ text: string; id: number } | null>(null)
  const [section, setSection] = useState<Section>('shared')
  const [view, setView] = useState<ViewMode>('grid')
  const [sort, setSort] = useState<SortOrder>('added')
  // The person's choices, sent back whole when one of them changes (settings.set). A ref, so two
  // quick changes never send an older copy of the other.
  const choices = useRef<SetSettingsArgs>({
    language: null,
    view: null,
    sort: null,
    sendStepsSeen: 0,
    startWithWindows: false,
    publicServers: [],
    helperAddress: null,
    helperKey: null,
    yourName: null,
    theme: null,
  })
  const [yourName, setYourName] = useState('')
  const [theme, setTheme] = useState<Theme>('system')
  const [version, setVersion] = useState('')
  const [guidedSend, setGuidedSend] = useState(true)
  const [startWithWindows, setStartWithWindows] = useState(false)
  const [publicServers, setPublicServers] = useState<readonly string[]>([])
  const [helper, setHelper] = useState<{ address: string | null; key: string | null }>({ address: null, key: null })
  // Files that would stop moving if the app quit now; the person confirms first (AD-14).
  const [quitting, setQuitting] = useState<readonly string[] | null>(null)
  const [query, setQuery] = useState('')
  const [receiving, setReceiving] = useState(false)
  // An invite pasted anywhere in the window, already read, for the confirmation.
  const [pasted, setPasted] = useState<{ text: string; preview: InvitePreviewDto } | undefined>()
  const [adding, setAdding] = useState(false)
  // The guided send is open (UX design, "Send a file, first time").
  const [guiding, setGuiding] = useState(false)
  // Files are being dragged over the window (epic 8 story 6).
  const [dragging, setDragging] = useState(false)
  // Mirrors the catalog's current language so the whole screen re-renders when it changes.
  const [language, setShownLanguage] = useState<Language>(getLanguage)
  const [connection, setConnection] = useState<ConnectionHealthDto | null>(null)
  const [throughHelper, setThroughHelper] = useState(false)
  const [helping, setHelping] = useState(false)
  // Which window the header or rail opened: the short help or the settings.
  const [opened, setOpened] = useState<'help' | 'settings' | null>(null)
  const [activity, setActivity] = useState<ActivityDto | null>(null)
  const [details, setDetails] = useState<DetailsOf | null>(null)
  const [viewing, setViewing] = useState<{ items: ViewedItem[]; start: number } | null>(null)
  const [stuck, setStuck] = useState<ReadonlySet<string>>(() => new Set())
  // When this screen first saw each transfer waiting for the other computer.
  const waitingSince = useRef(new Map<string, number>())

  const showError = useCallback((code: ErrorCode) => setNotice(errorMessage(code)), [])
  const showInfo = useCallback((text: string) => setToast((current) => ({ text, id: (current?.id ?? 0) + 1 })), [])
  const clearToast = useCallback(() => setToast(null), [])
  const clearNotice = useCallback(() => setNotice(null), [])
  const clearSearch = useCallback(() => setQuery(''), [])

  // The files the viewer moves between: the pictures, songs and videos in the same section that
  // can be opened, in the order the section shows them (epic 18 stories 2 and 3).
  function viewable(kind: 'share' | 'transfer'): ViewedItem[] {
    const items =
      kind === 'share'
        ? inOrder(shares ?? [], sort, (s) => s.name, (s) => s.sizeBytes)
            .filter((s) => s.status === 'ready')
            .map((s) => ({ kind, id: s.id, name: s.name }))
        : inOrder(transfers ?? [], sort, (tr) => tr.name ?? '', (tr) => tr.sizeBytes)
            .filter((tr) => tr.state === 'done' && tr.name)
            .map((tr) => ({ kind, id: tr.id, name: tr.name ?? '' }))
    return items.filter((item) => playableKind(item.name) !== null)
  }

  const closeViewer = useCallback(() => setViewing(null), [])

  // Any file that arrived or is shared can be opened; one ComParta cannot play shows the message
  // that offers its folder instead (epic 18 stories 4 and 5).
  const canView = (item: DetailsOf) =>
    item.kind === 'share'
      ? shares?.some((s) => s.id === item.id && s.status === 'ready')
      : transfers?.some((tr) => tr.id === item.id && tr.state === 'done')

  function openViewer(item: DetailsOf) {
    const items = viewable(item.kind)
    const at = items.findIndex((file) => file.id === item.id)
    setViewing(at >= 0 ? { items, start: at } : { items: [item], start: 0 })
  }

  // Each guided send that shared a file counts; opting out ends them at once.
  const endGuide = useCallback(
    ({ shared, hide }: { shared: boolean; hide: boolean }) => {
      setGuiding(false)
      const seen = choices.current.sendStepsSeen ?? 0
      const next = hide ? sendStepsShown : shared ? Math.min(seen + 1, sendStepsShown) : seen
      if (next === seen) return
      choices.current = { ...choices.current, sendStepsSeen: next }
      void bridge.invoke('settings.set', choices.current).then((result) => {
        if (!result.ok) showError(result.error.code)
      })
    },
    [bridge, showError],
  )

  useEffect(() => {
    let active = true
    const applySettings = (stored: SettingsDto) => {
      choices.current = {
        language: stored.language,
        view: stored.view,
        sort: stored.sort,
        sendStepsSeen: stored.sendStepsSeen,
        startWithWindows: stored.startWithWindows,
        publicServers: stored.publicServers,
        helperAddress: stored.helperAddress,
        helperKey: stored.helperKey,
        yourName: stored.yourName,
        theme: stored.theme,
      }
      setYourName(stored.yourName ?? '')
      setSenderName(stored.yourName)
      const shownTheme = stored.theme === 'light' || stored.theme === 'dark' ? stored.theme : 'system'
      setTheme(shownTheme)
      if (shownTheme === 'system') delete document.documentElement.dataset.theme
      else document.documentElement.dataset.theme = shownTheme
      setVersion(stored.version)
      setGuidedSend(stored.sendStepsSeen < sendStepsShown)
      setStartWithWindows(stored.startWithWindows)
      setPublicServers(stored.publicServers)
      setHelper({ address: stored.helperAddress, key: stored.helperKey })
      setView(stored.view === 'list' ? 'list' : 'grid')
      setSort(stored.sort === 'name' || stored.sort === 'size' ? stored.sort : 'added')
      const chosen = pickLanguage(stored.language, stored.systemLanguage)
      setLanguage(chosen)
      document.documentElement.lang = chosen
      setShownLanguage(chosen)
    }
    const unsubscribe = bridge.subscribe((event) => {
      if (event.event === 'shares.changed') setShares(event.payload)
      else if (event.event === 'transfers.changed') setTransfers(event.payload)
      else if (event.event === 'settings.changed') applySettings(event.payload)
      else if (event.event === 'connection.changed') {
        setConnection(event.payload.health)
        setThroughHelper(event.payload.throughHelper)
      }
      else if (event.event === 'activity.changed') setActivity(event.payload)
      else if (event.event === 'quit.changed') setQuitting(event.payload.moving)
      // The person clicked a system notification about this item (CAP-20).
      else if (event.event === 'details.changed') setDetails(event.payload)
    })

    void bridge.invoke('activity.get').then((result) => {
      if (active && result.ok) setActivity((current) => current ?? result.value)
    })

    void bridge.invoke('connection.get').then((result) => {
      if (active && result.ok) {
        setConnection(result.value.health)
        setThroughHelper(result.value.throughHelper)
      }
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
    // The button changes at once; the app's own report follows.
    setActivity((current) => current && { ...current, ...stopped })
    const result = await bridge.invoke('activity.set', stopped)
    if (result.ok) return
    showError(result.error.code)
    const now = await bridge.invoke('activity.get')
    if (now.ok) setActivity(now.value)
  }

  /** Continues one side for a file that was stopped with everything else. */
  const continueSending = () =>
    void setStopped({ sendingStopped: false, receivingStopped: activity?.receivingStopped ?? false })
  const continueReceiving = () =>
    void setStopped({ sendingStopped: activity?.sendingStopped ?? false, receivingStopped: false })

  async function setSharePaused(id: string, paused: boolean) {
    clearNotice()
    const result = await bridge.invoke(paused ? 'shares.pause' : 'shares.resume', { id })
    if (!result.ok) showError(result.error.code)
  }

  async function setTransferPaused(id: string, paused: boolean) {
    clearNotice()
    const result = await bridge.invoke(paused ? 'transfers.pause' : 'transfers.resume', { id })
    if (!result.ok) showError(result.error.code)
  }

  async function saveSettings(change: Partial<SetSettingsArgs>) {
    choices.current = { ...choices.current, ...change }
    const result = await bridge.invoke('settings.set', choices.current)
    if (!result.ok) showError(result.error.code)
  }

  function chooseLanguage(next: Language) {
    void saveSettings({ language: next })
  }

  function chooseStartWithWindows(on: boolean) {
    setStartWithWindows(on)
    void saveSettings({ startWithWindows: on })
  }

  async function chooseName(name: string) {
    const before = choices.current
    choices.current = { ...before, yourName: name.trim() || null }
    const result = await bridge.invoke('settings.set', choices.current)
    if (!result.ok) {
      choices.current = before
      showError(result.error.code)
      return false
    }
    showInfo(t('settings.nameSaved'))
    return true
  }

  function chooseTheme(next: Theme) {
    void saveSettings({ theme: next === 'system' ? null : next })
  }

  // On: the guided send runs again for the next sends. Off: Share opens the file dialog at once.
  function chooseGuidedSend(on: boolean) {
    setGuidedSend(on)
    void saveSettings({ sendStepsSeen: on ? 0 : sendStepsShown })
  }

  async function choosePublicServers(servers: string[]) {
    const before = choices.current
    choices.current = { ...before, publicServers: servers }
    const result = await bridge.invoke('settings.set', choices.current)
    if (!result.ok) {
      choices.current = before
      showError(result.error.code)
      return false
    }
    setPublicServers(servers)
    showInfo(t('settings.serversSaved'))
    return true
  }

  async function chooseHelper(next: { address: string; key: string } | null) {
    const before = choices.current
    choices.current = { ...before, helperAddress: next?.address ?? null, helperKey: next?.key ?? null }
    const result = await bridge.invoke('settings.set', choices.current)
    if (!result.ok) {
      choices.current = before
      showError(result.error.code)
      return false
    }
    setHelper({ address: next?.address ?? null, key: next?.key ?? null })
    showInfo(t(next ? 'settings.helperSaved' : 'settings.helperOff'))
    return true
  }

  async function quitAnyway() {
    setQuitting(null)
    const result = await bridge.invoke('app.quit', { force: true })
    if (!result.ok) showError(result.error.code)
  }

  // The screen changes at once; the stored choice follows.
  function changeView(next: ViewMode) {
    setView(next)
    void saveSettings({ view: next })
  }

  function changeSort(next: SortOrder) {
    setSort(next)
    void saveSettings({ sort: next })
  }

  async function add() {
    clearNotice()
    setSection('shared')
    if ((choices.current.sendStepsSeen ?? 0) < sendStepsShown) {
      setGuiding(true)
      return
    }
    setAdding(true)
    try {
      // The app opens the file dialog; a cancel comes back as ok with no value.
      const result = await bridge.invoke('shares.add')
      if (!result.ok) showError(result.error.code)
    } finally {
      setAdding(false)
    }
  }

  // Dropped files are shared as if picked. Folders wait for epic 11; the page never sees a path,
  // it only hands the files to the window under a token (AD-7).
  async function drop(data: DataTransfer) {
    clearNotice()
    const entries = [...data.items].map((item) => item.webkitGetAsEntry?.() ?? null)
    const files = [...data.files].filter((_, i) => !entries[i]?.isDirectory)
    const folders = files.length < data.files.length
    if (files.length === 0) {
      if (folders) setNotice(t('drop.folderNotYet'))
      return
    }
    setSection('shared')
    const token = crypto.randomUUID()
    if (!bridge.handOverDrop(token, files)) {
      showError('share.dropFailed')
      return
    }
    const result = await bridge.invoke('shares.addDropped', { token })
    if (!result.ok) {
      showError(result.error.code)
      return
    }
    const { added, problem } = result.value
    if (problem) showError(problem as ErrorCode)
    else if (folders) setNotice(t('drop.folderNotYet'))
    if (added > 0) showInfo(added === 1 ? t('drop.sharedOne') : t('drop.sharedMany', { count: added }))
  }

  const isFileDrag = (event: DragEvent) => event.dataTransfer.types.includes('Files')

  function startReceiving() {
    clearNotice()
    setPasted(undefined)
    setReceiving(true)
  }

  function stopReceiving() {
    setReceiving(false)
    setPasted(undefined)
  }

  const speeds = useMemo(
    () =>
      new Map<string, ItemSpeed>(
        (activity?.items ?? []).map((item) => [
          item.id,
          { receiving: item.receivingBytesPerSecond, sending: item.sendingBytesPerSecond },
        ]),
      ),
    [activity],
  )

  // The details window offers the same pause or play button as the file's card.
  const detailsShare = details?.kind === 'share' ? shares?.find((s) => s.id === details.id) : undefined
  const detailsTransfer = details?.kind === 'transfer' ? transfers?.find((t) => t.id === details.id) : undefined
  const detailsToggle = detailsShare
    ? shareToggle(detailsShare, activity?.sendingStopped ?? false, {
        setPaused: (paused) => void setSharePaused(detailsShare.id, paused),
        continueAll: continueSending,
      })
    : detailsTransfer
      ? transferToggle(detailsTransfer, activity?.receivingStopped ?? false, {
          setPaused: (paused) => void setTransferPaused(detailsTransfer.id, paused),
          continueAll: continueReceiving,
        })
      : undefined

  const shared = (
    <SharesSection
      bridge={bridge}
      shares={shares}
      query={query}
      view={view}
      onViewChange={changeView}
      sort={sort}
      onSortChange={changeSort}
      onError={showError}
      onInfo={showInfo}
      adding={adding}
      onAdd={add}
      onStart={clearNotice}
      onClearSearch={clearSearch}
      stopped={activity?.sendingStopped ?? false}
      speeds={speeds}
      onSetPaused={(id, paused) => void setSharePaused(id, paused)}
      onContinueAll={continueSending}
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
      sort={sort}
      onSortChange={changeSort}
      onError={showError}
      onInfo={showInfo}
      onStart={clearNotice}
      onClearSearch={clearSearch}
      stuck={stuck}
      onHelp={() => setHelping(true)}
      stopped={activity?.receivingStopped ?? false}
      speeds={speeds}
      onSetPaused={(id, paused) => void setTransferPaused(id, paused)}
      onContinueAll={continueReceiving}
      onDetails={setDetails}
      onReceive={startReceiving}
      onView={openViewer}
    />
  )

  return (
    <div
      className="app"
      onDragOver={(event) => {
        if (!isFileDrag(event)) return
        event.preventDefault()
        event.dataTransfer.dropEffect = 'copy'
        setDragging(true)
      }}
      onDragLeave={(event) => {
        // Leaving one element for another inside the window is not leaving the window.
        if (!event.relatedTarget) setDragging(false)
      }}
      onDrop={(event) => {
        if (!isFileDrag(event)) return
        event.preventDefault()
        setDragging(false)
        void drop(event.dataTransfer)
      }}
    >
      {dragging && (
        <div className="drop-overlay" aria-hidden="true">
          <p>{t('drop.here')}</p>
        </div>
      )}
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
        throughHelper={throughHelper}
        onConnection={() => setHelping(true)}
        onSection={setSection}
        onAdd={add}
        onReceive={startReceiving}
        onHelp={() => setOpened('help')}
        onSettings={() => setOpened('settings')}
      />

      <ActionButton adding={adding} onAdd={add} onReceive={startReceiving} />

      <main className="content">
        {notice && <Notice text={notice} onDismiss={clearNotice} />}
        {section === 'shared' ? shared : received}
      </main>

      {toast && <Toast key={toast.id} text={toast.text} closeLabel={t('notice.dismiss')} onClose={clearToast} />}

      {guiding && (
        <SendSteps bridge={bridge} shares={shares} onError={showError} onInfo={showInfo} onClose={endGuide} />
      )}
      {details && (
        <DetailsWindow
          bridge={bridge}
          item={details}
          stopped={(details.kind === 'share' ? activity?.sendingStopped : activity?.receivingStopped) ?? false}
          toggle={detailsToggle}
          onClose={() => setDetails(null)}
          onError={showError}
          onInfo={showInfo}
          onView={canView(details) ? () => openViewer(details) : undefined}
        />
      )}

      {viewing && (
        <MediaViewer
          bridge={bridge}
          items={viewing.items}
          start={viewing.start}
          onClose={closeViewer}
          onError={showError}
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
        <SettingsDialog
          language={language}
          onLanguage={chooseLanguage}
          startWithWindows={startWithWindows}
          onStartWithWindows={chooseStartWithWindows}
          publicServers={publicServers}
          onPublicServers={choosePublicServers}
          helperAddress={helper.address}
          helperKey={helper.key}
          onHelper={chooseHelper}
          yourName={yourName}
          onYourName={chooseName}
          theme={theme}
          onTheme={chooseTheme}
          guidedSend={guidedSend}
          onGuidedSend={chooseGuidedSend}
          version={version}
          onClose={() => setOpened(null)}
        />
      )}

      {quitting && <QuitDialog moving={quitting} onConfirm={quitAnyway} onCancel={() => setQuitting(null)} />}

      {helping && (
        <ConnectionHelp health={connection} throughHelper={throughHelper} onOpenSettings={openConnectionSettings} onClose={() => setHelping(false)} />
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
