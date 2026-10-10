import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import type { ShareDto, TransferDto } from './bridge/contract.generated.ts'
import { createFakeBridge, sampleFakeInvite, type FakeBridgeOptions } from './bridge/fakeBridge.ts'
import { en } from './i18n/en.ts'

const video: ShareDto = { id: 's1', name: 'Birthday party.mp4', sizeBytes: 1_288_490_188, status: 'ready' }
const album: ShareDto = { id: 's2', name: 'Holiday photos.zip', sizeBytes: 2048, status: 'unavailable' }

function setup(options: FakeBridgeOptions = {}) {
  const bridge = createFakeBridge(options)
  render(<App bridge={bridge} />)
  return { bridge, user: userEvent.setup() }
}

const sharesSection = () => screen.getByRole('region', { name: en['shares.heading'] })
const transfersSection = () => screen.getByRole('region', { name: en['transfers.heading'] })

describe('the list of my shares', () => {
  it('shows the empty-state message when nothing is shared', async () => {
    setup()

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
  })

  it('shows each share with its name and size', async () => {
    setup({ shares: [video] })

    const item = (await within(sharesSection()).findByText('Birthday party.mp4')).closest('li')!
    expect(within(item).getByText('1.20 GB')).toBeInTheDocument()
    expect(within(item).getByText(en['shares.status.ready'])).toBeInTheDocument()
    expect(screen.queryByText(en['shares.empty'])).not.toBeInTheDocument()
  })

  it('marks a share whose file went missing', async () => {
    setup({ shares: [video, album] })

    const item = (await within(sharesSection()).findByText('Holiday photos.zip')).closest('li')!
    expect(within(item).getByText(en['shares.status.unavailable'])).toBeInTheDocument()
    expect(within(item).queryByRole('button', { name: /copy invite/i })).not.toBeInTheDocument()
  })

  it('updates from a change event without reloading', async () => {
    const { bridge } = setup()
    await screen.findByText(en['shares.empty'])

    act(() => bridge.setShares([video]))

    expect(await screen.findByText('Birthday party.mp4')).toBeInTheDocument()
  })
})

describe('adding a file', () => {
  it('shows the new share', async () => {
    const { bridge, user } = setup()
    bridge.nextPick = { name: 'Recipe.pdf', sizeBytes: 482_113 }

    await user.click(await screen.findByRole('button', { name: en['shares.add'] }))

    expect(await screen.findByText('Recipe.pdf')).toBeInTheDocument()
    expect(screen.getByText('471 KB')).toBeInTheDocument()
  })

  it('shows a plain message for a duplicate', async () => {
    const { bridge, user } = setup({ shares: [video] })
    bridge.nextPick = { name: video.name, sizeBytes: video.sizeBytes }

    await user.click(await screen.findByRole('button', { name: en['shares.add'] }))

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.share.duplicate'])
    expect(within(sharesSection()).getAllByRole('listitem')).toHaveLength(1)
  })

  it('shows a plain message for a file that cannot be read', async () => {
    const { bridge, user } = setup()
    bridge.nextPick = { name: 'Empty.txt', sizeBytes: 0 }

    await user.click(await screen.findByRole('button', { name: en['shares.add'] }))

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.file.unreadable'])
  })

  it('changes nothing and says nothing when the person cancels', async () => {
    const { bridge, user } = setup()
    bridge.nextPick = null

    await user.click(await screen.findByRole('button', { name: en['shares.add'] }))

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('removing a share', () => {
  it('asks first, says the original is kept, and removes on confirm', async () => {
    const { user } = setup({ shares: [video] })

    await user.click(await screen.findByRole('button', { name: 'Stop sharing Birthday party.mp4' }))
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveTextContent('The file stays on your computer')
    await user.click(within(dialog).getByRole('button', { name: en['remove.confirm'] }))

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('keeps the share when the person declines', async () => {
    const { user } = setup({ shares: [video] })

    await user.click(await screen.findByRole('button', { name: 'Stop sharing Birthday party.mp4' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: en['remove.cancel'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByText('Birthday party.mp4')).toBeInTheDocument()
  })

  it('can remove a share whose file went missing', async () => {
    const { user } = setup({ shares: [album] })

    await user.click(await screen.findByRole('button', { name: 'Stop sharing Holiday photos.zip' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: en['remove.confirm'] }))

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
  })
})

describe('sending to another person', () => {
  it('copies the invite and explains what to do with it', async () => {
    const { bridge, user } = setup({ shares: [video] })

    await user.click(await screen.findByRole('button', { name: 'Copy invite for Birthday party.mp4' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Paste it into an email or chat message')
    expect(bridge.copiedInvite).not.toBeNull()
  })
})

describe('receiving from another person', () => {
  const receiving: TransferDto = {
    id: 't1',
    name: 'Birthday party.mp4',
    sizeBytes: 1000 * 1024 * 1024,
    receivedBytes: 250 * 1024 * 1024,
    state: 'receiving',
  }

  it('shows the empty-state message when nothing is being received', async () => {
    setup()

    expect(await screen.findByText(en['transfers.empty'])).toBeInTheDocument()
  })

  it('starts looking for the other computer when an invite is pasted', async () => {
    const { user } = setup()

    await user.type(await screen.findByLabelText(en['transfers.inviteLabel']), sampleFakeInvite)
    await user.click(screen.getByRole('button', { name: en['transfers.receive'] }))

    expect(await within(transfersSection()).findByText(en['transfers.state.connecting'])).toBeInTheDocument()
    expect(screen.getByLabelText(en['transfers.inviteLabel'])).toHaveValue('')
  })

  it('explains when the text is not an invite and keeps what was typed', async () => {
    const { user } = setup()

    await user.type(await screen.findByLabelText(en['transfers.inviteLabel']), 'hello grandma')
    await user.click(screen.getByRole('button', { name: en['transfers.receive'] }))

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.invite.invalid'])
    expect(screen.getByLabelText(en['transfers.inviteLabel'])).toHaveValue('hello grandma')
  })

  it('shows progress while receiving and where the file is when done', async () => {
    const { bridge } = setup({ transfers: [receiving] })

    expect(await screen.findByText('Receiving: 25% (250.0 MB of 1,000.0 MB)')).toBeInTheDocument()
    expect(within(transfersSection()).getByRole('progressbar')).toHaveAttribute('value', String(receiving.receivedBytes))

    act(() => bridge.setTransfers([{ ...receiving, receivedBytes: receiving.sizeBytes!, state: 'done' }]))

    expect(await screen.findByText(en['transfers.state.done'])).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Clear Birthday party.mp4 from list' })).toBeInTheDocument()
  })

  it('cancels a transfer', async () => {
    const { user } = setup({ transfers: [receiving] })

    await user.click(await screen.findByRole('button', { name: 'Cancel receiving Birthday party.mp4' }))

    expect(await screen.findByText(en['transfers.empty'])).toBeInTheDocument()
  })

  it('opens the folder with received files', async () => {
    const { bridge, user } = setup()

    await user.click(await screen.findByRole('button', { name: en['transfers.openFolder'] }))

    expect(bridge.openedFolder).toBe(1)
  })
})
