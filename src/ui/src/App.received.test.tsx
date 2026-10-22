import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { TransferDto } from './bridge/contract.generated.ts'
import { en } from './i18n/en.ts'
import { goToReceived, setup, transfersSection } from './test/appHarness.tsx'

describe('using what was received', () => {
  const arrived: TransferDto = {
    id: 't1',
    name: 'Recipe.pdf',
    sizeBytes: 2048,
    receivedBytes: 2048,
    state: 'done',
    bytesPerSecond: 0,
    secondsLeft: null,
    neededBytes: null,
    risky: false, folder: false,
  }
  const onTheWay: TransferDto = {
    id: 't2',
    name: 'Birthday party.mp4',
    sizeBytes: 1000,
    receivedBytes: 250,
    state: 'receiving',
    bytesPerSecond: 0,
    secondsLeft: null,
    neededBytes: null,
    risky: false, folder: false,
  }

  const button = (label: string) => within(transfersSection()).findByRole('button', { name: label })

  it('pins files on their way above the ones that arrived', async () => {
    const { user } = setup({ transfers: [arrived, onTheWay] })
    await goToReceived(user)

    const lists = await within(transfersSection()).findAllByRole('list')
    expect(lists.map((list) => list.getAttribute('aria-labelledby'))).toEqual([
      'transfers-onTheWay',
      'transfers-arrived',
    ])
    expect(within(lists[0]).getByText(onTheWay.name!)).toBeInTheDocument()
    expect(within(lists[1]).getByText(arrived.name!)).toBeInTheDocument()
    expect(within(transfersSection()).getByRole('heading', { name: en['transfers.onTheWay'] })).toBeInTheDocument()
    expect(within(transfersSection()).getByRole('heading', { name: en['transfers.arrived'] })).toBeInTheDocument()
  })

  it('offers open, show and delete only for files that arrived', async () => {
    const { user } = setup({ transfers: [arrived, onTheWay] })
    await goToReceived(user)

    await button('Open Recipe.pdf')
    expect(screen.queryByRole('button', { name: 'Open Birthday party.mp4' })).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Delete Birthday party.mp4 from this computer' }),
    ).not.toBeInTheDocument()
  })

  it('opens a received file with another app and shows it in its folder', async () => {
    const { user, bridge } = setup({ transfers: [arrived] })
    await goToReceived(user)

    await user.click(await button('Open Recipe.pdf with another app'))
    await user.click(await button('Show Recipe.pdf in its folder'))

    expect(bridge.openedReceived).toEqual(['t1'])
    expect(bridge.revealedReceived).toEqual(['t1'])
  })

  it('asks before deleting, and keeping the file changes nothing', async () => {
    const { user } = setup({ transfers: [arrived] })
    await goToReceived(user)

    await user.click(await button('Delete Recipe.pdf from this computer'))
    const dialog = screen.getByRole('dialog', { name: 'Delete “Recipe.pdf”?' })
    await user.click(within(dialog).getByRole('button', { name: en['deleteReceived.cancel'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(within(transfersSection()).getByText(arrived.name!)).toBeInTheDocument()
  })

  it('deletes the file after the person confirms and says so', async () => {
    const { user } = setup({ transfers: [arrived] })
    await goToReceived(user)

    await user.click(await button('Delete Recipe.pdf from this computer'))
    await user.click(screen.getByRole('button', { name: en['deleteReceived.confirm'] }))

    expect(await screen.findByText('Deleted “Recipe.pdf” from this computer.')).toBeInTheDocument()
    expect(within(transfersSection()).queryByText(arrived.name!)).not.toBeInTheDocument()
  })

  it('explains when the received file is no longer there', async () => {
    const { user, bridge } = setup({ transfers: [arrived] })
    await goToReceived(user)

    bridge.failNextWith = 'received.notFound'
    await user.click(await button('Open Recipe.pdf'))

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.received.notFound'])
  })

  it('points to the folder when the file cannot be opened', async () => {
    const { user, bridge } = setup({ transfers: [arrived] })
    await goToReceived(user)

    bridge.failNextWith = 'received.openFailed'
    await user.click(await button('Open Recipe.pdf with another app'))

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.received.openFailed'])
  })

  it('asks again before opening a file that can run a program, and opens it only after the second yes', async () => {
    const program: TransferDto = { ...arrived, id: 't3', name: 'Setup.exe', risky: true }
    const { user, bridge } = setup({ transfers: [program] })
    await goToReceived(user)

    await user.click(await button('Open Setup.exe with another app'))

    const dialog = await screen.findByRole('dialog', { name: 'Open “Setup.exe”?' })
    expect(bridge.openedReceived).toEqual([])
    await user.click(within(dialog).getByRole('button', { name: en['riskyOpen.confirm'] }))

    expect(bridge.openedReceived).toEqual(['t3'])
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('keeps a program closed when the person cancels the second question', async () => {
    const program: TransferDto = { ...arrived, id: 't3', name: 'Setup.exe', risky: true }
    const { user, bridge } = setup({ transfers: [program] })
    await goToReceived(user)

    await user.click(await button('Open Setup.exe with another app'))
    const dialog = await screen.findByRole('dialog', { name: 'Open “Setup.exe”?' })
    await user.click(within(dialog).getByRole('button', { name: en['riskyOpen.cancel'] }))

    expect(bridge.openedReceived).toEqual([])
  })

  it('shows a received folder as its files instead of opening it', async () => {
    const folder: TransferDto = { ...arrived, id: 't4', name: 'Trip', folder: true }
    const { user, bridge } = setup({ transfers: [folder] })
    await goToReceived(user)

    await user.click(await button('Open Trip with another app'))

    const dialog = await screen.findByRole('dialog', { name: '“Trip”' })
    expect(within(dialog).getByText('day 1/photo.jpg')).toBeInTheDocument()
    expect(within(dialog).getByText('notes.txt')).toBeInTheDocument()
    expect(bridge.openedReceived).toEqual([])
  })

  it('opens an ordinary received file without asking again', async () => {
    const { user, bridge } = setup({ transfers: [arrived] })
    await goToReceived(user)

    await user.click(await button('Open Recipe.pdf with another app'))

    expect(bridge.openedReceived).toEqual(['t1'])
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})

describe('pictures of received files', () => {
  it('shows a thumbnail for an arrived video and the icon for others', async () => {
    const video: TransferDto = {
      id: 't3',
      name: 'Holiday.mp4',
      sizeBytes: 2048,
      receivedBytes: 2048,
      state: 'done',
      bytesPerSecond: 0,
      secondsLeft: null,
      neededBytes: null,
      risky: false, folder: false,
    }
    const zip: TransferDto = { ...video, id: 't4', name: 'Photos.zip' }
    const { user } = setup({ transfers: [video, zip] })
    await goToReceived(user)

    const card = (name: string) =>
      within(transfersSection()).getByText(name, { selector: '.visually-hidden, .name-start' }).closest('li')!
    await waitFor(() => expect(card('Holiday.mp4').querySelector('img')).not.toBeNull())
    expect(card('Photos.zip').querySelector('img')).toBeNull()
  })
})
