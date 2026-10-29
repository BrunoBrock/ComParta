import { screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { TransferDto } from './bridge/contract.generated.ts'
import { createFakeBridge } from './bridge/fakeBridge.ts'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { goToReceived, setup, transfersSection } from './test/appHarness.tsx'

// A received file Windows' protection stopped (AD-13): it says so and is never offered to open.
const stopped: TransferDto = {
  id: 'b1',
  name: 'game setup.exe',
  sizeBytes: 3_000_000,
  receivedBytes: 3_000_000,
  state: 'blocked',
  bytesPerSecond: 0,
  secondsLeft: null,
  neededBytes: null,
  risky: true,
  folder: false,
}

const itemOf = async (name: string) => (await within(transfersSection()).findByText(name)).closest('li')!
const row = (dialog: HTMLElement, label: string) =>
  within(dialog).queryByText(label, { selector: 'dt' })?.nextElementSibling?.textContent

describe('a received file the antivirus stopped', () => {
  it('says so on its card, offers nothing to open, and can be cleared', async () => {
    const { user } = setup({ transfers: [stopped] })
    await goToReceived(user)

    const item = await itemOf(stopped.name!)
    expect(within(item).getByText(en['transfers.chip.blocked'])).toBeInTheDocument()
    expect(within(item).getByText(en['transfers.state.blocked'])).toBeInTheDocument()
    for (const name of [/^Open/, /^Show/, /^Pause/, /^Continue/])
      expect(within(item).queryByRole('button', { name })).not.toBeInTheDocument()

    item.focus()
    await user.keyboard('{Delete}')
    expect(await within(transfersSection()).findByText(en['transfers.empty'])).toBeInTheDocument()
  })

  it('says why in its details, with no way to open it or find it among the received files', async () => {
    const { user } = setup({ transfers: [stopped] })
    await goToReceived(user)

    await user.click(await screen.findByRole('button', { name: `Details of ${stopped.name}` }))

    const dialog = screen.getByRole('dialog')
    await vi.waitFor(() => expect(row(dialog, en['details.state'])).toBe(en['transfers.chip.blocked']))
    expect(within(dialog).getByText(en['transfers.state.blocked'])).toBeInTheDocument()
    expect(row(dialog, en['details.where'])).toBeUndefined()
    for (const label of [en['received.open'], en['received.openWith'], en['received.reveal']])
      expect(within(dialog).queryByRole('button', { name: label })).not.toBeInTheDocument()
  })

  it('reads in Portuguese', async () => {
    const { user } = setup({ language: 'pt-BR', transfers: [stopped] })
    await user.click(await screen.findByRole('button', { name: new RegExp(`^${ptBR['nav.received']}`) }))

    const item = (await screen.findByText(stopped.name!)).closest('li')!
    expect(within(item).getByText(ptBR['transfers.chip.blocked'])).toBeInTheDocument()
    expect(within(item).getByText(ptBR['transfers.state.blocked'])).toBeInTheDocument()
    expect(within(item).queryByRole('button', { name: new RegExp(`^${ptBR['received.open']}`) })).not.toBeInTheDocument()
  })

  it('cannot be paused or opened through the bridge, as in the app', async () => {
    const bridge = createFakeBridge({ transfers: [stopped] })

    expect(await bridge.invoke('transfers.pause', { id: stopped.id })).toEqual({
      ok: false,
      error: expect.objectContaining({ code: 'transfer.finished' }),
    })
    expect(await bridge.invoke('received.open', { id: stopped.id, confirmed: false })).toEqual({
      ok: false,
      error: expect.objectContaining({ code: 'received.notFound' }),
    })
  })
})
