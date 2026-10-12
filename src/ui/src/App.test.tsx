import { act, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App, { stuckAfterMs } from './App.tsx'
import type { ShareDto, TransferDto } from './bridge/contract.generated.ts'
import {
  createFakeBridge,
  sampleFakeDownloadLink,
  sampleFakeInvite,
  type FakeBridgeOptions,
} from './bridge/fakeBridge.ts'
import { en } from './i18n/en.ts'
import { setLanguage } from './i18n/index.ts'
import { ptBR } from './i18n/pt-BR.ts'

const video: ShareDto = { id: 's1', name: 'Birthday party.mp4', sizeBytes: 1_288_490_188, status: 'ready' }
const album: ShareDto = { id: 's2', name: 'Holiday photos.zip', sizeBytes: 2048, status: 'unavailable' }

beforeEach(() => {
  localStorage.clear()
  setLanguage('en')
})

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

describe('the language', () => {
  it('starts in Portuguese on a computer set to Brazilian Portuguese', async () => {
    setup({ systemLanguage: 'pt-BR' })

    expect(await screen.findByText(ptBR['shares.empty'])).toBeInTheDocument()
    expect(document.documentElement.lang).toBe('pt-BR')
  })

  it('starts in English on a computer set to a language ComParta does not speak', async () => {
    setup({ systemLanguage: 'fr-FR' })

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
  })

  it('keeps the chosen language after the app starts again', async () => {
    const bridge = createFakeBridge({ systemLanguage: 'pt-BR' })
    const user = userEvent.setup()
    const first = render(<App bridge={bridge} />)
    await screen.findByText(ptBR['shares.empty'])

    await user.selectOptions(screen.getByRole('combobox', { name: ptBR['language.label'] }), 'en')

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
    first.unmount()
    setLanguage('pt-BR')
    render(<App bridge={bridge} />)
    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: en['language.label'] })).toHaveValue('en')
  })
})

