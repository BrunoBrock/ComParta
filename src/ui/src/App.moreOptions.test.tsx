import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import { createFakeBridge, sampleFakeDownloadLink, sampleFakeInvite } from './bridge/fakeBridge.ts'
import type { FakeBridge, PickedFile } from './bridge/fakeBridge.ts'
import { en } from './i18n/en.ts'
import { formatSize } from './i18n/index.ts'
import { railButton, setup, transfersSection, type User, pasteInvite } from './test/appHarness.tsx'

const collection: PickedFile = {
  name: 'Nature films',
  sizeBytes: 1_600_000_000,
  files: [
    { name: 'Forest.mkv', sizeBytes: 1_000_000_000 },
    { name: 'Extras/Making of.mkv', sizeBytes: 500_000_000 },
    { name: 'Subtitles.srt', sizeBytes: 100_000_000 },
  ],
}

async function openDownloadFile(user: User, bridge: FakeBridge, file: PickedFile) {
  bridge.nextDownloadFile = file
  await user.click(await railButton(en['transfers.open']))
  await user.click(screen.getByRole('button', { name: en['receive.openFile'] }))
  await within(transfersSection()).findByText(file.name)
}

async function openMoreOptions(user: User, name: string) {
  await user.click(await screen.findByRole('button', { name: en['details.openFor'].replace('{name}', name) }))
  await user.click(await screen.findByRole('button', { name: en['options.open'] }))
  return screen.findByRole('dialog', { name: en['options.title'].replace('{name}', name) })
}

describe('More options on a download (epic 14 story 4)', () => {
  it('lists the files of a download and leaves out the ones the person unticks', async () => {
    const { bridge, user } = setup()
    await openDownloadFile(user, bridge, collection)

    const options = await openMoreOptions(user, 'Nature films')
    const extras = await within(options).findByRole('checkbox', { name: /Extras\/Making of\.mkv/ })
    expect(within(options).getAllByRole('checkbox', { checked: true })).toHaveLength(4)

    await user.click(extras)

    expect(await within(options).findByRole('checkbox', { name: /Extras\/Making of\.mkv/ })).not.toBeChecked()
    // Only the files it gets count, so the card shows the smaller size.
    const result = await bridge.invoke('transfers.list')
    expect(result.ok && result.value[0].sizeBytes).toBe(1_100_000_000)
    expect(await within(transfersSection()).findByText(formatSize(1_100_000_000))).toBeInTheDocument()
  })

  it('keeps the last ticked file ticked', async () => {
    const { bridge, user } = setup()
    await openDownloadFile(user, bridge, collection)
    const options = await openMoreOptions(user, 'Nature films')

    await user.click(await within(options).findByRole('checkbox', { name: /Forest\.mkv/ }))
    await user.click(await within(options).findByRole('checkbox', { name: /Making of\.mkv/ }))

    expect(await within(options).findByRole('checkbox', { name: /Subtitles\.srt/ })).toBeDisabled()
    expect(await bridge.invoke('transfers.setOptions', { id: 'file-Nature films', skipped: [0, 1, 2], keepSharing: null })).toEqual({
      ok: false,
      error: { code: 'transfer.noFiles' },
    })
  })

  it('says the files show once the download is found', async () => {
    const { user } = setup()
    await pasteInvite(user, sampleFakeDownloadLink)

    const options = await openMoreOptions(user, 'Open film.mkv')

    expect(await within(options).findByText(en['options.filesWaiting'])).toBeInTheDocument()
  })

  it('keeps sharing a download once it arrives unless the person turns it off', async () => {
    const { bridge, user } = setup()
    await openDownloadFile(user, bridge, collection)
    const options = await openMoreOptions(user, 'Nature films')

    const keep = await within(options).findByRole('checkbox', { name: en['options.keepSharing'] })
    expect(keep).toBeChecked()
    await user.click(keep)

    expect(await within(options).findByRole('checkbox', { name: en['options.keepSharing'] })).not.toBeChecked()
    const stored = await bridge.invoke('transfers.options', { id: 'file-Nature films' })
    expect(stored.ok && stored.value.keepSharing).toBe(false)
  })

  it('stops sharing a download that arrived', async () => {
    const { bridge, user } = setup()
    await openDownloadFile(user, bridge, collection)
    const listed = await bridge.invoke('transfers.list')
    act(() => bridge.setTransfers(listed.ok ? listed.value.map((t) => ({ ...t, state: 'done' as const })) : []))
    const options = await openMoreOptions(user, 'Nature films')

    // Once it has arrived the files are fixed; only sharing can change.
    expect(within(options).queryByText(en['options.files'])).not.toBeInTheDocument()
    await user.click(await within(options).findByRole('checkbox', { name: en['options.keepSharing'] }))

    expect(await within(options).findByText(en['options.notShared'])).toBeInTheDocument()
    expect(bridge.notSharing.has('file-Nature films')).toBe(true)
  })

  it('changes the speed limits for all of ComParta, as Settings does', async () => {
    const { bridge, user } = setup()
    await openDownloadFile(user, bridge, collection)
    const options = await openMoreOptions(user, 'Nature films')

    await user.selectOptions(within(options).getByLabelText(en['settings.receiveLimit']), String(1024 ** 2))

    const settings = await bridge.invoke('settings.get')
    expect(settings.ok && settings.value.receiveLimit).toBe(1024 ** 2)
  })

  it('is not offered for a file sent with a ComParta invite of an earlier version', async () => {
    const { user } = setup()
    await pasteInvite(user, sampleFakeInvite)

    // Until its details arrive, a file from an invite has no name of its own yet.
    await user.click(await screen.findByRole('button', { name: en['details.openFor'].replace('{name}', en['transfers.unnamed']) }))

    await screen.findByRole('dialog', { name: en['details.title'].replace('{name}', en['transfers.unnamed']) })
    expect(screen.queryByRole('button', { name: en['options.open'] })).not.toBeInTheDocument()
  })
})

describe('a download file opened in Windows (epic 14 story 5)', () => {
  it('opens the confirmation with what the file holds, and Get the file starts it', async () => {
    const bridge = createFakeBridge({ sendStepsSeen: 3 })
    bridge.openDownloadFile({ name: 'Open documentary.mkv', sizeBytes: 700_000_000 })
    render(<App bridge={bridge} />)
    const user = userEvent.setup()

    const confirm = await screen.findByRole('dialog', { name: en['confirm.title'] })
    expect(within(confirm).getByText('Open documentary.mkv')).toBeInTheDocument()
    await user.click(within(confirm).getByRole('button', { name: en['transfers.receive'] }))

    expect(await within(transfersSection()).findByText('Open documentary.mkv')).toBeInTheDocument()
  })

  it('opens the confirmation when one arrives while the app runs', async () => {
    const { bridge } = setup()
    await screen.findByText(en['shares.empty'])

    act(() => bridge.openDownloadFile({ name: 'Concert.mkv', sizeBytes: 1000 }))

    expect(await screen.findByRole('dialog', { name: en['confirm.title'] })).toBeInTheDocument()
  })

  it('explains a file that is not a download file', async () => {
    const { bridge } = setup()
    await screen.findByText(en['shares.empty'])

    act(() => bridge.openDownloadFile('invalid'))

    expect(await screen.findByText(en['error.download.invalidFile'])).toBeInTheDocument()
  })
})
