import { act, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { stuckAfterMs } from './App.tsx'
import type { TransferDto } from './bridge/contract.generated.ts'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { goToReceived, setup, transfersSection } from './test/appHarness.tsx'

const film: TransferDto = {
  id: 't1',
  name: 'film.mp4',
  sizeBytes: 4_000_000,
  receivedBytes: 1_000_000,
  state: 'waiting',
  bytesPerSecond: 0,
  secondsLeft: null,
  neededBytes: null,
  risky: false,
}

const itemOf = async (name: string) => (await within(transfersSection()).findByText(name)).closest('li')!

describe('a transfer that is stuck says why in plain words', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('says the other computer is not answering, keeps what arrived, and can be paused', async () => {
    const { user } = setup({ transfers: [film] })
    await goToReceived(user)

    const item = await itemOf('film.mp4')
    expect(within(item).getByText(en['transfers.chip.waiting'])).toBeInTheDocument()
    expect(within(item).getByText(en['transfers.state.waiting'])).toBeInTheDocument()
    expect(within(item).getByRole('progressbar')).toHaveAttribute('value', '1000000')
    expect(
      within(item).getByRole('button', { name: en['transfers.pauseFor'].replace('{name}', 'film.mp4') }),
    ).toBeInTheDocument()
  })

  it('offers "What can I do?" after a long wait', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    const { user } = setup({ transfers: [film] })
    await goToReceived(user)
    await itemOf('film.mp4')

    act(() => {
      vi.setSystemTime(Date.now() + stuckAfterMs)
      vi.advanceTimersByTime(30_000)
    })

    expect(
      await screen.findByRole('button', { name: en['transfers.helpFor'].replace('{name}', 'film.mp4') }),
    ).toBeInTheDocument()
  })

  it('says how much room to free when the disk is full', async () => {
    const { bridge, user } = setup({ transfers: [{ ...film, state: 'noSpace', neededBytes: 2 * 1024 * 1024 * 1024 }] })
    await goToReceived(user)

    const item = await itemOf('film.mp4')
    expect(within(item).getByText(en['transfers.chip.noSpace'])).toBeInTheDocument()
    expect(
      within(item).getByText('This computer is out of space. Free up 2.00 GB and it will continue.'),
    ).toBeInTheDocument()

    act(() => bridge.setTransfers([{ ...film, state: 'noSpace' }]))
    expect(await within(transfersSection()).findByText(en['transfers.state.noSpaceUnknown'])).toBeInTheDocument()
  })

  it('says the antivirus stopped a file, and it can only be cleared', async () => {
    const { user } = setup({ transfers: [{ ...film, state: 'blocked' }], stopped: { receivingStopped: true } })
    await goToReceived(user)

    const item = await itemOf('film.mp4')
    expect(within(item).getByText(en['transfers.chip.blocked'])).toBeInTheDocument()
    expect(within(item).getByText(en['transfers.state.blocked'])).toBeInTheDocument()
    expect(within(item).queryByRole('button', { name: /Pause/ })).not.toBeInTheDocument()

    item.focus()
    await user.keyboard('{Delete}')
    expect(await within(transfersSection()).findByText(en['transfers.empty'])).toBeInTheDocument()
  })

  it('reads in Portuguese', async () => {
    const { user } = setup({
      language: 'pt-BR',
      transfers: [film, { ...film, id: 't2', name: 'fotos.zip', state: 'noSpace', neededBytes: 500 * 1024 * 1024 }],
    })
    await user.click(await screen.findByRole('button', { name: new RegExp(`^${ptBR['nav.received']}`) }))

    expect(await screen.findByText(ptBR['transfers.chip.waiting'])).toBeInTheDocument()
    expect(screen.getByText(ptBR['transfers.state.waiting'])).toBeInTheDocument()
    expect(screen.getByText(/^Este computador está sem espaço. Libere 500(,0)? MB e ele continua.$/)).toBeInTheDocument()
  })
})
