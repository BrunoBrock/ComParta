import type { Bridge } from './bridge.ts'
import type {
  ActivityDto,
  CommandResult,
  ConnectionHealthDto,
  ErrorCode,
  EventEnvelope,
  CopyInviteArgs,
  IdArgs,
  InvitePreviewDto,
  ReceiveArgs,
  SetActivityArgs,
  SetSettingsArgs,
  SettingsDto,
  ShareDetailsDto,
  ShareDto,
  TransferDetailsDto,
  TransferDto,
  TransferSourceDto,
} from './contract.generated.ts'

export interface PickedFile {
  name: string
  sizeBytes: number
}

export interface FakeBridgeOptions {
  shares?: ShareDto[]
  transfers?: TransferDto[]
  /** Milliseconds a new share stays "preparing" and between steps of a transfer. 0 means never advance on its own. */
  stepMs?: number
  /** Room left for received files; null when it cannot be told. */
  freeBytes?: number | null
  /** The operating system's language; English when left out. */
  systemLanguage?: string
  /** A language the person chose earlier. */
  language?: string | null
  /** How well others can reach this computer; good when left out. */
  connection?: ConnectionHealthDto
  /** Sending or receiving already stopped from the speed bar, as the app remembers it. */
  stopped?: Partial<SetActivityArgs>
  /** How files were shown and ordered last time, as the app stored it. */
  view?: string | null
  sort?: string | null
  /** How many guided sends the person has been through; 3 means the steps no longer show. */
  sendStepsSeen?: number
}

export interface FakeBridge extends Bridge {
  /** What the next "file dialog" returns; null means the person cancelled. */
  nextPick: PickedFile | null
  /** Make the next command fail with this code. */
  failNextWith: ErrorCode | null
  /** What the next "open a download file" dialog returns; null means cancelled, 'invalid' a wrong file. */
  nextDownloadFile: PickedFile | 'invalid' | null
  /** Text of the last invite message "copied". */
  copiedInvite: string | null
  /** What the system clipboard holds, for the "found an invite you copied" path. */
  clipboardText: string | null
  openedFolder: number
  /** Ids of received files "opened" and "shown in their folder", in order. */
  openedReceived: string[]
  revealedReceived: string[]
  /** How many times the firewall settings were opened. */
  openedConnectionSettings: number
  /** Change the connection health, as the app does when the network changes. */
  setConnection(health: ConnectionHealthDto): void
  /** An invite link clicked outside the app, as the app reports it. */
  openInvite(text: string): void
  /** Change state from outside, as the app does when something happens on its own. */
  setShares(shares: ShareDto[]): void
  setTransfers(transfers: TransferDto[]): void
}

/** Roughly what one ready share sends while someone is getting it, in the moving fake. */
export const fakeSendingPerShare = 180_000
const addedLongAgo = '2026-10-10T14:05:00.000Z'

