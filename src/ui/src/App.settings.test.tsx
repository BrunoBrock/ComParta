import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import type { FakeBridge } from './bridge/fakeBridge.ts'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { railButton, setup, sharesSection, video } from './test/appHarness.tsx'
import { accessibilityProblems } from './test/axe.ts'

const settings = () => screen.getByRole('dialog', { name: en['settings.title'] })

async function openSettings(user: ReturnType<typeof setup>['user'], words: Record<keyof typeof en, string> = en) {
  await user.click(await railButton(words['nav.settings']))
  return screen.getByRole('dialog', { name: words['settings.title'] })
}

async function stored(bridge: FakeBridge) {
  const result = await bridge.invoke('settings.get')
  if (!result.ok) throw new Error(result.error.code)
  return result.value
}

/** The same fake, a new screen: what the app would show after a restart. */
function reopen(bridge: FakeBridge) {
  cleanup()
  render(<App bridge={bridge} />)
}

describe('the Settings page', () => {
  it('saves your name, and invites say it is from you', async () => {
    const { bridge, user } = setup({ shares: [video] })
    const dialog = await openSettings(user)

    await user.type(within(dialog).getByRole('textbox', { name: en['settings.name'] }), '  Ana  ')
    await user.click(within(dialog).getByRole('button', { name: en['settings.nameSave'] }))

    expect(await screen.findByText(en['settings.nameSaved'])).toBeInTheDocument()
    expect((await stored(bridge)).yourName).toBe('Ana')
    await user.click(within(dialog).getByRole('button', { name: en['settings.close'] }))
    await user.click(within(sharesSection()).getByRole('button', { name: 'Copy invite for Birthday party.mp4' }))
    expect(bridge.copiedInvite).toMatch(/^Ana wants to send you/)

    reopen(bridge)
    expect(await within(await openSettings(user)).findByRole('textbox', { name: en['settings.name'] })).toHaveValue('Ana')
  })

  it('says Someone in invites until a name is given, and again when it is cleared', async () => {
    const { bridge, user } = setup({ shares: [video], yourName: 'Ana' })
    const dialog = await openSettings(user)
    const name = within(dialog).getByRole('textbox', { name: en['settings.name'] })
    await waitFor(() => expect(name).toHaveValue('Ana'))

    await user.clear(name)
    await user.type(name, '{Enter}')

    await waitFor(async () => expect((await stored(bridge)).yourName).toBeNull())
    await user.click(within(dialog).getByRole('button', { name: en['settings.close'] }))
    await user.click(within(sharesSection()).getByRole('button', { name: 'Copy invite for Birthday party.mp4' }))
    expect(bridge.copiedInvite).toMatch(/^Someone wants to send you/)
  })

  it('refuses a name with characters that cannot be shown, and stores nothing', async () => {
    const { bridge, user } = setup()
    const dialog = await openSettings(user)
    const name = within(dialog).getByRole('textbox', { name: en['settings.name'] })

    await user.click(name)
    await user.paste('Ana\u0007')
    await user.click(within(dialog).getByRole('button', { name: en['settings.nameSave'] }))

    expect(within(dialog).getByRole('alert')).toHaveTextContent(en['settings.nameInvalid'])
    expect(name).toHaveAttribute('aria-invalid', 'true')
    expect((await stored(bridge)).yourName).toBeNull()
  })

  it('changes the colors at once, keeps them, and can follow Windows again', async () => {
    const { bridge, user } = setup()
    const dialog = await openSettings(user)
    expect(within(dialog).getByRole('radio', { name: en['settings.theme.system'] })).toBeChecked()
    expect(document.documentElement.dataset.theme).toBeUndefined()

    await user.click(within(dialog).getByRole('radio', { name: en['settings.theme.dark'] }))

    await waitFor(() => expect(document.documentElement.dataset.theme).toBe('dark'))
    expect((await stored(bridge)).theme).toBe('dark')
    reopen(bridge)
    const again = await openSettings(user)
    await waitFor(() => expect(within(again).getByRole('radio', { name: en['settings.theme.dark'] })).toBeChecked())

    await user.click(within(again).getByRole('radio', { name: en['settings.theme.system'] }))

    await waitFor(() => expect(document.documentElement.dataset.theme).toBeUndefined())
    expect((await stored(bridge)).theme).toBeNull()
  })

  it('turns the guided send off and on again', async () => {
    const { bridge, user } = setup({ sendStepsSeen: 1 })
    const dialog = await openSettings(user)
    const guided = within(dialog).getByRole('checkbox', { name: en['settings.guidedSend'] })
    await waitFor(() => expect(guided).toBeChecked())

    await user.click(guided)
    await waitFor(async () => expect((await stored(bridge)).sendStepsSeen).toBe(3))
    expect(guided).not.toBeChecked()
    await user.click(within(dialog).getByRole('button', { name: en['settings.close'] }))
    await user.click(await railButton(en['shares.add']))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await openSettings(user)
    await user.click(within(settings()).getByRole('checkbox', { name: en['settings.guidedSend'] }))
    await waitFor(async () => expect((await stored(bridge)).sendStepsSeen).toBe(0))
  })

  it('limits the speed of receiving and sending, keeps it, and can go back to no limit', async () => {
    const { bridge, user } = setup()
    const dialog = await openSettings(user)
    const speed = within(dialog).getByRole('group', { name: en['settings.speed'] })
    const receive = within(speed).getByRole('combobox', { name: en['settings.receiveLimit'] })
    const send = within(speed).getByRole('combobox', { name: en['settings.sendLimit'] })
    expect(receive).toHaveDisplayValue(en['settings.noLimit'])

    await user.selectOptions(receive, '1.0 MB/s')
    await user.selectOptions(send, '256 KB/s')

    await waitFor(async () => expect((await stored(bridge)).sendLimit).toBe(256 * 1024))
    expect((await stored(bridge)).receiveLimit).toBe(1024 ** 2)
    reopen(bridge)
    const again = within(await openSettings(user)).getByRole('combobox', { name: en['settings.receiveLimit'] })
    await waitFor(() => expect(again).toHaveDisplayValue('1.0 MB/s'))

    await user.selectOptions(again, en['settings.noLimit'])

    await waitFor(async () => expect((await stored(bridge)).receiveLimit).toBeNull())
    expect((await stored(bridge)).sendLimit).toBe(256 * 1024)
  })

  it('shows a limit that is not one of the choices as it is', async () => {
    const { user } = setup({ sendLimit: 300_000 })
    const send = within(await openSettings(user)).getByRole('combobox', { name: en['settings.sendLimit'] })
    await waitFor(() => expect(send).toHaveDisplayValue('293 KB/s'))
  })

  it('shows the version and that ComParta is open source, and passes the accessibility check', async () => {
    const { user } = setup()
    const dialog = await openSettings(user)

    const about = within(dialog).getByRole('region', { name: en['settings.about'] })
    expect(await within(about).findByText('Version 1.2.0')).toBeInTheDocument()
    expect(within(about).getByText(en['settings.openSource'])).toBeInTheDocument()
    expect(await accessibilityProblems(dialog)).toEqual([])
  })

  it('reads in Brazilian Portuguese', async () => {
    const { user } = setup({ language: 'pt-BR' })
    await screen.findByText(ptBR['shares.empty'])
    const dialog = await openSettings(user, ptBR)

    expect(within(dialog).getByRole('textbox', { name: ptBR['settings.name'] })).toBeInTheDocument()
    expect(within(dialog).getByRole('radio', { name: ptBR['settings.theme.light'] })).toBeInTheDocument()
    expect(within(dialog).getByText('Versão 1.2.0')).toBeInTheDocument()
  })

  it('refuses in the fake bridge what core refuses', async () => {
    const { bridge } = setup()
    const { systemLanguage: _, version: __, ...base } = await stored(bridge)
    for (const change of [
      { yourName: 'x'.repeat(41) },
      { yourName: 'line\nbreak' },
      { theme: 'blue' },
      { receiveLimit: 9_999 },
      { sendLimit: 1.5e9 },
      { sendLimit: 20_000.5 },
    ]) {
      expect((await bridge.invoke('settings.set', { ...base, ...change })).ok).toBe(false)
    }
    expect((await bridge.invoke('settings.set', { ...base, yourName: 'x'.repeat(40), theme: 'light' })).ok).toBe(true)
  })
})
