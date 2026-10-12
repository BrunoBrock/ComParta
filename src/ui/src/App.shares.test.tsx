import { act, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import {
  video,
  album,
  setup,
  sharesSection,
} from './test/appHarness.tsx'

describe('the list of my shares', () => {
  it('shows the empty-state message when nothing is shared', async () => {
    setup()

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
  })

  it('shows each share with its name and size', async () => {
    setup({ shares: [video] })

    const item = (await within(sharesSection()).findByText('Birthday party.mp4')).closest('li')!
    expect(within(item).getByText('1.20 GB')).toBeInTheDocument()
    expect(within(item).getByText(en['shares.chip.ready'])).toBeInTheDocument()
    expect(screen.queryByText(en['shares.empty'])).not.toBeInTheDocument()
  })

  it('marks a share whose file went missing', async () => {
    setup({ shares: [video, album] })

    const item = (await within(sharesSection()).findByText('Holiday photos.zip')).closest('li')!
    expect(within(item).getByText(en['shares.chip.unavailable'])).toBeInTheDocument()
    expect(within(item).getByText(en['shares.status.unavailable'])).toBeInTheDocument()
    expect(within(item).queryByRole('button', { name: /copy invite/i })).not.toBeInTheDocument()
  })

  it('updates from a change event without reloading', async () => {
    const { bridge } = setup()
    await screen.findByText(en['shares.empty'])

    act(() => bridge.setShares([video]))

    expect(await screen.findByText('Birthday party.mp4')).toBeInTheDocument()
  })
})

describe('adding a file', () => {
  it('shows the new share', async () => {
    const { bridge, user } = setup()
    bridge.nextPick = { name: 'Recipe.pdf', sizeBytes: 482_113 }

    await user.click(await screen.findByRole('button', { name: en['shares.add'] }))

    expect(await screen.findByText('Recipe.pdf')).toBeInTheDocument()
    expect(screen.getByText('471 KB')).toBeInTheDocument()
  })

  it('shows a plain message for a duplicate', async () => {
    const { bridge, user } = setup({ shares: [video] })
    bridge.nextPick = { name: video.name, sizeBytes: video.sizeBytes }

    await user.click(await screen.findByRole('button', { name: en['shares.add'] }))

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.share.duplicate'])
    expect(within(sharesSection()).getAllByRole('listitem')).toHaveLength(1)
  })

  it('shows a plain message for a file that cannot be read', async () => {
    const { bridge, user } = setup()
    bridge.nextPick = { name: 'Empty.txt', sizeBytes: 0 }

    await user.click(await screen.findByRole('button', { name: en['shares.add'] }))

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.file.unreadable'])
  })

  it('changes nothing and says nothing when the person cancels', async () => {
    const { bridge, user } = setup()
    bridge.nextPick = null

    await user.click(await screen.findByRole('button', { name: en['shares.add'] }))

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('removing a share', () => {
  it('asks first, says the original is kept, and removes on confirm', async () => {
    const { user } = setup({ shares: [video] })

    await user.click(await screen.findByRole('button', { name: 'Stop sharing Birthday party.mp4' }))
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveTextContent('The file stays on your computer')
    await user.click(within(dialog).getByRole('button', { name: en['remove.confirm'] }))

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('keeps the share when the person declines', async () => {
    const { user } = setup({ shares: [video] })

    await user.click(await screen.findByRole('button', { name: 'Stop sharing Birthday party.mp4' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: en['remove.cancel'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByText('Birthday party.mp4')).toBeInTheDocument()
  })

  it('can remove a share whose file went missing', async () => {
    const { user } = setup({ shares: [album] })

    await user.click(await screen.findByRole('button', { name: 'Stop sharing Holiday photos.zip' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: en['remove.confirm'] }))

    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
  })
})

describe('sending to another person', () => {
  it('copies the invite and explains what to do with it', async () => {
    const { bridge, user } = setup({ shares: [video] })

    await user.click(await screen.findByRole('button', { name: 'Copy invite for Birthday party.mp4' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Paste it into WhatsApp or an email')
    expect(bridge.copiedInvite).toBe(
      'Someone wants to send you “Birthday party.mp4” (1.20 GB) with ComParta.\n' +
        '1. Get ComParta: https://github.com/BrunoBrock/ComParta/releases\n' +
        '2. Open this invite in ComParta: comparta://receive/v1/s1',
    )
    expect(screen.getByRole('button', { name: 'Copy invite for Birthday party.mp4' })).toHaveTextContent(en['shares.copied'])
  })
})
