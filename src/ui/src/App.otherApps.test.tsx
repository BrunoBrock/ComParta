import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { setup, video } from './test/appHarness.tsx'

type Words = Record<keyof typeof en, string>

async function openDetails(user: ReturnType<typeof setup>['user'], words: Words) {
  await user.click(await screen.findByRole('button', { name: words['details.openFor'].replace('{name}', video.name) }))
  return screen.getByRole('dialog', { name: words['details.title'].replace('{name}', video.name) })
}

describe('a shared file for other apps', () => {
  it('copies the link alone, so other apps open it', async () => {
    const { bridge, user } = setup({ shares: [video] })
    await openDetails(user, en)

    await user.click(await screen.findByRole('button', { name: en['shares.copyLink'] }))

    expect(bridge.copiedInvite).toMatch(/^[^\s]+$/)
    expect(bridge.copiedInvite).toContain(encodeURIComponent(video.name))
    expect(await screen.findByText(en['shares.linkCopied'].replace('{name}', video.name))).toBeInTheDocument()
  })

  it('saves the download file and says where it is, in Portuguese too', async () => {
    const { bridge, user } = setup({ language: 'pt-BR', shares: [video] })
    await openDetails(user, ptBR)

    await user.click(await screen.findByRole('button', { name: ptBR['shares.saveDownloadFile'] }))

    expect(bridge.savedDownloadFiles).toEqual([video.id])
    expect(await screen.findByText(ptBR['shares.downloadFileSaved'].replace('{name}', video.name))).toBeInTheDocument()
  })

  it('says so when the download file cannot be saved', async () => {
    const { bridge, user } = setup({ shares: [video] })
    await openDetails(user, en)

    bridge.failNextWith = 'file.saveFailed'
    await user.click(await screen.findByRole('button', { name: en['shares.saveDownloadFile'] }))

    expect(await screen.findByText(en['error.file.saveFailed'])).toBeInTheDocument()
    expect(bridge.savedDownloadFiles).toEqual([])
  })

  it('offers neither while the file is not ready', async () => {
    const { user } = setup({ shares: [{ ...video, status: 'preparing' }] })
    await openDetails(user, en)

    await screen.findByText(en['details.size'])
    expect(screen.queryByRole('button', { name: en['shares.copyLink'] })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: en['shares.saveDownloadFile'] })).not.toBeInTheDocument()
  })
})