// The fake's invites look like the app's ComParta links (AD-16) but carry the share id in the clear.
const invitePrefix = 'comparta://receive/v1/'
const invitePattern = /comparta:\/\/receive\/v1\/([A-Za-z0-9_-]+)/i
const downloadLinkPrefix = 'https://example.org/download/'
const demoInvite = { senderName: 'Paulo', name: 'Birthday party.mp4', sizeBytes: 250_000_000 }
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
  let nextId = 1
  let nextSample = 0
  const stepMs = options.stepMs ?? 0
  const listeners = new Set<(event: EventEnvelope) => void>()

  const emit = (event: EventEnvelope) => listeners.forEach((listener) => listener(event))
  let stopped: SetActivityArgs = { sendingStopped: false, receivingStopped: false, ...options.stopped }
  // What the app knows about each item beyond the list: when it was added and where it came from.
  const addedAt = new Map<string, string>()
  const sources = new Map<string, TransferSourceDto>()

  const activity = (): ActivityDto => ({
    ...stopped,
    receivingBytesPerSecond: stopped.receivingStopped
      ? 0
      : transfers.filter((t) => t.state === 'receiving').reduce((sum, t) => sum + t.bytesPerSecond, 0),
    sendingBytesPerSecond:
      stopped.sendingStopped || stepMs === 0
        ? 0
        : shares.filter((s) => s.status === 'ready').length * fakeSendingPerShare,
  })
  const emitActivity = () => emit({ event: 'activity.changed', payload: activity() })
  const emitShares = () => {
    emit({ event: 'shares.changed', payload: [...shares] })
    emitActivity()
  }
  const emitTransfers = () => {
    emit({ event: 'transfers.changed', payload: [...transfers] })
    emitActivity()
  }
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
          updateTransfer(id, { receivedBytes: total, state: 'done', bytesPerSecond: 0, secondsLeft: null })
        } else {
          updateTransfer(id, {
            receivedBytes: Math.round(received),
            state: 'receiving',
            bytesPerSecond,
            secondsLeft: Math.ceil((total - received) / bytesPerSecond),
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
    if (paused) updateTransfer(id, { state: 'paused', bytesPerSecond: 0, secondsLeft: null })
    else if (transfer.state === 'paused') updateTransfer(id, { state: transfer.sizeBytes ? 'receiving' : 'connecting' })
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

  let connection: ConnectionHealthDto = options.connection ?? 'good'
  let openedInvite: string | null = null
  let settings: SettingsDto = {
    language: options.language ?? null,
    systemLanguage: options.systemLanguage ?? 'en',
    view: options.view ?? null,
    sort: options.sort ?? null,
    sendStepsSeen: options.sendStepsSeen ?? 0,
  }
  const freeBytes = options.freeBytes === undefined ? 120 * 1024 ** 3 : options.freeBytes

  /** Reads text the way the app's invite codec and engine do, or null when it is neither. */
  const read = (text: string): { key: string; preview: InvitePreviewDto } | null => {
    const found = invitePattern.exec(text)
    if (found) {
      const share = shares.find((s) => s.id === found[1])
      const hints = share ? { senderName: null, name: share.name, sizeBytes: share.sizeBytes } : demoInvite
      return { key: `from-${found[1]}`, preview: { ...hints, itemCount: 1, isInvite: true, freeBytes } }
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

  const handlers: Record<string, (args: unknown) => CommandResult<unknown>> = {
    'shares.list': () => ok([...shares]),

    'shares.add': () => {
      const pick = fake.nextPick
      if (pick === null) return ok(null)
      fake.nextPick = samplePicks[++nextSample % samplePicks.length]
      if (shares.some((s) => s.name === pick.name)) return fail('share.duplicate')
      if (pick.sizeBytes === 0) return fail('file.unreadable')

      const share: ShareDto = { id: newId(), ...pick, status: stepMs === 0 ? 'ready' : 'preparing' }
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
    },

    'shares.remove': (args) => {
      const { id } = args as IdArgs
      if (!shares.some((s) => s.id === id)) return fail('share.notFound')
      shares = shares.filter((s) => s.id !== id)
      emitShares()
      return ok(null)
    },

    'shares.copyInvite': (args) => {
      const { id, message } = args as CopyInviteArgs
      const share = shares.find((s) => s.id === id)
      if (!share) return fail('share.notFound')
      if (share.status !== 'ready') return fail('share.notReady')
      const link = invitePrefix + share.id
      fake.copiedInvite = message
        ? message.includes('{link}')
          ? message.replace('{link}', link)
          : `${message}\n${link}`
        : link
      return ok(null)
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
      const found = invitePattern.exec(fake.clipboardText ?? '')
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
      settings = { ...settings, language, view, sort, sendStepsSeen }
      emit({ event: 'settings.changed', payload: { ...settings } })
      return ok(null)
    },

    'connection.get': () => ok({ health: connection }),

    'activity.get': () => ok(activity()),

    'activity.set': (args) => {
      const { sendingStopped, receivingStopped } = args as SetActivityArgs
      stopped = { sendingStopped, receivingStopped }
      emitActivity()
      return ok(null)
    },

    'shares.details': (args) => {
      const share = shares.find((s) => s.id === (args as IdArgs).id)
      if (!share) return fail('share.notFound')
      const sending = share.status === 'ready' && !stopped.sendingStopped && stepMs > 0
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
    nextPick: samplePicks[0] as PickedFile | null,
    nextDownloadFile: { name: 'Free documentary.mkv', sizeBytes: 2_147_483_648 } as PickedFile | 'invalid' | null,
    failNextWith: null as ErrorCode | null,
    copiedInvite: null as string | null,
    clipboardText: null as string | null,
    openedFolder: 0,
    openedConnectionSettings: 0,

    setConnection(health: ConnectionHealthDto) {
      connection = health
      emit({ event: 'connection.changed', payload: { health } })
    },

    openInvite(text: string) {
      openedInvite = text
      emit({ event: 'invites.changed', payload: true })
    },

    async invoke(command: string, args?: unknown) {
      if (fake.failNextWith) {
        const code = fake.failNextWith
        fake.failNextWith = null
        return fail(code)
      }
      return handlers[command]?.(args) ?? fail('unexpected')
    },

    subscribe(listener: (event: EventEnvelope) => void) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },

    setShares(next: ShareDto[]) {
      shares = [...next]
      emitShares()
    },

    setTransfers(next: TransferDto[]) {
      transfers = [...next]
      emitTransfers()
    },
  }

  return fake as unknown as FakeBridge
}

/** An invite the fake accepts, for trying the receive flow in a browser. */
export const sampleFakeInvite = `${invitePrefix}demo`

/** A download link the fake accepts. */
export const sampleFakeDownloadLink = `${downloadLinkPrefix}Open%20film.mkv`
