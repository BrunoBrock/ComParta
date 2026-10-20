import type { Bridge } from './bridge.ts'
import type {
  ActivityDto,
  AddPickedArgs,
  CommandResult,
  ConnectionDto,
  ConnectionHealthDto,
  DetailsTargetDto,
  ErrorCode,
  EventEnvelope,
  CopyInviteArgs,
  IdArgs,
  InvitePreviewDto,
  ItemSpeedDto,
  ReceiveArgs,
  SetActivityArgs,
  QuitArgs,
  SetSettingsArgs,
  SettingsDto,
  ShareDetailsDto,
  ShareDto,
  DropArgs,
  MediaArgs,
  ThumbnailArgs,
  TransferDetailsDto,
  TransferDto,
  TransferSourceDto,
} from './contract.generated.ts'
import { isPicture, playableKind } from '../fileNames.ts'
import { readHelper } from '../helper.ts'
import { readPublicServers } from '../publicServers.ts'

export interface PickedFile {
  name: string
  sizeBytes: number
}

/** A folder in the fake's folder dialog or dropped on the window: its name, size and how many files it holds. */
export interface PickedFolder extends PickedFile {
  itemCount: number
  /** How many of its files are photos or videos; none when left out. */
  pictures?: number
}

export interface FakeBridgeOptions {
  shares?: ShareDto[]
  transfers?: TransferDto[]
  /** Whether another device can reach this computer; good when left out, null while nothing is shared or received. */
  connection?: ConnectionHealthDto | null
  /** Milliseconds a new share stays "preparing" and between steps of a transfer. 0 means never advance on its own. */
  stepMs?: number
  /** Room left for received files; null when it cannot be told. */
  freeBytes?: number | null
  /** The operating system's language; English when left out. */
  systemLanguage?: string
  /** A language the person chose earlier. */
  language?: string | null
  /** Sending or receiving already stopped from the speed bar, as the app remembers it. */
  stopped?: Partial<SetActivityArgs>
  /** How files were shown and ordered last time, as the app stored it. */
  view?: string | null
  sort?: string | null
  /** How many guided sends the person has been through; 3 means the steps no longer show. */
  sendStepsSeen?: number
  /** Whether the person chose to start with Windows. */
  startWithWindows?: boolean
  /** The public servers the person named, as the app stored them. */
  publicServers?: string[]
  /** The helper computer the person named, as the app stored it. */
  helper?: { address: string; key: string }
  /** Whether the first-run welcome was done; done when left out, so only its own tests see it. */
  welcomeDone?: boolean
  /** The name and colors the person chose in Settings. */
  yourName?: string
  theme?: 'light' | 'dark'
}

export interface FakeBridge extends Bridge {
  /** What the next "file dialog" returns; several files wait for a name; null means the person cancelled. */
  nextPick: PickedFile | PickedFile[] | null
  /** What the next folder dialog returns; null means the person cancelled. */
  nextFolder: PickedFolder | null
  /** Dropped items with these names are folders, as the window would find. */
  folders: Record<string, PickedFolder>
  /** Make the next command fail with this code. */
  failNextWith: ErrorCode | null
  /** What the next "open a download file" dialog returns; null means cancelled, 'invalid' a wrong file. */
  nextDownloadFile: PickedFile | 'invalid' | null
  /** Text of the last invite message "copied". */
  copiedInvite: string | null
  /** The last support report put on the clipboard. */
  copiedReport: string | null
  /** Ids of shares whose download file was "saved", in order. */
  savedDownloadFiles: string[]
  /** What the system clipboard holds, for the "found an invite you copied" path. */
  clipboardText: string | null
  openedFolder: number
  /** Ids of received files "opened" and "shown in their folder", in order. */
  openedReceived: string[]
  revealedReceived: string[]
  /** Shared files shown in their folder. */
  revealedShares: string[]
  /** How many times the firewall settings were opened. */
  openedConnectionSettings: number
  /** How many times the app quit. */
  quits: number
  /** The person chose Quit on the notification-area icon while files were moving. */
  quitFromIcon(): void
  /** An invite link clicked outside the app, as the app reports it. */
  openInvite(text: string): void
  /** Change state from outside, as the app does when something happens on its own. */
  setShares(shares: ShareDto[]): void
  /** The person clicked a system notification about this item. */
  clickNotification(target: DetailsTargetDto): void
  /** Another device gets a share, a quarter at a time each step, then it reads as sent. */
  sendTo(shareId: string): void
  setTransfers(transfers: TransferDto[]): void
  /**
   * Change the connection health, as the app does when it learns more; null means it cannot be told.
   * Setting the value it already has raises no event, as in the app.
   */
  setConnection(health: ConnectionHealthDto | null): void
}

