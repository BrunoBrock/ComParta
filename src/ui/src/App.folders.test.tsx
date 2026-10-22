import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { railButton, setup, sharesSection } from './test/appHarness.tsx'
import { accessibilityProblems } from './test/axe.ts'

const party = [
  { name: 'a.jpg', sizeBytes: 1_000 },
  { name: 'b.jpg', sizeBytes: 2_000 },
  { name: 'c.mp4', sizeBytes: 3_000 },
]

const naming = () => screen.getByRole('dialog', { name: 'Name these 3 files' })

describe('sharing a folder or several files', () => {
  it('asks for a name when several files are chosen, then shares them as one', async () => {
    const { bridge, user } = setup()
    bridge.nextPick = party

    await user.click(await railButton(en['shares.add']))

    const box = within(await waitFor(naming)).getByRole('textbox', { name: en['shares.nameLabel'] })
    expect(box).toHaveValue('3 files')
    expect(await accessibilityProblems(naming())).toEqual([])
    await user.clear(box)
    await user.type(box, 'Party{Enter}')

    const item = (await within(sharesSection()).findByText('Party')).closest('li')!
    expect(within(item).getByText(/3 files/)).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    const listed = await bridge.invoke('shares.list')
    expect(listed.ok && listed.value.map((s) => [s.name, s.kind, s.itemCount])).toEqual([['Party', 'collection', 3]])
  })

  it('says which characters a name cannot have, and keeps the dialog open', async () => {
    const { bridge, user } = setup()
    bridge.nextPick = party
    await user.click(await railButton(en['shares.add']))
    const box = within(await waitFor(naming)).getByRole('textbox', { name: en['shares.nameLabel'] })

    await user.clear(box)
    await user.type(box, 'Party: 2026')
    await user.click(within(naming()).getByRole('button', { name: en['shares.nameShare'] }))

    expect(within(naming()).getByRole('alert')).toHaveTextContent(en['error.share.nameInvalid'])
    expect(box).toHaveAttribute('aria-invalid', 'true')
    const listed = await bridge.invoke('shares.list')
    expect(listed.ok && listed.value).toEqual([])
  })

  it('shares nothing when the name is cancelled', async () => {
    const { bridge, user } = setup()
    bridge.nextPick = party
    await user.click(await railButton(en['shares.add']))

    await user.click(within(await waitFor(naming)).getByRole('button', { name: en['shares.nameCancel'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
    expect((await bridge.invoke('shares.addPicked', { name: 'Late' })).ok && 'nothing waited').toBe('nothing waited')
    const listed = await bridge.invoke('shares.list')
    expect(listed.ok && listed.value).toEqual([])
  })

  it('shares a folder chosen in the folder dialog, with its files counted and a folder picture', async () => {
    const { bridge, user } = setup()
    bridge.nextFolder = { name: 'Birthday', sizeBytes: 5 * 1024 ** 2, itemCount: 200 }

    await user.click(await railButton(en['shares.addFolder']))

    const item = (await within(sharesSection()).findByText('Birthday')).closest('li')!
    expect(within(item).getByText(/· 200 files/)).toBeInTheDocument()
    expect(item.querySelector('.lucide-folder')).not.toBeNull()
    expect(within(item).getByRole('button', { name: 'Copy invite for Birthday' })).toBeInTheDocument()
  })

  it('says when the folder chosen has nothing in it, and does nothing when cancelled', async () => {
    const { bridge, user } = setup()
    bridge.nextFolder = { name: 'Empty', sizeBytes: 0, itemCount: 0 }
    await user.click(await railButton(en['shares.addFolder']))
    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.share.folderEmpty'])

    bridge.nextFolder = null
    await user.click(await railButton(en['shares.addFolder']))
    expect(screen.getByText(en['shares.empty'])).toBeInTheDocument()
  })

  it('names the files in the guided send without asking', async () => {
    const { bridge, user } = setup({ sendStepsSeen: 0 })
    bridge.nextPick = party

    await user.click(await railButton(en['shares.add']))

    expect(await screen.findByText('Step 3 of 3')).toBeInTheDocument()
    const listed = await bridge.invoke('shares.list')
    expect(listed.ok && listed.value.map((s) => s.name)).toEqual(['3 files'])
  })

  it('reads in Brazilian Portuguese', async () => {
    const { bridge, user } = setup({ language: 'pt-BR' })
    bridge.nextPick = party
    await screen.findByText(ptBR['shares.empty'])

    await user.click(await railButton(ptBR['shares.add']))

    const dialog = await screen.findByRole('dialog', { name: 'Dê um nome a estes 3 arquivos' })
    expect(within(dialog).getByRole('textbox', { name: ptBR['shares.nameLabel'] })).toHaveValue('3 arquivos')
    expect(within(await screen.findByRole('complementary')).getByRole('button', { name: ptBR['shares.addFolder'] })).toBeInTheDocument()
  })

  it("shows up to four of a folder's photos on its card, or the folder icon when it has none", async () => {
    const { bridge, user } = setup()
    bridge.nextFolder = { name: 'Birthday', sizeBytes: 9_000, itemCount: 9, pictures: 6 }
    await user.click(await railButton(en['shares.addFolder']))
    bridge.nextFolder = { name: 'Letters', sizeBytes: 900, itemCount: 3, pictures: 0 }
    await user.click(await railButton(en['shares.addFolder']))

    const birthday = (await within(sharesSection()).findByText('Birthday')).closest('li')!
    await waitFor(() => expect(birthday.querySelectorAll('.folder-pictures img')).toHaveLength(4))
    const letters = within(sharesSection()).getByText('Letters').closest('li')!
    expect(letters.querySelector('img')).toBeNull()
    expect(letters.querySelector('.lucide-folder')).not.toBeNull()
    expect(await accessibilityProblems(sharesSection())).toEqual([])
  })

  it('says in the details how many files a folder has and that later ones are not included', async () => {
    const { bridge, user } = setup()
    bridge.nextFolder = { name: 'Birthday', sizeBytes: 9_000, itemCount: 9, pictures: 6 }
    await user.click(await railButton(en['shares.addFolder']))

    await user.click(await screen.findByRole('button', { name: 'Details of Birthday' }))

    const details = screen.getByRole('dialog', { name: /Birthday/ })
    expect(await within(details).findByText(en['details.files'])).toBeInTheDocument()
    expect(within(details).getByText('9')).toBeInTheDocument()
    expect(within(details).getByText(en['details.folderSnapshot'])).toBeInTheDocument()
    expect(await accessibilityProblems(details)).toEqual([])
  })

  it('says in Brazilian Portuguese that files added later are not included', async () => {
    const { bridge, user } = setup({ language: 'pt-BR' })
    bridge.nextFolder = { name: 'Aniversário', sizeBytes: 9_000, itemCount: 9 }
    await screen.findByText(ptBR['shares.empty'])
    await user.click(await railButton(ptBR['shares.addFolder']))

    await user.click(await screen.findByRole('button', { name: ptBR['details.openFor'].replace('{name}', 'Aniversário') }))

    expect(await screen.findByText(ptBR['details.folderSnapshot'])).toBeInTheDocument()
  })
})
