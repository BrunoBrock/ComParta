import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { setup, sharesSection, video } from './test/appHarness.tsx'
import { accessibilityProblems } from './test/axe.ts'

const welcome = () => screen.getByRole('dialog')

async function stored(bridge: ReturnType<typeof setup>['bridge']) {
  const result = await bridge.invoke('settings.get')
  if (!result.ok) throw new Error(result.error.code)
  return result.value
}

describe('the first-run welcome', () => {
  it('shows three cards, the choices and the network note, then stores them and never shows again', async () => {
    const { bridge, user } = setup({ welcomeDone: false, shares: [video] })

    expect(await screen.findByRole('dialog', { name: en['welcome.card1'] })).toBeInTheDocument()
    expect(within(welcome()).getByText('1 of 5')).toBeInTheDocument()
    expect(await accessibilityProblems(welcome())).toEqual([])
    await user.click(within(welcome()).getByRole('button', { name: en['welcome.next'] }))
    expect(screen.getByRole('dialog', { name: en['welcome.card2'] })).toBeInTheDocument()
    await user.click(within(welcome()).getByRole('button', { name: en['welcome.next'] }))
    expect(screen.getByRole('dialog', { name: en['welcome.card3'] })).toBeInTheDocument()
    await user.click(within(welcome()).getByRole('button', { name: en['welcome.next'] }))

    await user.type(within(welcome()).getByRole('textbox', { name: en['welcome.name'] }), 'Ana{Enter}')
    expect(within(welcome()).getByText(en['welcome.network'])).toBeInTheDocument()
    expect(within(welcome()).queryByRole('button', { name: en['welcome.skip'] })).not.toBeInTheDocument()
    await user.click(within(welcome()).getByRole('button', { name: en['welcome.start'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await waitFor(async () => expect((await stored(bridge)).welcomeDone).toBe(true))
    expect(await stored(bridge)).toMatchObject({ yourName: 'Ana', startWithWindows: true })
    // The chosen name appears in the invite message.
    await user.click(within(sharesSection()).getByRole('button', { name: 'Copy invite for Birthday party.mp4' }))
    expect(bridge.copiedInvite).toMatch(/^Ana wants to send you/)
  })

  it('goes straight to the app when skipped, and changes nothing else', async () => {
    const { bridge, user } = setup({ welcomeDone: false })

    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: en['welcome.skip'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await waitFor(async () => expect((await stored(bridge)).welcomeDone).toBe(true))
    expect(await stored(bridge)).toMatchObject({ yourName: null, startWithWindows: false })
  })

  it('keeps start with Windows off when the person turns it off', async () => {
    const { bridge, user } = setup({ welcomeDone: false })
    await screen.findByRole('dialog')
    for (let i = 0; i < 3; i++) await user.click(within(welcome()).getByRole('button', { name: en['welcome.next'] }))

    await user.click(within(welcome()).getByRole('checkbox', { name: en['settings.startWithWindows'] }))
    await user.click(within(welcome()).getByRole('button', { name: en['welcome.next'] }))
    await user.click(within(welcome()).getByRole('button', { name: en['welcome.start'] }))

    await waitFor(async () => expect((await stored(bridge)).welcomeDone).toBe(true))
    expect(await stored(bridge)).toMatchObject({ yourName: null, startWithWindows: false })
  })

  it('does not show once it was done', async () => {
    setup({ welcomeDone: true })
    await screen.findByText(en['shares.empty'])
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('reads in Brazilian Portuguese', async () => {
    setup({ welcomeDone: false, language: 'pt-BR' })
    expect(await screen.findByRole('dialog', { name: ptBR['welcome.card1'] })).toBeInTheDocument()
    expect(within(welcome()).getByRole('button', { name: ptBR['welcome.skip'] })).toBeInTheDocument()
  })
})
