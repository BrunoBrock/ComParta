import { act, render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import type { TransferDto } from './bridge/contract.generated.ts'
import { createFakeBridge, sampleFakeInvite } from './bridge/fakeBridge.ts'
import { en } from './i18n/en.ts'
import {
  setup,
  sharesSection,
  transfersSection,
  goToReceived,
  enterInvite,
  pasteInvite,
} from './test/appHarness.tsx'

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
