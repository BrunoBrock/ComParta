import { act, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { setup, video } from './test/appHarness.tsx'

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
