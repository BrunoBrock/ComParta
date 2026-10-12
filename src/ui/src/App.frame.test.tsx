import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { accessibilityProblems } from './test/axe.ts'
import { setup, transfersSection } from './test/appHarness.tsx'

const header = () => screen.getByRole('banner')
const rail = () => screen.getByRole('complementary')

describe('the frame around the files', () => {
  it('opens Help from the header and from the rail, and leads on to the connection help', async () => {
    const { user } = setup()
    await screen.findByText(en['shares.empty'])

    await user.click(within(header()).getByRole('button', { name: en['nav.help'] }))
    const help = screen.getByRole('dialog', { name: en['help.title'] })
    expect(within(help).getAllByRole('listitem')).toHaveLength(3)
    expect(await accessibilityProblems()).toEqual([])
    await user.click(within(help).getByRole('button', { name: en['help.close'] }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await user.click(within(rail()).getByRole('button', { name: en['nav.help'] }))
    await user.click(screen.getByRole('button', { name: en['connection.title'] }))
    expect(screen.getByRole('dialog', { name: en['connection.title'] })).toBeInTheDocument()
  })

  it('changes the language in Settings', async () => {
    const { user } = setup()
    await screen.findByText(en['shares.empty'])

    await user.click(within(rail()).getByRole('button', { name: en['nav.settings'] }))
    await user.selectOptions(screen.getByRole('combobox', { name: en['language.label'] }), 'pt-BR')

    expect(await screen.findByRole('heading', { name: 'Configurações' })).toBeInTheDocument()
  })

  it('offers both actions from the floating button', async () => {
    const { user } = setup()
    await screen.findByText(en['shares.empty'])
    const fab = screen.getByRole('button', { name: en['fab.open'] })

    await user.click(fab)
    const menu = screen.getByRole('menu', { name: en['fab.open'] })
    await user.click(within(menu).getByRole('menuitem', { name: en['shares.add'] }))
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(await screen.findByText('Birthday party.mp4')).toBeInTheDocument()

    await user.click(fab)
    await user.click(screen.getByRole('menuitem', { name: en['transfers.open'] }))
    expect(screen.getByRole('dialog', { name: en['receive.title'] })).toBeInTheDocument()
  })

  it('moves through the floating menu with the keyboard', async () => {
    const { user } = setup()
    await screen.findByText(en['shares.empty'])
    const fab = screen.getByRole('button', { name: en['fab.open'] })

    await user.click(fab)
    const items = screen.getAllByRole('menuitem')
    expect(items[0]).toHaveFocus()
    await user.keyboard('{ArrowDown}')
    expect(items[1]).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(fab).toHaveFocus()
  })

  it('keeps both sections reachable', async () => {
    const { user } = setup()
    await screen.findByText(en['shares.empty'])

    await user.click(within(rail()).getByRole('button', { name: new RegExp(`^${en['nav.received']}`) }))

    expect(transfersSection()).toBeInTheDocument()
  })
})
