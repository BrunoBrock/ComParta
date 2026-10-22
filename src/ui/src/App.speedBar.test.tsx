import { act, fireEvent, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { TransferDto } from './bridge/contract.generated.ts'
import { en } from './i18n/en.ts'
import {
  video,
  setup,
  sharesSection,
  transfersSection,
  goToReceived,
  railButton,
} from './test/appHarness.tsx'

describe('the speed bar', () => {
  const moving: TransferDto = {
    id: 'm1',
    name: 'Open film.mkv',
    sizeBytes: 1000 * 1024 * 1024,
    receivedBytes: 500 * 1024 * 1024,
    state: 'receiving',
    bytesPerSecond: 2 * 1024 * 1024,
    secondsLeft: 250,
    neededBytes: null,
    risky: false,
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
    neededBytes: null,
    risky: false,
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
    await user.click(await railButton(en['transfers.open']))
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