describe('this computer\'s connection', () => {
  const indicator = (label: string) => screen.findByRole('button', { name: en['connection.open'].replace('{state}', label) })

  it('shows that others can reach this computer', async () => {
    setup()

    expect(await indicator(en['connection.good'])).toBeInTheDocument()
  })

  it('follows changes and explains a limited connection with three things to try', async () => {
    const { bridge, user } = setup()
    await indicator(en['connection.good'])

    act(() => bridge.setConnection('limited'))
    await user.click(await indicator(en['connection.limited']))

    const help = screen.getByRole('dialog', { name: en['connection.title'] })
    expect(within(help).getByText(en['connection.state.limited'])).toBeInTheDocument()
    expect(within(help).getAllByRole('listitem')).toHaveLength(3)
    expect(within(help).queryByRole('button', { name: en['connection.openSettings'] })).not.toBeInTheDocument()
    await user.click(within(help).getByRole('button', { name: en['connection.close'] }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('offers to open the Windows settings when nobody can reach this computer', async () => {
    const { bridge, user } = setup({ connection: 'blocked' })

    await user.click(await indicator(en['connection.blocked']))
    const help = screen.getByRole('dialog', { name: en['connection.title'] })
    expect(within(help).getByText(en['connection.state.blocked'])).toBeInTheDocument()
    await user.click(within(help).getByRole('button', { name: en['connection.openSettings'] }))

    expect(bridge.openedConnectionSettings).toBe(1)
  })

  it('uses no networking words in either language', () => {
    const banned = /\b(port|router|firewall|nat|upnp|ipv[46]|ip|dns|address)\b/i
    for (const catalog of [en, ptBR] as Record<string, string>[]) {
      for (const [key, text] of Object.entries(catalog)) {
        if (key.startsWith('connection.')) expect(text, key).not.toMatch(banned)
      }
    }
  })
})

describe('a transfer stuck waiting', () => {
  const waiting: TransferDto = {
    id: 't1',
    name: null,
    sizeBytes: null,
    receivedBytes: 0,
    state: 'connecting',
    bytesPerSecond: 0,
    secondsLeft: null,
  }

  afterEach(() => {
    vi.useRealTimers()
  })

  it('offers "What can I do?" only after waiting ten minutes, and it opens the connection help', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    const { user } = setup({ transfers: [waiting] })
    await goToReceived(user)
    await within(transfersSection()).findByText(en['transfers.state.connecting'])
    expect(screen.queryByRole('button', { name: /What can I do/ })).not.toBeInTheDocument()

    act(() => {
      vi.setSystemTime(Date.now() + stuckAfterMs)
      vi.advanceTimersByTime(30_000)
    })

    const help = await screen.findByRole('button', { name: en['transfers.helpFor'].replace('{name}', en['transfers.unnamed']) })
    await user.click(help)
    expect(screen.getByRole('dialog', { name: en['connection.title'] })).toBeInTheDocument()
  })

  it('stops offering help once the file starts arriving', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    const { bridge, user } = setup({ transfers: [waiting] })
    await goToReceived(user)
    await within(transfersSection()).findByText(en['transfers.state.connecting'])
    act(() => {
      vi.setSystemTime(Date.now() + stuckAfterMs)
      vi.advanceTimersByTime(30_000)
    })
    await screen.findByRole('button', { name: /What can I do/ })

    act(() => bridge.setTransfers([{ ...waiting, name: 'film.mkv', sizeBytes: 1000, receivedBytes: 10, state: 'receiving' }]))

    expect(screen.queryByRole('button', { name: /What can I do/ })).not.toBeInTheDocument()
  })
})

describe('an invite link clicked outside the app', () => {
  it('opens the confirmation when the app starts with one', async () => {
    const bridge = createFakeBridge()
    bridge.openInvite(sampleFakeInvite)
    render(<App bridge={bridge} />)

    expect(await screen.findByRole('dialog', { name: en['confirm.title'] })).toBeInTheDocument()
  })

  it('opens the confirmation when one arrives while the app runs', async () => {
    const { bridge } = setup()
    await screen.findByText(en['shares.empty'])

    act(() => bridge.openInvite(`comparta://receive/v1/${'x'.repeat(4)}`))

    expect(await screen.findByRole('dialog', { name: en['confirm.title'] })).toBeInTheDocument()
  })

  it('explains a link ComParta cannot use', async () => {
    const { bridge } = setup()
    await screen.findByText(en['shares.empty'])

    act(() => bridge.openInvite('comparta://receive/v1/'))

    expect(await screen.findByText(en['error.invite.invalid'])).toBeInTheDocument()
  })
})

describe('the speed bar', () => {
  const moving: TransferDto = {
    id: 'm1',
    name: 'Open film.mkv',
    sizeBytes: 1000 * 1024 * 1024,
    receivedBytes: 500 * 1024 * 1024,
    state: 'receiving',
    bytesPerSecond: 2 * 1024 * 1024,
    secondsLeft: 250,
  }
  const speedBar = () => screen.getByRole('region', { name: en['speed.label'] })

  it('shows what is being received and sent in total', async () => {
    setup({ transfers: [moving] })

    expect(await within(speedBar()).findByText('2.0 MB/s')).toBeInTheDocument()
    expect(within(speedBar()).getByText('0 bytes/s')).toBeInTheDocument()
  })

  it('stops sending and receiving with one press, and Continue starts both again', async () => {
    const { bridge, user } = setup({ shares: [video], transfers: [moving] })
    const stop = await screen.findByRole('button', { name: en['stop.both'] })
    await vi.waitFor(() => expect(stop).toBeEnabled())

    await user.click(stop)

    expect(within(speedBar()).getByText(en['speed.receivingStopped'])).toBeInTheDocument()
    expect(within(speedBar()).getByText(en['speed.sendingStopped'])).toBeInTheDocument()
    expect(within(sharesSection()).getByText(en['stopped.note'])).toBeInTheDocument()
    const activity = await bridge.invoke('activity.get')
    expect(activity.ok && activity.value.sendingStopped && activity.value.receivingStopped).toBe(true)

    await user.click(screen.getByRole('button', { name: en['stop.continue'] }))

    expect(within(speedBar()).queryByText(en['speed.receivingStopped'])).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: en['stop.both'] })).toBeInTheDocument()
  })

  it('stops only receiving from the menu, and the file reads "Stopped" without its own Pause', async () => {
    const { user } = setup({ shares: [video], transfers: [moving] })
    await screen.findByText('2.0 MB/s')
    await user.click(screen.getByRole('button', { name: en['stop.menu'] }))
    const menu = screen.getByRole('menu', { name: en['stop.menu'] })
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      en['stop.menuBoth'],
      en['stop.menuSending'],
      en['stop.menuReceiving'],
    ])

    await user.click(within(menu).getByRole('menuitem', { name: en['stop.menuReceiving'] }))

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(within(speedBar()).getByText(en['speed.receivingStopped'])).toBeInTheDocument()
    expect(within(speedBar()).queryByText(en['speed.sendingStopped'])).not.toBeInTheDocument()
    expect(within(sharesSection()).queryByText(en['stopped.note'])).not.toBeInTheDocument()
    await goToReceived(user)
    expect(within(transfersSection()).getByText(en['stopped.chip'])).toBeInTheDocument()
    expect(within(transfersSection()).queryByRole('button', { name: /^Pause/ })).not.toBeInTheDocument()
  })

  it('closes the menu with Escape and moves through it with the arrow keys', async () => {
    const { user } = setup()
    const toggle = await screen.findByRole('button', { name: en['stop.menu'] })
    await vi.waitFor(() => expect(toggle).toBeEnabled())
    await user.click(toggle)
    const items = within(screen.getByRole('menu')).getAllByRole('menuitem')
    expect(items[0]).toHaveFocus()

    await user.keyboard('{ArrowUp}')
    expect(items[2]).toHaveFocus()
    await user.keyboard('{ArrowDown}')
    expect(items[0]).toHaveFocus()
    await user.keyboard('{Escape}')

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(toggle).toHaveFocus()
  })

  it('closes the menu when the person clicks elsewhere', async () => {
    const { user } = setup()
    const toggle = await screen.findByRole('button', { name: en['stop.menu'] })
    await vi.waitFor(() => expect(toggle).toBeEnabled())
    await user.click(toggle)

    await user.click(screen.getByRole('heading', { name: en['shares.heading'] }))

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('starts with the stop the app remembered', async () => {
    setup({ shares: [video], stopped: { sendingStopped: true } })

    expect(await screen.findByText(en['speed.sendingStopped'])).toBeInTheDocument()
    expect(within(sharesSection()).getByText(en['stopped.chip'])).toBeInTheDocument()
    expect(screen.getByRole('button', { name: en['stop.continue'] })).toBeInTheDocument()
  })
})

