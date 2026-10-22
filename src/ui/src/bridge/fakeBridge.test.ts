import { describe, expect, it } from 'vitest'
import type { ConnectionHealthDto, EventEnvelope } from './contract.generated.ts'
import { createFakeBridge } from './fakeBridge.ts'

const healths: ConnectionHealthDto[] = ['good', 'limited', 'blocked']

function listen(bridge: ReturnType<typeof createFakeBridge>) {
  const events: EventEnvelope[] = []
  const stop = bridge.subscribe((event) => events.push(event))
  return { events, stop }
}

describe('the fake bridge: connection health', () => {
  it('answers connection.get with a good connection when none is given, so the command is not silently unexpected', async () => {
    const bridge = createFakeBridge()

    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health: 'good', throughHelper: false } })
  })

  it.each(healths)('starts with the %s health it was given', async (health) => {
    const bridge = createFakeBridge({ connection: health })

    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health, throughHelper: false } })
  })

  it('starts with no health when null is given', async () => {
    const bridge = createFakeBridge({ connection: null })

    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health: null, throughHelper: false } })
  })

  it.each(healths)('becomes %s when the app learns it, and says so', async (health) => {
    const bridge = createFakeBridge({ connection: null })
    const { events } = listen(bridge)

    bridge.setConnection(health)

    expect(events).toEqual([{ event: 'connection.changed', payload: { health, throughHelper: false } }])
    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health, throughHelper: false } })
  })

  it('goes back to no health when nothing can be measured any more, and says so', async () => {
    const bridge = createFakeBridge({ connection: 'good' })
    const { events } = listen(bridge)

    bridge.setConnection(null)

    expect(events).toEqual([{ event: 'connection.changed', payload: { health: null, throughHelper: false } }])
    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health: null, throughHelper: false } })
  })

  it('raises one event per real change and none for a repeat', () => {
    const bridge = createFakeBridge({ connection: 'blocked' })
    const { events } = listen(bridge)

    bridge.setConnection('blocked')
    bridge.setConnection('good')
    bridge.setConnection('good')
    bridge.setConnection('limited')
    bridge.setConnection(null)
    bridge.setConnection(null)

    expect(events.map((event) => event.payload)).toEqual([
      { health: 'good', throughHelper: false },
      { health: 'limited', throughHelper: false },
      { health: null, throughHelper: false },
    ])
  })

  it('stops raising events once the listener has stopped', () => {
    const bridge = createFakeBridge({ connection: null })
    const { events, stop } = listen(bridge)

    stop()
    bridge.setConnection('good')

    expect(events).toEqual([])
  })

  it('still fails the next command with the code it was told to', async () => {
    const bridge = createFakeBridge()
    bridge.failNextWith = 'unexpected'

    expect(await bridge.invoke('connection.get')).toEqual({ ok: false, error: { code: 'unexpected' } })
    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health: 'good', throughHelper: false } })
  })
})

describe('the fake bridge: opening a file inside the app', () => {
  const share = (name: string, status: 'ready' | 'unavailable' = 'ready') => ({
    id: name,
    name,
    sizeBytes: 10,
    status,
    activity: 'idle' as const,
    receiverCount: 0,
    percent: 0,
    finishedCount: 0,
    secondsLeft: null,
    paused: false,
    itemCount: 1,
    kind: 'file' as const,
  })
  const transfer = (id: string, name: string, state: 'done' | 'receiving') => ({
    id,
    name,
    sizeBytes: 10,
    receivedBytes: state === 'done' ? 10 : 5,
    state,
    bytesPerSecond: 0,
    secondsLeft: null,
    neededBytes: null,
    risky: false, folder: false,
  })

  it('gives an address for a picture, song or video, as the app does, and never a path', async () => {
    const bridge = createFakeBridge({
      shares: [share('beach.JPG'), share('song.mp3')],
      transfers: [transfer('t1', 'party.webm', 'done')],
    })

    const picture = await bridge.invoke('media.open', { kind: 'share', id: 'beach.JPG' })
    const song = await bridge.invoke('media.open', { kind: 'share', id: 'song.mp3' })
    const video = await bridge.invoke('media.open', { kind: 'transfer', id: 't1' })

    expect(picture).toMatchObject({ ok: true, value: { kind: 'picture', name: 'beach.JPG' } })
    expect(song).toMatchObject({ ok: true, value: { kind: 'song' } })
    expect(video).toMatchObject({ ok: true, value: { kind: 'video', name: 'party.webm' } })
    const urls = [picture, song, video].map((r) => (r.ok ? r.value.url : ''))
    for (const url of urls) expect(url).toMatch(/^https:\/\/media\.comparta\.invalid\/[0-9a-f]{32}$/)
    expect(new Set(urls).size).toBe(3)
  })

  it('answers that a program, an unknown kind or a missing file cannot be played', async () => {
    const bridge = createFakeBridge({
      shares: [share('setup.exe'), share('film.mkv'), share('gone.mp4', 'unavailable')],
      transfers: [transfer('t1', 'later.mp3', 'receiving')],
    })

    for (const id of ['setup.exe', 'film.mkv', 'gone.mp4'])
      expect(await bridge.invoke('media.open', { kind: 'share', id })).toEqual({
        ok: false,
        error: { code: 'media.notPlayable' },
      })
    expect(await bridge.invoke('media.open', { kind: 'transfer', id: 't1' })).toEqual({
      ok: false,
      error: { code: 'received.notFound' },
    })
    expect(await bridge.invoke('media.open', { kind: 'share', id: 'nope' })).toEqual({
      ok: false,
      error: { code: 'share.notFound' },
    })
    expect(await bridge.invoke('media.open', { kind: 'transfer', id: 'nope' })).toEqual({
      ok: false,
      error: { code: 'transfer.notFound' },
    })
  })
})
