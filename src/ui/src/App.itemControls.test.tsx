import { render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import App from './App.tsx'
import type { Bridge, TransferDto } from './bridge/index.ts'
import { createFakeBridge } from './bridge/fakeBridge.ts'
import { en } from './i18n/en.ts'
import { goToReceived, setup, sharesSection, transfersSection, video } from './test/appHarness.tsx'
import userEvent from '@testing-library/user-event'

const moving: TransferDto = {
  id: 'm1',
  name: 'Open film.mkv',
  sizeBytes: 1000 * 1024 * 1024,
  receivedBytes: 500 * 1024 * 1024,
  state: 'receiving',
  bytesPerSecond: 2 * 1024 * 1024,
  secondsLeft: 250,
  neededBytes: null,
}

const card = (name: string) => screen.getByText(name, { selector: '.visually-hidden, .name-start' }).closest('li')!
const speedBar = () => screen.getByRole('region', { name: en['speed.label'] })

describe("each file's own speed and pause or play button", () => {
  it('shows how fast each file moves on its card', async () => {
    const { user } = setup({ transfers: [moving] })
    await goToReceived(user)

    const item = await within(transfersSection()).findByRole('listitem')
    expect(within(item).getByText('2.0 MB/s')).toBeInTheDocument()
    expect(item).toHaveTextContent('Receiving 2.0 MB/s')
  })

  it('pauses a shared file from its card, shows it paused with play, and play sends it again', async () => {
    const { bridge, user } = setup({ shares: [video] })

    await user.click(await within(sharesSection()).findByRole('button', { name: `Pause ${video.name}` }))

    expect(await within(sharesSection()).findByText(en['shares.status.paused'])).toBeInTheDocument()
    expect(within(card(video.name)).getByText(en['shares.chip.paused'])).toBeInTheDocument()
    const listed = await bridge.invoke('shares.list')
    expect(listed.ok && listed.value[0].paused).toBe(true)

    await user.click(within(sharesSection()).getByRole('button', { name: `Continue ${video.name}` }))

    expect(await within(sharesSection()).findByRole('button', { name: `Pause ${video.name}` })).toBeInTheDocument()
    expect(within(sharesSection()).queryByText(en['shares.status.paused'])).not.toBeInTheDocument()
  })

  it('pauses and continues a download from its card', async () => {
    const { user } = setup({ transfers: [moving] })
    await goToReceived(user)

    await user.click(await within(transfersSection()).findByRole('button', { name: 'Pause Open film.mkv' }))

    expect(await within(transfersSection()).findByText(en['transfers.chip.paused'])).toBeInTheDocument()
    await user.click(within(transfersSection()).getByRole('button', { name: 'Continue Open film.mkv' }))
    expect(await within(transfersSection()).findByRole('button', { name: 'Pause Open film.mkv' })).toBeInTheDocument()
  })

  it('offers the same pause and play in the details window', async () => {
    const { user } = setup({ shares: [video] })
    await user.click(await screen.findByRole('button', { name: en['details.openFor'].replace('{name}', video.name) }))
    const details = screen.getByRole('dialog', { name: en['details.title'].replace('{name}', video.name) })

    await user.click(within(details).getByRole('button', { name: `Pause ${video.name}` }))

    expect(await within(details).findByRole('button', { name: `Continue ${video.name}` })).toBeInTheDocument()
    expect(within(sharesSection()).getByText(en['shares.status.paused'])).toBeInTheDocument()
    await user.click(within(details).getByRole('button', { name: `Continue ${video.name}` }))
    expect(await within(details).findByRole('button', { name: `Pause ${video.name}` })).toBeInTheDocument()
  })

  it('offers play on a file stopped with everything else, which continues receiving', async () => {
    const { user } = setup({ transfers: [moving], stopped: { receivingStopped: true } })
    await goToReceived(user)

    await user.click(await within(transfersSection()).findByRole('button', { name: en['stopped.continueReceiving'] }))

    expect(within(speedBar()).queryByText(en['speed.receivingStopped'])).not.toBeInTheDocument()
    expect(await within(transfersSection()).findByRole('button', { name: 'Pause Open film.mkv' })).toBeInTheDocument()
  })
})

describe('the Stop button', () => {
  it('changes at once, even while the app is still stopping files', async () => {
    const fake = createFakeBridge({ sendStepsSeen: 3, transfers: [moving] })
    let release = () => {}
    // An app that takes its time to answer, as with many files or a slow router.
    const invoke = fake.invoke as (command: string, args?: unknown) => Promise<unknown>
    const slow = {
      ...fake,
      invoke: (command: string, args?: unknown) =>
        command === 'activity.set'
          ? new Promise<unknown>((resolve) => {
              release = () => resolve(invoke(command, args))
            })
          : invoke(command, args),
    } as Bridge
    render(<App bridge={slow} />)
    const user = userEvent.setup()
    const stop = await screen.findByRole('button', { name: en['stop.both'] })
    await vi.waitFor(() => expect(stop).toBeEnabled())

    await user.click(stop)

    expect(within(speedBar()).getByText(en['speed.receivingStopped'])).toBeInTheDocument()
    expect(screen.getByRole('button', { name: en['stop.continue'] })).toBeInTheDocument()
    release()
    expect(await screen.findByRole('button', { name: en['stop.continue'] })).toBeInTheDocument()
  })
})
