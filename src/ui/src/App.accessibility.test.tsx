import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { TransferDto } from './bridge/contract.generated.ts'
import { en } from './i18n/en.ts'
import { accessibilityProblems } from './test/axe.ts'
import { album, goToReceived, setup, video } from './test/appHarness.tsx'

const arriving: TransferDto = {
  id: 't1',
  name: 'Open film.mkv',
  sizeBytes: 1000,
  receivedBytes: 400,
  state: 'receiving',
  bytesPerSecond: 100,
  secondsLeft: 6,
  neededBytes: null,
  risky: false, folder: false,
}

describe('the screen for assistive technology', () => {
  it('has no accessibility problem with files shared', async () => {
    setup({ shares: [video, album] })
    await screen.findByText(video.name)

    expect(await accessibilityProblems()).toEqual([])
  })

  it('has no accessibility problem with files arriving, stopped, and in a window', async () => {
    const { user } = setup({ transfers: [arriving], stopped: { receivingStopped: true } })
    await goToReceived(user)
    await screen.findByText(en['stopped.note'])
    expect(await accessibilityProblems()).toEqual([])

    await user.click(screen.getByRole('button', { name: `Details of ${arriving.name}` }))
    await screen.findByText(en['details.source.invite'])
    expect(await accessibilityProblems()).toEqual([])
  })
})
