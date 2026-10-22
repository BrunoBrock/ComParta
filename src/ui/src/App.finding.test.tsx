import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import type { ShareDto, TransferDto } from './bridge/contract.generated.ts'
import { createFakeBridge, notSending } from './bridge/fakeBridge.ts'
import { en } from './i18n/en.ts'
import { setLanguage } from './i18n/index.ts'
import { ptBR } from './i18n/pt-BR.ts'
import {
  video,
  album,
  setup,
  sharesSection,
  transfersSection,
  navButton,
  goToReceived,
} from './test/appHarness.tsx'

describe('finding my files', () => {
  const recipe: ShareDto = { id: 's3', name: 'Receita de aniversário.pdf', sizeBytes: 482_113, status: 'ready', ...notSending }

  it('counts the files in each place', async () => {
    setup({ shares: [video, album], transfers: [] })

    expect(await screen.findByText('Birthday party.mp4')).toBeInTheDocument()
    expect(navButton(en['nav.shared'])).toHaveAccessibleName(`${en['nav.shared']} 2`)
    expect(navButton(en['nav.shared'])).toHaveAttribute('aria-current', 'page')
  })

  it('finds a file by name without caring about accents or capitals', async () => {
    const { user } = setup({ shares: [video, recipe] })
    await screen.findByText('Birthday party.mp4')

    await user.type(screen.getByRole('searchbox', { name: en['search.label'] }), 'ANIVERSARIO')

    expect(within(sharesSection()).getAllByRole('listitem')).toHaveLength(1)
    expect(screen.getByText('Receita de aniversário.pdf')).toBeInTheDocument()
  })

  it('says so when nothing matches and clears the search', async () => {
    const { user } = setup({ shares: [video] })
    await screen.findByText('Birthday party.mp4')

    await user.type(screen.getByRole('searchbox', { name: en['search.label'] }), 'tax')

    expect(screen.getByText('Nothing here is called “tax”.')).toBeInTheDocument()
    await user.click(within(sharesSection()).getByRole('button', { name: en['search.clear'] }))
    expect(screen.getByText('Birthday party.mp4')).toBeInTheDocument()
  })

  it('shows files as a grid or a list and remembers the choice', async () => {
    const { bridge, user } = setup({ shares: [video] })
    await screen.findByText('Birthday party.mp4')
    expect(within(sharesSection()).getByRole('list')).toHaveClass('items-grid')

    await user.click(screen.getByRole('button', { name: en['view.list'] }))

    expect(within(sharesSection()).getByRole('list')).toHaveClass('items-list')
    expect(screen.getByRole('button', { name: en['view.list'] })).toHaveAttribute('aria-pressed', 'true')
    const stored = await bridge.invoke('settings.get')
    expect(stored.ok && stored.value.view).toBe('list')
  })

  it('opens in the view and order stored last time', async () => {
    setup({ shares: [video, album], view: 'list', sort: 'name' })

    expect(await screen.findByText('Birthday party.mp4')).toBeInTheDocument()
    expect(within(sharesSection()).getByRole('list')).toHaveClass('items-list')
    expect(screen.getByRole('button', { name: 'Order: Name' })).toBeInTheDocument()
  })

  it('orders files by name, by size, or newest first, and stores the choice', async () => {
    const small: ShareDto = { id: 's3', name: 'photo 10.jpg', sizeBytes: 5, status: 'ready', ...notSending }
    const big: ShareDto = { id: 's4', name: 'Photo 2.jpg', sizeBytes: 500, status: 'ready', ...notSending }
    const middle: ShareDto = { id: 's5', name: 'Árvore.png', sizeBytes: 50, status: 'ready', ...notSending }
    const { bridge, user } = setup({ shares: [small, big, middle] })
    await screen.findByText(small.name)
    const names = () => within(sharesSection()).getAllByRole('listitem').map((item) => item.querySelector('.item-name')?.getAttribute('title'))
    expect(names()).toEqual([middle.name, big.name, small.name])

    await user.click(screen.getByRole('button', { name: 'Order: Newest first' }))
    const menu = screen.getByRole('menu', { name: en['sort.menu'] })
    expect(within(menu).getByRole('menuitemradio', { name: en['sort.added'] })).toHaveAttribute('aria-checked', 'true')
    await user.click(within(menu).getByRole('menuitemradio', { name: en['sort.name'] }))
    expect(names()).toEqual([middle.name, big.name, small.name])

    await user.click(screen.getByRole('button', { name: 'Order: Name' }))
    await user.click(screen.getByRole('menuitemradio', { name: en['sort.size'] }))
    expect(names()).toEqual([big.name, middle.name, small.name])

    await user.click(screen.getByRole('button', { name: en['view.list'] }))
    const stored = await bridge.invoke('settings.get')
    expect(stored.ok && [stored.value.sort, stored.value.view]).toEqual(['size', 'list'])
  })

  it('puts files still on their way above finished ones', async () => {
    const done: TransferDto = { id: 't2', name: 'Old photo.jpg', sizeBytes: 10, receivedBytes: 10, state: 'done', bytesPerSecond: 0, secondsLeft: null, neededBytes: null, risky: false }
    const coming: TransferDto = { id: 't3', name: 'New video.mp4', sizeBytes: 100, receivedBytes: 40, state: 'receiving', bytesPerSecond: 0, secondsLeft: null, neededBytes: null, risky: false }
    const { user } = setup({ transfers: [done, coming] })
    await goToReceived(user)

    const items = await within(transfersSection()).findAllByRole('listitem')
    expect(items[0]).toHaveTextContent('New video.mp4')
    expect(items[1]).toHaveTextContent('Old photo.jpg')
  })
})

describe('the language', () => {
  it('starts in Portuguese on a computer set to Brazilian Portuguese', async () => {
    setup({ systemLanguage: 'pt-BR' })

    expect(await screen.findByText(ptBR['shares.empty'])).toBeInTheDocument()
    expect(document.documentElement.lang).toBe('pt-BR')
  })

  it('starts in English on a computer set to a language ComParta does not speak', async () => {
    setup({ systemLanguage: 'fr-FR' })

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
  })

  it('keeps the chosen language after the app starts again', async () => {
    const bridge = createFakeBridge({ systemLanguage: 'pt-BR' })
    const user = userEvent.setup()
    const first = render(<App bridge={bridge} />)
    await screen.findByText(ptBR['shares.empty'])

    await user.click(within(screen.getByRole('banner')).getByRole('button', { name: ptBR['nav.settings'] }))
    await user.selectOptions(screen.getByRole('combobox', { name: ptBR['language.label'] }), 'en')
    await user.click(screen.getByRole('button', { name: en['settings.close'] }))

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
    first.unmount()
    setLanguage('pt-BR')
    render(<App bridge={bridge} />)
    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
    await user.click(within(screen.getByRole('banner')).getByRole('button', { name: en['nav.settings'] }))
    expect(screen.getByRole('combobox', { name: en['language.label'] })).toHaveValue('en')
  })
})
