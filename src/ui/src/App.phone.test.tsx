import { act, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { TransferDto } from './bridge/contract.generated.ts'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { goToReceived, setup, video } from './test/appHarness.tsx'

describe("the phone's back button", () => {
  it('closes the floating menu first, then leaves the app', async () => {
    const { bridge, user } = setup()
    await screen.findByText(en['shares.empty'])
    const fab = screen.getByRole('button', { name: en['fab.open'] })

    await user.click(fab)
    act(() => bridge.pressBack())

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(fab).toHaveFocus()
    expect(bridge.leaves).toBe(0)

    await act(async () => bridge.pressBack())
    expect(bridge.leaves).toBe(1)
  })

  it('closes the details sheet and goes back to the files, still shared', async () => {
    const { bridge, user } = setup({ shares: [video] })
    await user.click(await screen.findByRole('button', { name: `Details of ${video.name}` }))
    expect(screen.getByRole('dialog', { name: `Details of “${video.name}”` })).toBeInTheDocument()

    act(() => bridge.pressBack())

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getAllByText(video.name).length).toBeGreaterThan(0)
    expect(bridge.leaves).toBe(0)
  })

  it('closes Help, and Settings while its list has focus', async () => {
    const { bridge, user } = setup()
    await screen.findByText(en['shares.empty'])
    await user.click(within(screen.getByRole('banner')).getByRole('button', { name: en['nav.help'] }))
    expect(screen.getByRole('dialog', { name: en['help.title'] })).toBeInTheDocument()

    act(() => bridge.pressBack())
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await user.click(within(screen.getByRole('banner')).getByRole('button', { name: en['nav.settings'] }))
    await user.click(screen.getByRole('combobox', { name: en['language.label'] }))
    act(() => bridge.pressBack())

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(bridge.leaves).toBe(0)
  })

  it('closes "Get this file?" without receiving anything', async () => {
    const { bridge, user } = setup()
    await screen.findByText(en['shares.empty'])
    await user.click(screen.getByRole('button', { name: en['fab.open'] }))
    await user.click(screen.getByRole('menuitem', { name: en['transfers.open'] }))
    expect(screen.getByRole('dialog', { name: en['receive.title'] })).toBeInTheDocument()

    act(() => bridge.pressBack())

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    const listed = await bridge.invoke('transfers.list')
    expect(listed.ok && listed.value).toEqual([])
  })
})

describe('a file shared from a phone', () => {
  it('shows its details without a folder, which a phone item does not have', async () => {
    const { user } = setup({ shares: [video], onPhone: true })
    await user.click(await screen.findByRole('button', { name: `Details of ${video.name}` }))
    const sheet = screen.getByRole('dialog', { name: `Details of “${video.name}”` })

    expect(await within(sheet).findByText(en['details.added'])).toBeInTheDocument()
    expect(within(sheet).queryByText(en['details.where'])).not.toBeInTheDocument()
  })

  it('still shows the folder of a file shared from a computer', async () => {
    const { user } = setup({ shares: [video] })
    await user.click(await screen.findByRole('button', { name: `Details of ${video.name}` }))
    const sheet = screen.getByRole('dialog', { name: `Details of “${video.name}”` })

    expect(await within(sheet).findByText(en['details.where'])).toBeInTheDocument()
  })
})

describe('pictures, songs and videos on the phone (epic 7 story 12)', () => {
  const arrived = (id: string, name: string): TransferDto => ({
    id,
    name,
    sizeBytes: 2_000_000,
    receivedBytes: 2_000_000,
    state: 'done',
    bytesPerSecond: 0,
    secondsLeft: null,
    neededBytes: null,
    risky: false,
    folder: false,
  })

  it('the back button closes the video and stops it, and stays in the app', async () => {
    const paused = vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => undefined)
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue()
    const { bridge, user } = setup({ transfers: [arrived('t1', 'Formatura.mp4')] })
    await goToReceived(user)
    await user.click(await screen.findByRole('button', { name: en['received.openFor'].replace('{name}', 'Formatura.mp4') }))
    const viewer = await screen.findByRole('dialog', { name: 'Formatura.mp4' })
    expect(viewer.querySelector('video')?.getAttribute('src')).toMatch(/^https:\/\/media\.comparta\.invalid\/[0-9a-f]{32}$/)

    act(() => bridge.pressBack())

    expect(screen.queryByRole('dialog', { name: 'Formatura.mp4' })).not.toBeInTheDocument()
    expect(paused).toHaveBeenCalled()
    expect(bridge.leaves).toBe(0)
    vi.restoreAllMocks()
  })

  it('a video the phone cannot play offers its folder in Portuguese, and Open the folder opens it', async () => {
    const { bridge, user } = setup({ language: 'pt-BR', transfers: [arrived('t2', 'Casamento.mkv')] })
    await goToReceived(user)
    await user.click(await screen.findByRole('button', { name: ptBR['received.openFor'].replace('{name}', 'Casamento.mkv') }))

    const viewer = await screen.findByRole('dialog', { name: 'Casamento.mkv' })
    expect(await within(viewer).findByRole('alert')).toHaveTextContent(ptBR['error.media.notPlayable'])
    await user.click(within(viewer).getByRole('button', { name: ptBR['viewer.openFolder'] }))

    expect(bridge.revealedReceived).toEqual(['t2'])
    expect(screen.queryByRole('dialog', { name: 'Casamento.mkv' })).not.toBeInTheDocument()
  })

  it('the back button closes the message about a file that cannot play, without opening anything', async () => {
    const { bridge, user } = setup({ transfers: [arrived('t3', 'Casamento.mkv')] })
    await goToReceived(user)
    await user.click(await screen.findByRole('button', { name: en['received.openFor'].replace('{name}', 'Casamento.mkv') }))
    const viewer = await screen.findByRole('dialog', { name: 'Casamento.mkv' })
    await within(viewer).findByRole('alert')

    act(() => bridge.pressBack())

    expect(screen.queryByRole('dialog', { name: 'Casamento.mkv' })).not.toBeInTheDocument()
    expect(bridge.revealedReceived).toEqual([])
    expect(bridge.leaves).toBe(0)
  })
})
