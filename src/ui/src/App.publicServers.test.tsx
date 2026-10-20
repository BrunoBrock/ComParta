import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { setup } from './test/appHarness.tsx'

const rail = () => screen.getByRole('complementary')
const server = 'udp://open.example.org:1337/announce'

async function openSettings(user: ReturnType<typeof setup>['user'], words: Record<keyof typeof en, string> = en) {
  await user.click(within(rail()).getByRole('button', { name: words['nav.settings'] }))
  return screen.getByRole('textbox', { name: words['settings.servers'] })
}

describe('public servers in Settings', () => {
  it('saves the servers typed, one per line, without blanks or repeats', async () => {
    const { bridge, user } = setup()
    await screen.findByText(en['shares.empty'])
    const box = await openSettings(user)

    await user.type(box, ` ${server}{Enter}{Enter}https://example.net/announce{Enter}${server}`)
    await user.click(screen.getByRole('button', { name: en['settings.serversSave'] }))

    const stored = await bridge.invoke('settings.get')
    expect(stored.ok && stored.value.publicServers).toEqual([server, 'https://example.net/announce'])
    expect(await screen.findByText(en['settings.serversSaved'])).toBeInTheDocument()
    expect(box).toHaveValue(`${server}\nhttps://example.net/announce`)
  })

  it('names the line that is not an address and stores nothing', async () => {
    const { bridge, user } = setup()
    await screen.findByText(en['shares.empty'])
    const box = await openSettings(user)

    await user.type(box, `${server}{Enter}open.example.org`)
    await user.click(screen.getByRole('button', { name: en['settings.serversSave'] }))

    expect(screen.getByRole('alert')).toHaveTextContent(
      en['settings.serversInvalid'].replace('{line}', 'open.example.org'),
    )
    expect(box).toHaveAttribute('aria-invalid', 'true')
    const stored = await bridge.invoke('settings.get')
    expect(stored.ok && stored.value.publicServers).toEqual([])
  })

  it('shows the servers chosen before, and keeps them when another choice changes', async () => {
    const { bridge, user } = setup({ language: 'pt-BR', publicServers: [server] })
    await screen.findByText(ptBR['shares.empty'])

    expect(await openSettings(user, ptBR)).toHaveValue(server)
    await user.click(screen.getByRole('checkbox', { name: ptBR['settings.startWithWindows'] }))

    const stored = await bridge.invoke('settings.get')
    expect(stored.ok && stored.value.publicServers).toEqual([server])
  })

  it('refuses in the fake bridge what core refuses', async () => {
    const { bridge } = setup()
    const base = {
      language: null,
      view: null,
      sort: null,
      sendStepsSeen: 0,
      startWithWindows: false,
      helperAddress: null,
      helperKey: null, yourName: null, theme: null, welcomeDone: null
    }
    for (const bad of [
      'ftp://example.org/x',
      'udp://example.org',
      'https://user:secret@example.org/a',
      `${server}\nudp://b.example.org:80`,
    ]) {
      const result = await bridge.invoke('settings.set', { ...base, publicServers: [bad] })
      expect(result.ok).toBe(false)
    }
    const many = Array.from({ length: 21 }, (_, i) => `udp://s${i}.example.org:80`)
    expect((await bridge.invoke('settings.set', { ...base, publicServers: many })).ok).toBe(false)
  })
})
