import { act, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { stuckAfterMs } from './App.tsx'
import type { TransferDto } from './bridge/contract.generated.ts'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import {
  setup,
  transfersSection,
  goToReceived,
} from './test/appHarness.tsx'

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
