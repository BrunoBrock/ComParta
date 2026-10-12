import type { Bridge } from './bridge.ts'
import type {
  CommandResult,
  ConnectionDto,
  ConnectionHealthDto,
  ErrorCode,
  EventEnvelope,
  IdArgs,
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
  /** Whether another device can reach this computer; null (the default) while nothing is shared or received. */
  connection?: ConnectionHealthDto | null
  /** Milliseconds a new share stays "preparing" and between steps of a transfer. 0 means never advance on its own. */
  stepMs?: number
}

export interface FakeBridge extends Bridge {
  /** What the next "file dialog" returns; null means the person cancelled. */
  nextPick: PickedFile | null
  /** Make the next command fail with this code. */
  failNextWith: ErrorCode | null
  /** What the next "open a download file" dialog returns; null means cancelled, 'invalid' a wrong file. */
  nextDownloadFile: PickedFile | 'invalid' | null
  /** Text of the last invite "copied". */
  copiedInvite: string | null
  openedFolder: number
  /** Change state from outside, as the app does when something happens on its own. */
  setShares(shares: ShareDto[]): void
  setTransfers(transfers: TransferDto[]): void
  /**
   * Change the connection health, as the app does when it learns more; null means it cannot be told.
   * Setting the value it already has raises no event, as in the app.
   */
  setConnection(health: ConnectionHealthDto | null): void
}

const invitePrefix = 'comparta-invite:'
const downloadLinkPrefix = 'https://example.org/download/'
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
  let connection: ConnectionHealthDto | null = options.connection ?? null
  let nextId = 1
  let nextSample = 0
  const stepMs = options.stepMs ?? 0
  const listeners = new Set<(event: EventEnvelope) => void>()

  const emit = (event: EventEnvelope) => listeners.forEach((listener) => listener(event))
  const emitShares = () => emit({ event: 'shares.changed', payload: [...shares] })
  const emitTransfers = () => emit({ event: 'transfers.changed', payload: [...transfers] })
  const connectionDto = (): ConnectionDto => ({ health: connection })
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
      const share = shares.find((s) => s.id === (args as IdArgs).id)
      if (!share) return fail('share.notFound')
      if (share.status !== 'ready') return fail('share.notReady')
      fake.copiedInvite = invitePrefix + share.id
      return ok(null)
    },

    'transfers.list': () => ok([...transfers]),

    'transfers.receive': (args) => {
      const invite = (args as ReceiveArgs).invite.trim()
      if (invite.startsWith(invitePrefix))
        return startTransfer(`from-${invite.slice(invitePrefix.length)}`, 'Birthday party.mp4', 250_000_000)
      if (invite.startsWith(downloadLinkPrefix)) {
        const name = decodeURIComponent(invite.slice(downloadLinkPrefix.length)) || 'Download'
        return startTransfer(`link-${name}`, name, 1_800_000_000, { name })
      }
      return fail('invite.invalid')
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

    'connection.get': () => ok(connectionDto()),
  }

  const fake = {
    nextPick: samplePicks[0] as PickedFile | null,
    nextDownloadFile: { name: 'Free documentary.mkv', sizeBytes: 2_147_483_648 } as PickedFile | 'invalid' | null,
    failNextWith: null as ErrorCode | null,
    copiedInvite: null as string | null,
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

    setConnection(health: ConnectionHealthDto | null) {
      if (health === connection) return
      connection = health
      emitConnection()
    },
  }

  return fake as unknown as FakeBridge
}

/** An invite the fake accepts, for trying the receive flow in a browser. */
export const sampleFakeInvite = `${invitePrefix}demo`

/** A download link the fake accepts. */
export const sampleFakeDownloadLink = `${downloadLinkPrefix}Open%20film.mkv`
