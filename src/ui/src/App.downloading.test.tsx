import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { TransferDto } from './bridge/contract.generated.ts'
import { sampleFakeDownloadLink } from './bridge/fakeBridge.ts'
import { en } from './i18n/en.ts'
import {
  setup,
  transfersSection,
  goToReceived,
  pasteInvite,
  railButton,
} from './test/appHarness.tsx'

describe('downloading from a link or a download file', () => {
  const downloading: TransferDto = {
    id: 'd1',
    name: 'Open film.mkv',
    sizeBytes: 1000 * 1024 * 1024,
    receivedBytes: 500 * 1024 * 1024,
    state: 'receiving',
    bytesPerSecond: 2 * 1024 * 1024,
    secondsLeft: 250,
    neededBytes: null,
    risky: false, folder: false,
  }

  it('starts a download when a download link is pasted', async () => {
    const { user } = setup()

    await pasteInvite(user, sampleFakeDownloadLink)

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await within(transfersSection()).findByText('Open film.mkv')).toBeInTheDocument()
  })

  it('starts a download from a download file', async () => {
    const { user } = setup()

    await user.click(await railButton(en['transfers.open']))
    await user.click(screen.getByRole('button', { name: en['receive.openFile'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await within(transfersSection()).findByText('Free documentary.mkv')).toBeInTheDocument()
  })

  it('keeps the box open when the person closes the file dialog', async () => {
    const { bridge, user } = setup()
    bridge.nextDownloadFile = null

    await user.click(await railButton(en['transfers.open']))
    await user.click(screen.getByRole('button', { name: en['receive.openFile'] }))

    const dialog = screen.getByRole('dialog', { name: en['receive.title'] })
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
  })

  it('explains when the chosen file is not a download file', async () => {
    const { bridge, user } = setup()
    bridge.nextDownloadFile = 'invalid'

    await user.click(await railButton(en['transfers.open']))
    await user.click(screen.getByRole('button', { name: en['receive.openFile'] }))

    const dialog = screen.getByRole('dialog', { name: en['receive.title'] })
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(en['error.download.invalidFile'])
  })

  it('shows the speed and about how long is left', async () => {
    const { user } = setup({ transfers: [downloading] })
    await goToReceived(user)

    const note = await screen.findByText('Receiving: 50% (500.0 MB of 1,000.0 MB) · about 4 minutes left')
    // The speed has its own line on the card, in grid and list alike.
    expect(note.closest('li')).toHaveTextContent('Receiving 2.0 MB/s')
  })

  it('pauses and continues a download', async () => {
    const { user } = setup({ transfers: [downloading] })
    await goToReceived(user)

    await user.click(await screen.findByRole('button', { name: 'Pause Open film.mkv' }))

    const item = within(transfersSection()).getByRole('listitem')
    expect(await within(item).findByText(en['transfers.chip.paused'])).toBeInTheDocument()
    expect(within(item).getByText(en['transfers.state.paused'])).toBeInTheDocument()
    expect(within(item).getByRole('progressbar')).toBeInTheDocument()

    await user.click(within(item).getByRole('button', { name: 'Continue Open film.mkv' }))

    expect(await within(item).findByRole('button', { name: 'Pause Open film.mkv' })).toBeInTheDocument()
  })

  it('offers no pause for a finished file', async () => {
    const { user } = setup({ transfers: [{ ...downloading, receivedBytes: downloading.sizeBytes!, state: 'done' }] })
    await goToReceived(user)

    await within(transfersSection()).findByText('Open film.mkv')
    expect(screen.queryByRole('button', { name: 'Pause Open film.mkv' })).not.toBeInTheDocument()
  })
})
