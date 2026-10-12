import { act, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { setup, video } from './test/appHarness.tsx'

const rail = () => screen.getByRole('complementary')

describe('keeping sharing in the background', () => {
  it('turns start with Windows on in Settings and keeps the choice', async () => {
    const { bridge, user } = setup()
    await screen.findByText(en['shares.empty'])

    await user.click(within(rail()).getByRole('button', { name: en['nav.settings'] }))
    const box = screen.getByRole('checkbox', { name: en['settings.startWithWindows'] })
    expect(box).not.toBeChecked()

    await user.click(box)

    expect(box).toBeChecked()
    const stored = await bridge.invoke('settings.get')
    expect(stored.ok && stored.value.startWithWindows).toBe(true)
  })

  it('shows start with Windows as chosen before', async () => {
    const { user } = setup({ startWithWindows: true })
    await screen.findByText(en['shares.empty'])

    await user.click(within(rail()).getByRole('button', { name: en['nav.settings'] }))

    expect(screen.getByRole('checkbox', { name: en['settings.startWithWindows'] })).toBeChecked()
  })

  it('asks before quitting from the icon cuts someone off, naming the file, and quits when confirmed', async () => {
    const { bridge, user } = setup({ shares: [{ ...video, activity: 'sending', receiverCount: 1, percent: 30 }] })
    await screen.findAllByText(video.name)

    act(() => bridge.quitFromIcon())

    const dialog = await screen.findByRole('dialog', { name: en['quit.title'] })
    expect(within(dialog).getByText(video.name)).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: en['quit.confirm'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(bridge.quits).toBe(1)
  })

  it('keeps running when the person changes their mind', async () => {
    const { bridge, user } = setup({ shares: [{ ...video, activity: 'sending', receiverCount: 1, percent: 30 }] })
    await screen.findAllByText(video.name)

    act(() => bridge.quitFromIcon())
    await user.click(await screen.findByRole('button', { name: en['quit.cancel'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(bridge.quits).toBe(0)
  })

  it('asks in Portuguese', async () => {
    const { bridge } = setup({ language: 'pt-BR', shares: [{ ...video, activity: 'sending', receiverCount: 1 }] })
    await screen.findAllByText(video.name)

    act(() => bridge.quitFromIcon())

    expect(await screen.findByRole('dialog', { name: ptBR['quit.title'] })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: ptBR['quit.confirm'] })).toBeInTheDocument()
  })
})
