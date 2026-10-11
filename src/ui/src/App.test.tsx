import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import App from './App.tsx'
import type { ShareDto, TransferDto } from './bridge/contract.generated.ts'
import {
  createFakeBridge,
  sampleFakeDownloadLink,
  sampleFakeInvite,
  type FakeBridgeOptions,
} from './bridge/fakeBridge.ts'
import { en } from './i18n/en.ts'

const video: ShareDto = { id: 's1', name: 'Birthday party.mp4', sizeBytes: 1_288_490_188, status: 'ready' }
const album: ShareDto = { id: 's2', name: 'Holiday photos.zip', sizeBytes: 2048, status: 'unavailable' }

beforeEach(() => localStorage.clear())

function setup(options: FakeBridgeOptions = {}) {
  const bridge = createFakeBridge(options)
  render(<App bridge={bridge} />)
  return { bridge, user: userEvent.setup() }
}

type User = ReturnType<typeof userEvent.setup>

const sharesSection = () => screen.getByRole('region', { name: en['shares.heading'] })
const transfersSection = () => screen.getByRole('region', { name: en['transfers.heading'] })
const navButton = (label: string) =>
  within(screen.getByRole('navigation', { name: en['nav.label'] })).getByRole('button', { name: new RegExp(`^${label}`) })
const goToReceived = (user: User) => user.click(navButton(en['nav.received']))

/** Types the text into the receive box and continues; stops at the confirmation or the problem. */
async function enterInvite(user: User, invite: string) {
  await user.click(await screen.findByRole('button', { name: en['transfers.open'] }))
  const dialog = screen.getByRole('dialog', { name: en['receive.title'] })
  await user.type(within(dialog).getByLabelText(en['transfers.inviteLabel']), invite)
  await user.click(within(dialog).getByRole('button', { name: en['receive.continue'] }))
}

async function pasteInvite(user: User, invite: string) {
  await enterInvite(user, invite)
  const confirm = await screen.findByRole('dialog', { name: en['confirm.title'] })
  await user.click(within(confirm).getByRole('button', { name: en['transfers.receive'] }))
}

describe('the list of my shares', () => {
  it('shows the empty-state message when nothing is shared', async () => {
    setup()

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
  })

  it('shows each share with its name and size', async () => {
    setup({ shares: [video] })

    const item = (await within(sharesSection()).findByText('Birthday party.mp4')).closest('li')!
    expect(within(item).getByText('1.20 GB')).toBeInTheDocument()
    expect(within(item).getByText(en['shares.chip.ready'])).toBeInTheDocument()
    expect(screen.queryByText(en['shares.empty'])).not.toBeInTheDocument()
  })

  it('marks a share whose file went missing', async () => {
    setup({ shares: [video, album] })

    const item = (await within(sharesSection()).findByText('Holiday photos.zip')).closest('li')!
    expect(within(item).getByText(en['shares.chip.unavailable'])).toBeInTheDocument()
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

    expect(await screen.findByRole('status')).toHaveTextContent('Paste it into WhatsApp or an email')
    expect(bridge.copiedInvite).toBe(
      'Someone wants to send you “Birthday party.mp4” (1.20 GB) with ComParta.\n' +
        '1. Get ComParta: https://github.com/BrunoBrock/ComParta/releases\n' +
        '2. Open this invite in ComParta: comparta://receive/v1/s1',
    )
    expect(screen.getByRole('button', { name: 'Copy invite for Birthday party.mp4' })).toHaveTextContent(en['shares.copied'])
  })
})

