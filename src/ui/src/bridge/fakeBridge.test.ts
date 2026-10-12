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

    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health: 'good' } })
  })

  it.each(healths)('starts with the %s health it was given', async (health) => {
    const bridge = createFakeBridge({ connection: health })

    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health } })
  })

  it('starts with no health when null is given', async () => {
    const bridge = createFakeBridge({ connection: null })

    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health: null } })
  })

  it.each(healths)('becomes %s when the app learns it, and says so', async (health) => {
    const bridge = createFakeBridge({ connection: null })
    const { events } = listen(bridge)

    bridge.setConnection(health)

    expect(events).toEqual([{ event: 'connection.changed', payload: { health } }])
    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health } })
  })

  it('goes back to no health when nothing can be measured any more, and says so', async () => {
    const bridge = createFakeBridge({ connection: 'good' })
    const { events } = listen(bridge)

    bridge.setConnection(null)

    expect(events).toEqual([{ event: 'connection.changed', payload: { health: null } }])
    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health: null } })
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
      { health: 'good' },
      { health: 'limited' },
      { health: null },
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
    expect(await bridge.invoke('connection.get')).toEqual({ ok: true, value: { health: 'good' } })
  })
})
