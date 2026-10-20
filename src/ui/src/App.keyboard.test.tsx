import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { TransferDto } from './bridge/contract.generated.ts'
import { en } from './i18n/en.ts'
import { album, goToReceived, setup, sharesSection, transfersSection, video } from './test/appHarness.tsx'

const item = (section: HTMLElement, name: string) =>
  within(section).getByText(name, { selector: '.visually-hidden, .name-start' }).closest('li')!

const arrived: TransferDto = {
  id: 't1',
  name: 'Recipe.pdf',
  sizeBytes: 2048,
  receivedBytes: 2048,
  state: 'done',
  bytesPerSecond: 0,
  secondsLeft: null,
  neededBytes: null,
}

describe('using the files with the keyboard', () => {
  it('moves between files with the arrow keys, Home and End', async () => {
    const { user } = setup({ shares: [video, album] })
    await screen.findByText(video.name, { selector: '.visually-hidden, .name-start' })
    const [first, second] = within(sharesSection()).getAllByRole('listitem')

    first.focus()
    await user.keyboard('{ArrowRight}')
    expect(second).toHaveFocus()
    await user.keyboard('{ArrowDown}')
    expect(second).toHaveFocus()
    await user.keyboard('{Home}')
    expect(first).toHaveFocus()
    await user.keyboard('{End}')
    expect(second).toHaveFocus()
    await user.keyboard('{ArrowLeft}')
    expect(first).toHaveFocus()
  })

  it('opens the details with Enter and asks before stopping with Delete', async () => {
    const { user } = setup({ shares: [video] })
    await screen.findByText(video.name, { selector: '.visually-hidden, .name-start' })

    item(sharesSection(), video.name).focus()
    await user.keyboard('{Enter}')
    expect(await screen.findByRole('dialog', { name: `Details of “${video.name}”` })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: en['details.close'] }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(item(sharesSection(), video.name)).toHaveFocus()

    await user.keyboard('{Delete}')
    expect(screen.getByRole('dialog', { name: `Stop sharing “${video.name}”?` })).toBeInTheDocument()
  })

  it('copies the invite on a double-click', async () => {
    const { bridge, user } = setup({ shares: [video] })
    await screen.findByText(video.name, { selector: '.visually-hidden, .name-start' })

    await user.dblClick(item(sharesSection(), video.name))

    await waitFor(() => expect(bridge.copiedInvite).toMatch(/wants to send you/))
  })

  it('opens a received file inside ComParta on a double-click and asks before deleting it with Delete', async () => {
    const { bridge, user } = setup({ transfers: [arrived] })
    await goToReceived(user)
    await within(transfersSection()).findByText(arrived.name!, { selector: '.visually-hidden, .name-start' })

    await user.dblClick(item(transfersSection(), arrived.name!))
    // A document cannot be played here, so the viewer offers its folder instead.
    const viewer = await screen.findByRole('dialog', { name: arrived.name! })
    expect(await within(viewer).findByRole('alert')).toHaveTextContent(en['error.media.notPlayable'])
    await user.click(within(viewer).getByRole('button', { name: en['viewer.cancel'] }))
    expect(bridge.openedReceived).toEqual([])

    item(transfersSection(), arrived.name!).focus()
    await user.keyboard('{Delete}')
    expect(screen.getByRole('dialog', { name: 'Delete “Recipe.pdf”?' })).toBeInTheDocument()
  })
})

describe('the main action in the details', () => {
  it('copies the invite of a shared file', async () => {
    const { bridge, user } = setup({ shares: [video] })
    await user.click(await screen.findByRole('button', { name: `Details of ${video.name}` }))
    const details = await screen.findByRole('dialog', { name: `Details of “${video.name}”` })

    await user.click(await within(details).findByRole('button', { name: en['shares.copyInvite'] }))

    await waitFor(() => expect(bridge.copiedInvite).toMatch(/wants to send you/))
  })

  it('opens and shows a received file', async () => {
    const { bridge, user } = setup({ transfers: [arrived] })
    await goToReceived(user)
    await user.click(await screen.findByRole('button', { name: `Details of ${arrived.name}` }))
    const details = await screen.findByRole('dialog', { name: `Details of “${arrived.name}”` })

    await user.click(await within(details).findByRole('button', { name: en['received.openWith'] }))
    await user.click(within(details).getByRole('button', { name: en['received.reveal'] }))

    expect(bridge.openedReceived).toEqual(['t1'])
    expect(bridge.revealedReceived).toEqual(['t1'])
  })
})
