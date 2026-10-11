import type { Bridge } from './bridge.ts'
import type {
  CommandResult,
  ErrorCode,
  EventEnvelope,
  CopyInviteArgs,
  IdArgs,
  InvitePreviewDto,
  ReceiveArgs,
  ShareDto,
  TransferDto,
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
  /** Change state from outside, as the app does when something happens on its own. */
  setShares(shares: ShareDto[]): void
  setTransfers(transfers: TransferDto[]): void
}

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
  const emitShares = () => emit({ event: 'shares.changed', payload: [...shares] })
  const emitTransfers = () => emit({ event: 'transfers.changed', payload: [...transfers] })
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
        if (current.state === 'paused') return
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

  const startTransfer = (id: string, name: string, total: number, known: Partial<TransferDto> = {}) => {
    if (transfers.some((t) => t.id === id && t.state !== 'done')) return fail('transfer.duplicate')
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
      fake.copiedInvite = message ? (message.includes('{link}') ? message.replace('{link}', link) : `${message}\n${link}`) : link
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
      return startTransfer(found.key, name ?? 'File', sizeBytes ?? 250_000_000, isInvite ? {} : { name })
    },

    'clipboard.findInvite': () => {
      const found = invitePattern.exec(fake.clipboardText ?? '')
      return ok(found ? found[0] : null)
    },

    'transfers.openFile': () => {
      const file = fake.nextDownloadFile
      if (file === null) return ok(null)
      if (file === 'invalid') return fail('download.invalidFile')
      return startTransfer(`file-${file.name}`, file.name, file.sizeBytes, { name: file.name, sizeBytes: file.sizeBytes })
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

    'received.openFolder': () => {
      fake.openedFolder++
      return ok(null)
    },
  }

  const fake = {
    nextPick: samplePicks[0] as PickedFile | null,
    nextDownloadFile: { name: 'Free documentary.mkv', sizeBytes: 2_147_483_648 } as PickedFile | 'invalid' | null,
    failNextWith: null as ErrorCode | null,
    copiedInvite: null as string | null,
    clipboardText: null as string | null,
    openedFolder: 0,

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
