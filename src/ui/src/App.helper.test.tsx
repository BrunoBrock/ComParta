import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { setup } from './test/appHarness.tsx'

const rail = () => screen.getByRole('complementary')
const address = 'https://helper.example.org:6970'
const key = 'a-helper-key-for-the-family'

async function openSettings(user: ReturnType<typeof setup>['user'], words: Record<keyof typeof en, string> = en) {
  await user.click(within(rail()).getByRole('button', { name: words['nav.settings'] }))
  return {
    address: screen.getByLabelText(words['settings.helperAddress']),
    key: screen.getByLabelText(words['settings.helperKey']),
    save: screen.getByRole('button', { name: words['settings.helperSave'] }),
  }
}

describe('the helper computer in Settings', () => {
  it('saves the helper, and the connection then says others get the files through it', async () => {
    const { bridge, user } = setup({ connection: 'blocked' })
    await screen.findByText(en['shares.empty'])
    expect(await within(rail()).findByText(en['connection.blocked'])).toBeInTheDocument()
    const fields = await openSettings(user)

    await user.type(fields.address, ` ${address} `)
    await user.type(fields.key, key)
    await user.click(fields.save)

    const stored = await bridge.invoke('settings.get')
    expect(stored.ok && [stored.value.helperAddress, stored.value.helperKey]).toEqual([address, key])
    expect(await screen.findByText(en['settings.helperSaved'])).toBeInTheDocument()
    expect(fields.address).toHaveValue(address)
    await user.click(screen.getByRole('button', { name: en['settings.close'] }))

    const indicator = await within(rail()).findByRole('button', {
      name: en['connection.open'].replace('{state}', en['connection.helper']),
    })
    await user.click(indicator)
    expect(screen.getByText(en['connection.state.helper'])).toBeInTheDocument()
  })

  it('says what is wrong and stores nothing when the key is too short', async () => {
    const { bridge, user } = setup()
    await screen.findByText(en['shares.empty'])
    const fields = await openSettings(user)

    await user.type(fields.address, address)
    await user.type(fields.key, 'short')
    await user.click(fields.save)

    expect(screen.getByRole('alert')).toHaveTextContent(en['settings.helperInvalid'])
    expect(fields.key).toHaveAttribute('aria-invalid', 'true')
    const stored = await bridge.invoke('settings.get')
    expect(stored.ok && stored.value.helperAddress).toBeNull()
  })

  it('shows the helper chosen before, keeps it when another choice changes, and turns it off when emptied', async () => {
    const { bridge, user } = setup({ language: 'pt-BR', helper: { address, key }, connection: 'blocked' })
    await screen.findByText(ptBR['shares.empty'])
    expect(await within(rail()).findByText(ptBR['connection.helper'])).toBeInTheDocument()
    const fields = await openSettings(user, ptBR)
    expect(fields.address).toHaveValue(address)
    expect(fields.key).toHaveValue(key)

    await user.click(screen.getByRole('checkbox', { name: ptBR['settings.startWithWindows'] }))
    let stored = await bridge.invoke('settings.get')
    expect(stored.ok && stored.value.helperKey).toBe(key)

    await user.clear(fields.address)
    await user.clear(fields.key)
    await user.click(fields.save)

    expect(await screen.findByText(ptBR['settings.helperOff'])).toBeInTheDocument()
    stored = await bridge.invoke('settings.get')
    expect(stored.ok && [stored.value.helperAddress, stored.value.helperKey]).toEqual([null, null])
    expect(await within(rail()).findByText(ptBR['connection.blocked'])).toBeInTheDocument()
  })

  it('refuses in the fake bridge what core refuses', async () => {
    const { bridge } = setup()
    const base = { language: null, view: null, sort: null, sendStepsSeen: 0, startWithWindows: false, publicServers: [], yourName: null, theme: null, welcomeDone: null }
    for (const [badAddress, badKey] of [
      [address, null],
      [null, key],
      ['ftp://helper.example.org', key],
      ['https://user:secret@helper.example.org', key],
      [address, 'has a space in the key'],
      [address, 'short'],
    ]) {
      const result = await bridge.invoke('settings.set', { ...base, helperAddress: badAddress, helperKey: badKey })
      expect(result.ok).toBe(false)
    }
    expect((await bridge.invoke('settings.set', { ...base, helperAddress: address, helperKey: key })).ok).toBe(true)
  })
})