describe('the details of a file', () => {
  const arriving: TransferDto = {
    id: 'a1',
    name: 'Open film.mkv',
    sizeBytes: 1000 * 1024 * 1024,
    receivedBytes: 500 * 1024 * 1024,
    state: 'receiving',
    bytesPerSecond: 2 * 1024 * 1024,
    secondsLeft: 250,
  }
  const row = (dialog: HTMLElement, label: string) =>
    within(dialog).getByText(label, { selector: 'dt' }).nextElementSibling?.textContent

  it('shows a shared file’s size, state, folder and what it has sent', async () => {
    const { user } = setup({ shares: [video] })

    await user.click(await screen.findByRole('button', { name: `Details of ${video.name}` }))

    const dialog = screen.getByRole('dialog', { name: `Details of “${video.name}”` })
    await vi.waitFor(() => expect(row(dialog, en['details.size'])).toBe('1.20 GB'))
    expect(row(dialog, en['details.state'])).toBe(en['shares.chip.ready'])
    expect(row(dialog, en['details.where'])).toBe('C:\\Users\\Ana\\Videos')
    expect(row(dialog, en['details.connections'])).toBe('0')
    expect(within(dialog).queryByText(en['details.source'])).not.toBeInTheDocument()
  })

  it('shows how much of a received file has arrived and where it came from', async () => {
    const { user } = setup({ transfers: [arriving] })
    await goToReceived(user)

    await user.click(await screen.findByRole('button', { name: `Details of ${arriving.name}` }))

    const dialog = screen.getByRole('dialog')
    await vi.waitFor(() => expect(row(dialog, en['details.size'])).toBe('1,000.0 MB, 500.0 MB received (50%)'))
    expect(row(dialog, en['details.state'])).toBe(en['details.state.receiving'])
    expect(row(dialog, en['details.source'])).toBe(en['details.source.invite'])
    expect(row(dialog, en['details.receivingNow'])).toBe('2.0 MB/s, about 4 minutes left')
    expect(row(dialog, en['details.where'])).toBe(en['details.whereReceived'])
  })

  it('says "Stopped" while receiving is stopped', async () => {
    const { user } = setup({ transfers: [arriving], stopped: { receivingStopped: true } })
    await goToReceived(user)

    await user.click(await screen.findByRole('button', { name: `Details of ${arriving.name}` }))

    const dialog = screen.getByRole('dialog')
    await vi.waitFor(() => expect(row(dialog, en['details.state'])).toBe(en['stopped.chip']))
    expect(row(dialog, en['details.receivingNow'])).toBe('0 bytes/s')
  })

  it('remembers how a download file was received', async () => {
    const { user } = setup()
    await user.click(await screen.findByRole('button', { name: en['transfers.open'] }))
    await user.click(screen.getByRole('button', { name: en['receive.openFile'] }))
    await goToReceived(user)

    await user.click(await screen.findByRole('button', { name: /^Details of Free documentary/ }))

    const dialog = screen.getByRole('dialog')
    await vi.waitFor(() => expect(row(dialog, en['details.source'])).toBe(en['details.source.downloadFile']))
  })

  it('refreshes every second and says so when the file leaves the list', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    try {
      const { bridge, user } = setup({ shares: [video] })
      const opener = await screen.findByRole('button', { name: `Details of ${video.name}` })
      await user.click(opener)
      const dialog = screen.getByRole('dialog')
      await vi.waitFor(() => expect(row(dialog, en['details.state'])).toBe(en['shares.chip.ready']))

      act(() => bridge.setShares([{ ...video, status: 'unavailable' }]))
      await act(() => vi.advanceTimersByTimeAsync(1000))
      expect(row(dialog, en['details.state'])).toBe(en['shares.chip.unavailable'])

      act(() => bridge.setShares([]))
      await act(() => vi.advanceTimersByTimeAsync(1000))
      expect(within(dialog).getByText(en['details.gone'])).toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })

  it('closes with Close and with Escape, and focus goes back to the button', async () => {
    const { user } = setup({ shares: [video] })
    const opener = await screen.findByRole('button', { name: `Details of ${video.name}` })

    await user.click(opener)
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: en['details.close'] }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(opener).toHaveFocus()

    await user.click(opener)
    fireEvent(screen.getByRole('dialog'), new Event('cancel', { cancelable: true }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