/** What a share reports while nobody is getting it. */
export const notSending = {
  activity: 'idle',
  receiverCount: 0,
  percent: 0,
  finishedCount: 0,
  secondsLeft: null,
  neededBytes: null,
  paused: false,
  itemCount: 1,
  kind: 'file',
} as const

/** Roughly what one ready share sends while someone is getting it, in the moving fake. */
export const fakeSendingPerShare = 180_000
const addedLongAgo = '2026-10-10T14:05:00.000Z'

// The fake's invites are shaped like the engine's links (AD-16, amended: an address, then the name
// and size) but carry the share id in the clear.
// The ComParta links of earlier builds are still read.
const invitePrefix = 'link:?id='
const invitePattern = /link:\?id=([A-Za-z0-9_-]+)[^\s"'<>]*/i
const oldInvitePrefix = 'comparta://receive/v1/'
const oldInvitePattern = /comparta:\/\/receive\/v1\/([A-Za-z0-9_-]+)/i
const downloadLinkPrefix = 'https://example.org/download/'
const demoInvite = { senderName: 'Paulo', name: 'Birthday party.mp4', sizeBytes: 250_000_000 }
/** A made-up picture for a photo or video, coloured from its name, so the browser preview shows thumbnails. */
export function fakePicture(name: string): string {
  const hue = [...name].reduce((sum, c) => (sum + c.charCodeAt(0) * 7) % 360, 0)
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="160">` +
    `<defs><linearGradient id="g" x2="1" y2="1"><stop offset="0" stop-color="hsl(${hue} 60% 70%)"/>` +
    `<stop offset="1" stop-color="hsl(${(hue + 60) % 360} 50% 45%)"/></linearGradient></defs>` +
    `<rect width="240" height="160" fill="url(#g)"/><circle cx="180" cy="48" r="20" fill="hsl(50 90% 85%)"/>` +
    `<path d="M0 160 L80 80 L140 130 L180 100 L240 160 Z" fill="hsl(${hue} 40% 30%)"/></svg>`
  return `data:image/svg+xml;base64,${btoa(svg)}`
}

const ok = <T>(value: T): CommandResult<T> => ({ ok: true, value })
const fail = (code: ErrorCode): CommandResult<never> => ({ ok: false, error: { code } })

const samplePicks: PickedFile[] = [
  { name: 'Birthday party.mp4', sizeBytes: 1_288_490_188 },
  { name: 'Holiday photos.zip', sizeBytes: 734_003_200 },
  { name: 'Recipe from grandma.pdf', sizeBytes: 482_113 },
]

/**
 * Implements the whole contract in memory so the UI runs in a normal browser and in tests
 * without the app (AD-12).
 */
export function createFakeBridge(options: FakeBridgeOptions = {}): FakeBridge {
  let shares = [...(options.shares ?? [])]
  let transfers = [...(options.transfers ?? [])]
  let connection: ConnectionHealthDto | null = options.connection === undefined ? 'good' : options.connection
  let nextId = 1
  let nextSample = 0
  let mediaOpened = 0
  const stepMs = options.stepMs ?? 0
  const listeners = new Set<(event: EventEnvelope) => void>()

  const emit = (event: EventEnvelope) => listeners.forEach((listener) => listener(event))
  let stopped: SetActivityArgs = { sendingStopped: false, receivingStopped: false, ...options.stopped }
  // What the app knows about each item beyond the list: when it was added and where it came from.
  const addedAt = new Map<string, string>()
  const sources = new Map<string, TransferSourceDto>()

  // In the moving fake every ready share that is not paused sends a little; files that do not move are left out.
  const itemSpeeds = (): ItemSpeedDto[] => [
    ...(stopped.sendingStopped || stepMs === 0
      ? []
      : shares
          .filter((s) => s.status === 'ready' && !s.paused)
          .map((s) => ({ id: s.id, receivingBytesPerSecond: 0, sendingBytesPerSecond: fakeSendingPerShare }))),
    ...(stopped.receivingStopped
      ? []
      : transfers
          .filter((t) => t.state === 'receiving' && t.bytesPerSecond > 0)
          .map((t) => ({ id: t.id, receivingBytesPerSecond: t.bytesPerSecond, sendingBytesPerSecond: 0 }))),
  ]
  const activity = (): ActivityDto => {
    const items = itemSpeeds()
    return {
      ...stopped,
      receivingBytesPerSecond: items.reduce((sum, i) => sum + i.receivingBytesPerSecond, 0),
      sendingBytesPerSecond: items.reduce((sum, i) => sum + i.sendingBytesPerSecond, 0),
      items,
    }
  }
  const emitActivity = () => emit({ event: 'activity.changed', payload: activity() })
  const emitShares = () => {
    emit({ event: 'shares.changed', payload: [...shares] })
    emitActivity()
  }
  const emitTransfers = () => {
    emit({ event: 'transfers.changed', payload: [...transfers] })
    emitActivity()
  }
  // As the app: a helper computer that holds the files makes them reachable whatever this computer's connection.
  const connectionDto = (): ConnectionDto =>
    settings.helperAddress !== null ? { health: 'good', throughHelper: true } : { health: connection, throughHelper: false }
  const emitConnection = () => emit({ event: 'connection.changed', payload: connectionDto() })
  const newId = () => `fake-${nextId++}`

  const updateTransfer = (id: string, change: Partial<TransferDto>) => {
    transfers = transfers.map((t) => (t.id === id ? { ...t, ...change } : t))
    emitTransfers()
  }

  const simulateTransfer = (id: string, name: string, total: number) => {
    if (stepMs === 0) return
    const step = total / 12
    // Pretend each step takes four seconds, so the speed and time left read like real ones.
    const bytesPerSecond = Math.round(step / 4)
    let received = 0
    setTimeout(() => {
      updateTransfer(id, { name, sizeBytes: total })
      const timer = setInterval(() => {
        const current = transfers.find((t) => t.id === id)
        if (!current) {
          clearInterval(timer)
          return
        }
        if (current.state === 'paused' || stopped.receivingStopped) return
        received = Math.min(total, received + step)
        if (received >= total) {
          clearInterval(timer)
          updateTransfer(id, {
            receivedBytes: total,
            state: 'done',
            bytesPerSecond: 0,
            secondsLeft: null,
            neededBytes: null,
          })
        } else {
          updateTransfer(id, {
            receivedBytes: Math.round(received),
            state: 'receiving',
            bytesPerSecond,
            secondsLeft: Math.ceil((total - received) / bytesPerSecond),
            neededBytes: null,
          })
        }
      }, stepMs)
    }, stepMs * 2)
  }

  const startTransfer = (
    id: string,
    name: string,
    total: number,
    source: TransferSourceDto,
    known: Partial<TransferDto> = {},
  ) => {
    if (transfers.some((t) => t.id === id && t.state !== 'done')) return fail('transfer.duplicate')
    addedAt.set(id, new Date().toISOString())
    sources.set(id, source)
    const transfer: TransferDto = {
      id,
      name: null,
      sizeBytes: null,
      receivedBytes: 0,
      state: 'connecting',
      bytesPerSecond: 0,
      secondsLeft: null,
      neededBytes: null,
      ...known,
    }
    transfers = [...transfers.filter((t) => t.id !== id), transfer]
    emitTransfers()
    simulateTransfer(id, name, total)
    return ok(transfer)
  }

  const setPaused = (id: string, paused: boolean) => {
    const transfer = transfers.find((t) => t.id === id)
    if (!transfer) return fail('transfer.notFound')
    if (transfer.state === 'done' || transfer.state === 'failed') return fail('transfer.finished')
    if (paused) updateTransfer(id, { state: 'paused', bytesPerSecond: 0, secondsLeft: null, neededBytes: null })
    else if (transfer.state === 'paused') updateTransfer(id, { state: transfer.sizeBytes ? 'receiving' : 'connecting' })
    return ok(null)
  }

  const setSharePaused = (id: string, paused: boolean) => {
    if (!shares.some((s) => s.id === id)) return fail('share.notFound')
    shares = shares.map((s) => (s.id === id ? { ...s, paused } : s))
    emitShares()
    return ok(null)
  }

  // Only a file that has arrived can be opened, shown or deleted, as in the app.
  const withArrived = (id: string, act: (id: string) => void) => {
    const transfer = transfers.find((t) => t.id === id)
    if (!transfer) return fail('transfer.notFound')
    if (transfer.state !== 'done') return fail('received.notFound')
    act(id)
    return ok(null)
  }

  let openedInvite: string | null = null
  let problems: { at: Date; code: ErrorCode }[] = []
  let settings: SettingsDto = {
    language: options.language ?? null,
    systemLanguage: options.systemLanguage ?? 'en',
    view: options.view ?? null,
    sort: options.sort ?? null,
    sendStepsSeen: options.sendStepsSeen ?? 0,
    startWithWindows: options.startWithWindows ?? false,
    publicServers: options.publicServers ?? [],
    helperAddress: options.helper?.address ?? null,
    helperKey: options.helper?.key ?? null,
    yourName: options.yourName ?? null,
    theme: options.theme ?? null,
    version: '1.2.0',
    welcomeDone: options.welcomeDone ?? true,
  }
  // What quitting would stop: shares someone is getting and files arriving, as the app counts them.
  const moving = () => [
    ...shares.filter((s) => s.activity === 'sending').map((s) => s.name),
    ...transfers.filter((t) => t.state === 'receiving').map((t) => t.name ?? ''),
  ]
  const freeBytes = options.freeBytes === undefined ? 120 * 1024 ** 3 : options.freeBytes

  /** Reads text the way the app's invite codec and engine do, or null when it is neither. */
  const read = (text: string): { key: string; preview: InvitePreviewDto } | null => {
    const old = oldInvitePattern.exec(text)
    if (old) {
      const share = shares.find((s) => s.id === old[1])
      const hints = share ? { senderName: null, name: share.name, sizeBytes: share.sizeBytes } : demoInvite
      return { key: `from-${old[1]}`, preview: { ...hints, itemCount: 1, isInvite: true, freeBytes } }
    }
    // The app's own link reads like any download link: the name and size are the ones it carries.
    const found = invitePattern.exec(text)
    if (found) {
      const share = shares.find((s) => s.id === found[1])
      const { name, sizeBytes } = share ?? demoInvite
      return {
        key: `from-${found[1]}`,
        preview: { senderName: null, name, sizeBytes, itemCount: null, isInvite: false, freeBytes },
      }
    }
    const trimmed = text.trim()
    if (trimmed.startsWith(downloadLinkPrefix)) {
      const name = decodeURIComponent(trimmed.slice(downloadLinkPrefix.length)) || 'Download'
      return {
        key: `link-${name}`,
        preview: { senderName: null, name, sizeBytes: 1_800_000_000, itemCount: null, isInvite: false, freeBytes },
      }
    }
    return null
  }

  // Shares one picked or dropped file the way the app does: no duplicates, no empty files.
  const shareFile = (pick: PickedFile): CommandResult<ShareDto> => {
    if (shares.some((s) => s.name === pick.name)) return fail('share.duplicate')
    if (pick.sizeBytes === 0) return fail('file.unreadable')

    const share: ShareDto = { id: newId(), ...pick, status: stepMs === 0 ? 'ready' : 'preparing', ...notSending }
    shares = [...shares, share]
    addedAt.set(share.id, new Date().toISOString())
    emitShares()
    if (stepMs > 0) {
      setTimeout(() => {
        shares = shares.map((s) => (s.id === share.id ? { ...s, status: 'ready' } : s))
        emitShares()
      }, stepMs * 2)
    }
    return ok(share)
  }

  // Several files chosen together, waiting for their name.
  let picked: PickedFile[] | null = null

  const shareItems = (name: string, kind: 'folder' | 'collection', itemCount: number, sizeBytes: number) => {
    const made = shareFile({ name, sizeBytes })
    if (!made.ok) return made
    shares = shares.map((s) => (s.id === made.value.id ? { ...s, itemCount, kind } : s))
    emitShares()
    return ok(shares.find((s) => s.id === made.value.id)!)
  }

  // The photos and videos in each shared folder, as the app would count them.
  const picturesIn = new Map<string, number>()

  const shareFolder = (folder: PickedFolder) => {
    if (folder.itemCount === 0) return fail('share.folderEmpty')
    const made = shareItems(folder.name, 'folder', folder.itemCount, folder.sizeBytes)
    if (made.ok) picturesIn.set(made.value.id, folder.pictures ?? 0)
    return made
  }

  // Files the page handed over with a drop, by token, as the window does in the app.
  const dropped = new Map<string, PickedFile[]>()

  const handlers: Record<string, (args: unknown) => CommandResult<unknown>> = {
    'shares.list': () => ok([...shares]),

    'shares.add': () => {
      const pick = fake.nextPick
      picked = null
      if (pick === null || (Array.isArray(pick) && pick.length === 0)) return ok({ share: null, filesToName: 0 })
      fake.nextPick = samplePicks[++nextSample % samplePicks.length]
      if (Array.isArray(pick) && pick.length > 1) {
        picked = pick
        return ok({ share: null, filesToName: pick.length })
      }
      const one = shareFile(Array.isArray(pick) ? pick[0] : pick)
      return one.ok ? ok({ share: one.value, filesToName: 0 }) : one
    },

    // As core: the name is trimmed, and refused when empty, too long or not possible for a folder.
    'shares.addPicked': (args) => {
      const { name } = args as AddPickedArgs
      const files = picked
      if (name === null || files === null) {
        picked = null
        return ok(null)
      }
      const trimmed = name.trim()
      // eslint-disable-next-line no-control-regex
      if (!trimmed || trimmed.length > 100 || trimmed.endsWith('.') || /[<>:"/\\|?*\u0000-\u001f]/.test(trimmed))
        return fail('share.nameInvalid')
      picked = null
      return shareItems(trimmed, 'collection', files.length, files.reduce((sum, file) => sum + file.sizeBytes, 0))
    },

    'shares.addFolder': () => {
      const folder = fake.nextFolder
      return folder === null ? ok(null) : shareFolder(folder)
    },

    'shares.addDropped': (args) => {
      const { token } = args as DropArgs
      const files = dropped.get(token)
      dropped.delete(token)
      if (!files) return fail('share.dropFailed')
      let added = 0
      let problem: ErrorCode | null = null
      for (const file of files) {
        const result = fake.folders[file.name] ? shareFolder(fake.folders[file.name]) : shareFile(file)
        if (result.ok) added++
        else problem ??= result.error.code
      }
      return ok({ added, problem })
    },

    'shares.remove': (args) => {
      const { id } = args as IdArgs
      if (!shares.some((s) => s.id === id)) return fail('share.notFound')
      shares = shares.filter((s) => s.id !== id)
      emitShares()
      return ok(null)
    },

    'shares.pause': (args) => setSharePaused((args as IdArgs).id, true),

    'shares.resume': (args) => setSharePaused((args as IdArgs).id, false),

    'shares.reveal': (args) => {
      const { id } = args as IdArgs
      if (!shares.some((s) => s.id === id)) return fail('share.notFound')
      fake.revealedShares.push(id)
      return ok(null)
    },

    'shares.copyInvite': (args) => {
      const { id, message } = args as CopyInviteArgs
      const share = shares.find((s) => s.id === id)
      if (!share) return fail('share.notFound')
      if (share.status !== 'ready') return fail('share.notReady')
      const link = `${invitePrefix}${share.id}&dn=${encodeURIComponent(share.name)}&xl=${share.sizeBytes}`
      fake.copiedInvite = message
        ? message.includes('{link}')
          ? message.replace('{link}', link)
          : `${message}\n${link}`
        : link
      return ok(null)
    },

    'shares.saveDownloadFile': (args) => {
      const { id } = args as IdArgs
      const share = shares.find((s) => s.id === id)
      if (!share) return fail('share.notFound')
      if (share.status !== 'ready') return fail('share.notReady')
      fake.savedDownloadFiles.push(id)
      // As the app: a numbered name when one is already there. The app's names end in the engine's
      // own extension, which the plain-words guard keeps out of this folder; the screen never shows them.
      const copies = fake.savedDownloadFiles.filter((saved) => saved === id).length
      return ok(copies === 1 ? `${share.name}.download` : `${share.name} (${copies}).download`)
    },

    'transfers.list': () => ok([...transfers]),

    'transfers.preview': (args) => {
      const found = read((args as ReceiveArgs).invite ?? '')
      return found ? ok(found.preview) : fail('invite.invalid')
    },

    'transfers.receive': (args) => {
      const found = read((args as ReceiveArgs).invite ?? '')
      if (!found) return fail('invite.invalid')
      const { name, sizeBytes, isInvite } = found.preview
      if (sizeBytes !== null && freeBytes !== null && sizeBytes > freeBytes) return fail('disk.full')
      // Names from an invite are hints only; the name appears once the file's details arrive.
      return startTransfer(
        found.key,
        name ?? 'File',
        sizeBytes ?? 250_000_000,
        isInvite ? 'invite' : 'downloadLink',
        isInvite ? {} : { name },
      )
    },

    'clipboard.findInvite': () => {
      const text = fake.clipboardText ?? ''
      const found = oldInvitePattern.exec(text) ?? invitePattern.exec(text)
      return ok(found ? found[0] : null)
    },

    'transfers.openFile': () => {
      const file = fake.nextDownloadFile
      if (file === null) return ok(null)
      if (file === 'invalid') return fail('download.invalidFile')
      return startTransfer(`file-${file.name}`, file.name, file.sizeBytes, 'downloadFile', {
        name: file.name,
        sizeBytes: file.sizeBytes,
      })
    },

    'transfers.pause': (args) => setPaused((args as IdArgs).id, true),

    'transfers.resume': (args) => setPaused((args as IdArgs).id, false),

    'transfers.remove': (args) => {
      const { id } = args as IdArgs
      if (!transfers.some((t) => t.id === id)) return fail('transfer.notFound')
      transfers = transfers.filter((t) => t.id !== id)
      emitTransfers()
      return ok(null)
    },

    'settings.get': () => ok({ ...settings }),

    'settings.set': (args) => {
      const { language, view, sort } = args as SetSettingsArgs
      const sendStepsSeen = (args as SetSettingsArgs).sendStepsSeen ?? 0
      if (
        (view !== null && view !== 'grid' && view !== 'list') ||
        (sort !== null && !['added', 'name', 'size'].includes(sort)) ||
        sendStepsSeen < 0 ||
        sendStepsSeen > 3
      )
        return fail('settings.invalid')
      const startWithWindows = (args as SetSettingsArgs).startWithWindows ?? false
      // As core: the same rules, blank lines and repeats dropped.
      const entries = (args as SetSettingsArgs).publicServers ?? []
      const read = readPublicServers(entries.join('\n'))
      if (!('servers' in read) || entries.some((entry) => entry.includes('\n'))) return fail('settings.invalid')
      const helper = readHelper((args as SetSettingsArgs).helperAddress ?? '', (args as SetSettingsArgs).helperKey ?? '')
      if (!('helper' in helper)) return fail('settings.invalid')
      // As core: a blank name is none; a long one or one with control characters is refused.
      const yourName = (args as SetSettingsArgs).yourName?.trim() || null
      // eslint-disable-next-line no-control-regex
      if (yourName !== null && (yourName.length > 40 || /[\u0000-\u001f\u007f-\u009f]/.test(yourName)))
        return fail('settings.invalid')
      const theme = (args as SetSettingsArgs).theme ?? null
      if (theme !== null && theme !== 'light' && theme !== 'dark') return fail('settings.invalid')
      const connectionBefore = connectionDto()
      settings = {
        ...settings,
        language,
        view,
        sort,
        sendStepsSeen,
        startWithWindows,
        publicServers: read.servers,
        helperAddress: helper.helper?.address ?? null,
        helperKey: helper.helper?.key ?? null,
        yourName,
        theme,
        welcomeDone: (args as SetSettingsArgs).welcomeDone ?? false,
      }
      emit({ event: 'settings.changed', payload: { ...settings } })
      const connectionAfter = connectionDto()
      if (connectionAfter.health !== connectionBefore.health || connectionAfter.throughHelper !== connectionBefore.throughHelper)
        emitConnection()
      return ok(null)
    },

    'connection.get': () => ok(connectionDto()),

    'activity.get': () => ok(activity()),

    'activity.set': (args) => {
      const { sendingStopped, receivingStopped } = args as SetActivityArgs
      stopped = { sendingStopped, receivingStopped }
      emitActivity()
      return ok(null)
    },

    'app.quit': (args) => {
      const names = moving()
      if (!(args as QuitArgs).force && names.length > 0) return ok({ quitting: false, moving: names })
      fake.quits++
      return ok({ quitting: true, moving: [] })
    },

    // As the app: the facts for whoever helps, the last problems with their times, and never a name, path or invite.
    'support.report': () => {
      const counts = (words: string[]) => {
        const each = [...new Set(words)].sort().map((word) => `${word} ${words.filter((w) => w === word).length}`)
        return words.length === 0 ? '0' : `${words.length} (${each.join(', ')})`
      }
      const time = (at: Date) => `${at.toISOString().slice(0, 16).replace('T', ' ')} UTC`
      const report = [
        'ComParta report',
        `Made: ${time(new Date())}`,
        `Version: ${settings.version || 'not known'}`,
        'System: Microsoft Windows 10.0.22631',
        `Language: ${settings.language ?? 'same as the system'} (system ${settings.systemLanguage})`,
        '',
        `Connection: ${connectionDto().health ?? 'not known yet'}`,
        `Helper computer named: ${settings.helperAddress === null ? 'no' : 'yes'}`,
        `Public servers named: ${settings.publicServers.length}`,
        '',
        `Shared: ${counts(shares.map((s) => (s.paused ? 'paused' : s.status === 'partlyUnavailable' ? 'partly unavailable' : s.status)))}`,
        `Received: ${counts(transfers.map((t) => t.state))}`,
        '',
        'Last problems, newest last:',
        ...(problems.length === 0 ? ['none'] : problems.map((p) => `${time(p.at)}  ${p.code}`)),
        '',
      ].join('\n')
      fake.copiedReport = report
      return ok(report)
    },

    'media.open': (args) => {
      const { kind, id } = args as MediaArgs
      let name: string
      if (kind === 'share') {
        const share = shares.find((s) => s.id === id)
        if (!share) return fail('share.notFound')
        // The app cannot read a file that has gone from its place.
        if (share.status === 'unavailable') return fail('media.notPlayable')
        name = share.name
      } else {
        const transfer = transfers.find((t) => t.id === id)
        if (!transfer) return fail('transfer.notFound')
        if (transfer.state !== 'done' || !transfer.name) return fail('received.notFound')
        name = transfer.name
      }
      const playing = playableKind(name)
      if (!playing) return fail('media.notPlayable')
      // As the app: an address that reads this one file, never its path.
      mediaOpened++
      return ok({ url: `https://media.comparta.invalid/${mediaOpened.toString(16).padStart(32, '0')}`, kind: playing, name })
    },

    'thumbnails.get': (args) => {
      const { kind, id, item } = args as ThumbnailArgs
      const folder = kind === 'share' ? shares.find((s) => s.id === id && s.kind !== 'file') : undefined
      if (folder) {
        if (folder.status === 'unavailable' || folder.status === 'partlyUnavailable') return ok(null)
        return ok(item >= 0 && item < Math.min(4, picturesIn.get(id) ?? 0) ? fakePicture(`${folder.name} ${item}.jpg`) : null)
      }
      if (item !== 0) return ok(null)
      const name =
        kind === 'share'
          ? shares.find((s) => s.id === id && s.status !== 'unavailable' && s.status !== 'partlyUnavailable')?.name
          : (transfers.find((t) => t.id === id && t.state === 'done')?.name ?? undefined)
      if (!name) return kind === 'share' && !shares.some((s) => s.id === id) ? fail('share.notFound') : ok(null)
      return ok(isPicture(name) ? fakePicture(name) : null)
    },

    'shares.details': (args) => {
      const share = shares.find((s) => s.id === (args as IdArgs).id)
      if (!share) return fail('share.notFound')
      const sending = share.status === 'ready' && !share.paused && !stopped.sendingStopped && stepMs > 0
      const details: ShareDetailsDto = {
        ...share,
        folder: 'C:\\Users\\Ana\\Videos',
        addedAt: addedAt.get(share.id) ?? addedLongAgo,
        sendingBytesPerSecond: sending ? fakeSendingPerShare : 0,
        connections: sending ? 1 : 0,
        bytesSent: share.status === 'ready' ? Math.round(share.sizeBytes / 3) : 0,
      }
      return ok(details)
    },

    'transfers.details': (args) => {
      const transfer = transfers.find((t) => t.id === (args as IdArgs).id)
      if (!transfer) return fail('transfer.notFound')
      const moving = transfer.state === 'receiving' && !stopped.receivingStopped
      const details: TransferDetailsDto = {
        id: transfer.id,
        name: transfer.name,
        sizeBytes: transfer.sizeBytes,
        receivedBytes: transfer.receivedBytes,
        state: transfer.state,
        source: sources.get(transfer.id) ?? 'invite',
        addedAt: addedAt.get(transfer.id) ?? addedLongAgo,
        receivingBytesPerSecond: moving ? transfer.bytesPerSecond : 0,
        sendingBytesPerSecond: moving ? Math.round(transfer.bytesPerSecond / 10) : 0,
        secondsLeft: moving ? transfer.secondsLeft : null,
        connections: moving ? 3 : 0,
        bytesSent: Math.round(transfer.receivedBytes / 10),
      }
      return ok(details)
    },

    'connection.openSettings': () => {
      fake.openedConnectionSettings++
      return ok(null)
    },

    'invites.takeOpened': () => {
      const taken = openedInvite
      openedInvite = null
      return ok(taken)
    },

    'received.openFolder': () => {
      fake.openedFolder++
      return ok(null)
    },

    'received.open': (args) => withArrived((args as IdArgs).id, (id) => fake.openedReceived.push(id)),

    'received.reveal': (args) => withArrived((args as IdArgs).id, (id) => fake.revealedReceived.push(id)),

    'received.delete': (args) =>
      withArrived((args as IdArgs).id, (id) => {
        transfers = transfers.filter((t) => t.id !== id)
        emitTransfers()
      }),
  }

  const fake = {
    openedReceived: [] as string[],
    revealedReceived: [] as string[],
    revealedShares: [] as string[],
    nextPick: samplePicks[0] as PickedFile | PickedFile[] | null,
    nextFolder: { name: 'Holiday photos', sizeBytes: 734_003_200, itemCount: 200, pictures: 200 } as PickedFolder | null,
    folders: {} as Record<string, PickedFolder>,
    nextDownloadFile: { name: 'Free documentary.mkv', sizeBytes: 2_147_483_648 } as PickedFile | 'invalid' | null,
    failNextWith: null as ErrorCode | null,
    copiedInvite: null as string | null,
    copiedReport: null as string | null,
    savedDownloadFiles: [] as string[],
    clipboardText: null as string | null,
    openedFolder: 0,
    openedConnectionSettings: 0,
    quits: 0,

    openInvite(text: string) {
      openedInvite = text
      emit({ event: 'invites.changed', payload: true })
    },

    async invoke(command: string, args?: unknown) {
      let result: CommandResult<unknown>
      if (fake.failNextWith) {
        result = fail(fake.failNextWith)
        fake.failNextWith = null
      } else result = (await handlers[command]?.(args)) ?? fail('unexpected')
      // As the app: every problem a command answers with is written down for the support report.
      if (!result.ok) problems = [...problems, { at: new Date(), code: result.error.code }].slice(-20)
      return result
    },

    handOverDrop(token: string, files: File[]) {
      dropped.set(
        token,
        files.map((file) => ({ name: file.name, sizeBytes: file.size })),
      )
      return true
    },

    subscribe(listener: (event: EventEnvelope) => void) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },

    quitFromIcon() {
      emit({ event: 'quit.changed', payload: { quitting: false, moving: moving() } })
    },

    clickNotification(target: DetailsTargetDto) {
      emit({ event: 'details.changed', payload: target })
    },

    sendTo(shareId: string) {
      const update = (change: Partial<ShareDto>) => {
        shares = shares.map((s) => (s.id === shareId ? { ...s, ...change } : s))
        emitShares()
      }
      const step = (percent: number) => {
        const share = shares.find((s) => s.id === shareId)
        if (!share) return
        if (percent >= 100) {
          update({ ...notSending, paused: share.paused, activity: 'sent', finishedCount: share.finishedCount + 1 })
          return
        }
        const left = (share.sizeBytes * (100 - percent)) / 100
        update({
          activity: 'sending',
          receiverCount: 1,
          percent,
          secondsLeft: percent === 0 ? null : Math.ceil(left / fakeSendingPerShare),
        })
        setTimeout(() => step(percent + 25), Math.max(stepMs, 1))
      }
      step(0)
    },

    setShares(next: ShareDto[]) {
      shares = [...next]
      emitShares()
    },

    setTransfers(next: TransferDto[]) {
      transfers = [...next]
      emitTransfers()
    },

    setConnection(health: ConnectionHealthDto | null) {
      if (health === connection) return
      connection = health
      emitConnection()
    },
  }

  return fake as unknown as FakeBridge
}

/** An invite from an earlier build, which the fake accepts as the app does. */
export const sampleFakeInvite = `${oldInvitePrefix}demo`

/** An invite as the app hands it out now, for trying the receive flow in a browser. */
export const sampleFakeLinkInvite = `${invitePrefix}demo&dn=Birthday%20party.mp4&xl=250000000`

/** A download link the fake accepts. */
export const sampleFakeDownloadLink = `${downloadLinkPrefix}Open%20film.mkv`