describe('receiving from another person', () => {
  const receiving: TransferDto = {
    id: 't1',
    name: 'Birthday party.mp4',
    sizeBytes: 1000 * 1024 * 1024,
    receivedBytes: 250 * 1024 * 1024,
    state: 'receiving',
    bytesPerSecond: 0,
    secondsLeft: null,
  }

  it('shows the empty-state message when nothing is being received', async () => {
    const { user } = setup()

    await goToReceived(user)

    expect(await screen.findByText(en['transfers.empty'])).toBeInTheDocument()
  })

  it('starts looking for the other computer when an invite is pasted', async () => {
    const { user } = setup()

    await pasteInvite(user, sampleFakeInvite)

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await within(transfersSection()).findByText(en['transfers.state.connecting'])).toBeInTheDocument()
    expect(within(transfersSection()).getByText(en['transfers.chip.connecting'])).toBeInTheDocument()
  })

  it('explains when the text is not an invite and keeps what was typed', async () => {
    const { user } = setup()

    await enterInvite(user, 'hello grandma')

    const dialog = screen.getByRole('dialog', { name: en['receive.title'] })
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(en['error.invite.invalid'])
    expect(within(dialog).getByLabelText(en['transfers.inviteLabel'])).toHaveValue('hello grandma')
  })

  it('closes the invite box without receiving anything', async () => {
    const { user } = setup()

    await user.click(await screen.findByRole('button', { name: en['transfers.open'] }))
    await user.click(screen.getByRole('button', { name: en['receive.cancel'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(sharesSection()).toBeInTheDocument()
  })

  it('shows progress while receiving and where the file is when done', async () => {
    const { bridge, user } = setup({ transfers: [receiving] })
    await goToReceived(user)

    expect(await screen.findByText('Receiving: 25% (250.0 MB of 1,000.0 MB)')).toBeInTheDocument()
    expect(within(transfersSection()).getByRole('progressbar')).toHaveAttribute('value', String(receiving.receivedBytes))

    act(() => bridge.setTransfers([{ ...receiving, receivedBytes: receiving.sizeBytes!, state: 'done' }]))

    expect(await screen.findByText(en['transfers.state.done'])).toBeInTheDocument()
    expect(within(within(transfersSection()).getByRole('listitem')).getByText(en['transfers.chip.done'])).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Clear Birthday party.mp4 from list' })).toBeInTheDocument()
  })

  it('cancels a transfer', async () => {
    const { user } = setup({ transfers: [receiving] })
    await goToReceived(user)

    await user.click(await screen.findByRole('button', { name: 'Cancel receiving Birthday party.mp4' }))

    expect(await screen.findByText(en['transfers.empty'])).toBeInTheDocument()
  })

  it('opens the folder with received files', async () => {
    const { bridge, user } = setup()
    await goToReceived(user)

    await user.click(await screen.findByRole('button', { name: en['transfers.openFolder'] }))

    expect(bridge.openedFolder).toBe(1)
  })
})

describe('confirming before receiving', () => {
  it('shows what the invite says before anything is received', async () => {
    const { bridge, user } = setup()

    await enterInvite(user, `Oi! Abra isto: ${sampleFakeInvite} 😀`)

    const confirm = await screen.findByRole('dialog', { name: en['confirm.title'] })
    expect(within(confirm).getByText('Birthday party.mp4')).toBeInTheDocument()
    expect(within(confirm).getByText('238.4 MB · from Paulo')).toBeInTheDocument()
    expect(within(confirm).getByText(en['confirm.fromInvite'])).toBeInTheDocument()
    expect(within(confirm).getByText('You have 120.00 GB free.')).toBeInTheDocument()
    expect(await bridge.invoke('transfers.list')).toEqual({ ok: true, value: [] })
  })

  it('starts nothing when the person says not now', async () => {
    const { bridge, user } = setup()
    await enterInvite(user, sampleFakeInvite)

    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: en['receive.cancel'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await bridge.invoke('transfers.list')).toEqual({ ok: true, value: [] })
  })

  it('says how much to free when the file does not fit', async () => {
    const { user } = setup({ freeBytes: 100 * 1024 * 1024 })

    await enterInvite(user, sampleFakeInvite)

    const confirm = await screen.findByRole('dialog', { name: en['confirm.title'] })
    expect(within(confirm).getByRole('alert')).toHaveTextContent('Free up 138.4 MB on this computer')
    expect(within(confirm).getByRole('button', { name: en['transfers.receive'] })).toBeDisabled()
  })

  it('fills in an invite found on the clipboard and says so', async () => {
    const { bridge, user } = setup()
    bridge.clipboardText = `Paulo sent you this: ${sampleFakeInvite}`

    await user.click(await screen.findByRole('button', { name: en['transfers.open'] }))

    const dialog = screen.getByRole('dialog', { name: en['receive.title'] })
    expect(await within(dialog).findByText(en['receive.found'])).toBeInTheDocument()
    expect(within(dialog).getByLabelText(en['transfers.inviteLabel'])).toHaveValue(sampleFakeInvite)
  })

  it('leaves the box empty when the clipboard holds no invite', async () => {
    const { bridge, user } = setup()
    bridge.clipboardText = 'shopping list'

    await user.click(await screen.findByRole('button', { name: en['transfers.open'] }))

    const dialog = screen.getByRole('dialog', { name: en['receive.title'] })
    expect(within(dialog).getByLabelText(en['transfers.inviteLabel'])).toHaveValue('')
    expect(within(dialog).getByText(en['receive.hint'])).toBeInTheDocument()
  })
})

describe('pasting an invite anywhere', () => {
  function paste(text: string) {
    const event = new Event('paste', { bubbles: true }) as Event & { clipboardData: unknown }
    event.clipboardData = { getData: () => text }
    act(() => {
      document.body.dispatchEvent(event)
    })
  }

  it('opens the confirmation for a message holding an invite', async () => {
    setup()
    await screen.findByText(en['shares.empty'])

    paste(`Oi vó!\n${sampleFakeInvite}\nBeijos`)

    expect(await screen.findByRole('dialog', { name: en['confirm.title'] })).toBeInTheDocument()
  })

  it('does nothing with ordinary text', async () => {
    setup()
    await screen.findByText(en['shares.empty'])

    paste('shopping list')

    await act(() => Promise.resolve())
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})

describe('downloading from a link or a download file', () => {
  const downloading: TransferDto = {
    id: 'd1',
    name: 'Open film.mkv',
    sizeBytes: 1000 * 1024 * 1024,
    receivedBytes: 500 * 1024 * 1024,
    state: 'receiving',
    bytesPerSecond: 2 * 1024 * 1024,
    secondsLeft: 250,
  }

  it('starts a download when a download link is pasted', async () => {
    const { user } = setup()

    await pasteInvite(user, sampleFakeDownloadLink)

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await within(transfersSection()).findByText('Open film.mkv')).toBeInTheDocument()
  })

  it('starts a download from a download file', async () => {
    const { user } = setup()

    await user.click(await screen.findByRole('button', { name: en['transfers.open'] }))
    await user.click(screen.getByRole('button', { name: en['receive.openFile'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await within(transfersSection()).findByText('Free documentary.mkv')).toBeInTheDocument()
  })

  it('keeps the box open when the person closes the file dialog', async () => {
    const { bridge, user } = setup()
    bridge.nextDownloadFile = null

    await user.click(await screen.findByRole('button', { name: en['transfers.open'] }))
    await user.click(screen.getByRole('button', { name: en['receive.openFile'] }))

    const dialog = screen.getByRole('dialog', { name: en['receive.title'] })
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
  })

  it('explains when the chosen file is not a download file', async () => {
    const { bridge, user } = setup()
    bridge.nextDownloadFile = 'invalid'

    await user.click(await screen.findByRole('button', { name: en['transfers.open'] }))
    await user.click(screen.getByRole('button', { name: en['receive.openFile'] }))

    const dialog = screen.getByRole('dialog', { name: en['receive.title'] })
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(en['error.download.invalidFile'])
  })

  it('shows the speed and about how long is left', async () => {
    const { user } = setup({ transfers: [downloading] })
    await goToReceived(user)

    expect(
      await screen.findByText('Receiving: 50% (500.0 MB of 1,000.0 MB) · 2.0 MB per second · about 4 minutes left'),
    ).toBeInTheDocument()
  })

  it('pauses and continues a download', async () => {
    const { user } = setup({ transfers: [downloading] })
    await goToReceived(user)

    await user.click(await screen.findByRole('button', { name: 'Pause Open film.mkv' }))

    const item = within(transfersSection()).getByRole('listitem')
    expect(await within(item).findByText(en['transfers.chip.paused'])).toBeInTheDocument()
    expect(within(item).getByText(en['transfers.state.paused'])).toBeInTheDocument()
    expect(within(item).getByRole('progressbar')).toBeInTheDocument()

    await user.click(within(item).getByRole('button', { name: 'Continue Open film.mkv' }))

    expect(await within(item).findByRole('button', { name: 'Pause Open film.mkv' })).toBeInTheDocument()
  })

  it('offers no pause for a finished file', async () => {
    const { user } = setup({ transfers: [{ ...downloading, receivedBytes: downloading.sizeBytes!, state: 'done' }] })
    await goToReceived(user)

    await within(transfersSection()).findByText('Open film.mkv')
    expect(screen.queryByRole('button', { name: 'Pause Open film.mkv' })).not.toBeInTheDocument()
  })
})

describe('finding my files', () => {
  const recipe: ShareDto = { id: 's3', name: 'Receita de aniversário.pdf', sizeBytes: 482_113, status: 'ready' }

  it('counts the files in each place', async () => {
    setup({ shares: [video, album], transfers: [] })

    expect(await screen.findByText('Birthday party.mp4')).toBeInTheDocument()
    expect(navButton(en['nav.shared'])).toHaveAccessibleName(`${en['nav.shared']} 2`)
    expect(navButton(en['nav.shared'])).toHaveAttribute('aria-current', 'page')
  })

  it('finds a file by name without caring about accents or capitals', async () => {
    const { user } = setup({ shares: [video, recipe] })
    await screen.findByText('Birthday party.mp4')

    await user.type(screen.getByRole('searchbox', { name: en['search.label'] }), 'ANIVERSARIO')

    expect(within(sharesSection()).getAllByRole('listitem')).toHaveLength(1)
    expect(screen.getByText('Receita de aniversário.pdf')).toBeInTheDocument()
  })

  it('says so when nothing matches and clears the search', async () => {
    const { user } = setup({ shares: [video] })
    await screen.findByText('Birthday party.mp4')

    await user.type(screen.getByRole('searchbox', { name: en['search.label'] }), 'tax')

    expect(screen.getByText('Nothing here is called “tax”.')).toBeInTheDocument()
    await user.click(within(sharesSection()).getByRole('button', { name: en['search.clear'] }))
    expect(screen.getByText('Birthday party.mp4')).toBeInTheDocument()
  })

  it('shows files as a grid or a list and remembers the choice', async () => {
    const { user } = setup({ shares: [video] })
    await screen.findByText('Birthday party.mp4')
    expect(within(sharesSection()).getByRole('list')).toHaveClass('items-grid')

    await user.click(screen.getByRole('button', { name: en['view.list'] }))

    expect(within(sharesSection()).getByRole('list')).toHaveClass('items-list')
    expect(screen.getByRole('button', { name: en['view.list'] })).toHaveAttribute('aria-pressed', 'true')
    expect(localStorage.getItem('comparta.view')).toBe('list')
  })

  it('puts files still on their way above finished ones', async () => {
    const done: TransferDto = { id: 't2', name: 'Old photo.jpg', sizeBytes: 10, receivedBytes: 10, state: 'done', bytesPerSecond: 0, secondsLeft: null }
    const coming: TransferDto = { id: 't3', name: 'New video.mp4', sizeBytes: 100, receivedBytes: 40, state: 'receiving', bytesPerSecond: 0, secondsLeft: null }
    const { user } = setup({ transfers: [done, coming] })
    await goToReceived(user)

    const items = await within(transfersSection()).findAllByRole('listitem')
    expect(items[0]).toHaveTextContent('New video.mp4')
    expect(items[1]).toHaveTextContent('Old photo.jpg')
  })
})
