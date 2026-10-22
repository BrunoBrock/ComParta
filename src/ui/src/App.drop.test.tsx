import { fireEvent, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { setup, sharesSection, video } from './test/appHarness.tsx'

/** What the browser hands the page for a drop: files, and for each whether it is a folder. */
function dropped(items: { name: string; size: number; folder?: boolean }[]) {
  const files = items.map((item) => new File([new Uint8Array(item.size)], item.name))
  return {
    types: ['Files'],
    files,
    items: items.map((item) => ({ webkitGetAsEntry: () => ({ isDirectory: item.folder ?? false }) })),
    dropEffect: 'none',
  }
}

const app = () => document.querySelector('.app')!

describe('dropping files onto the window', () => {
  it('shows where to drop, then shares every file and says so', async () => {
    setup()
    await screen.findByText(en['shares.empty'])
    const data = dropped([
      { name: 'Beach.jpg', size: 10 },
      { name: 'Party.mp4', size: 20 },
    ])

    fireEvent.dragOver(app(), { dataTransfer: data })
    expect(screen.getByText(en['drop.here'])).toBeInTheDocument()
    fireEvent.drop(app(), { dataTransfer: data })

    expect(await screen.findByText('Shared 2 files. Copy each invite to send them.')).toBeInTheDocument()
    expect(screen.queryByText(en['drop.here'])).not.toBeInTheDocument()
    expect(within(sharesSection()).getAllByRole('listitem')).toHaveLength(2)
  })

  it('shares a dropped folder whole, beside the files dropped with it', async () => {
    const { bridge } = setup()
    bridge.folders = { Photos: { name: 'Photos', sizeBytes: 3_000, itemCount: 30 } }
    await screen.findByText(en['shares.empty'])

    fireEvent.drop(app(), {
      dataTransfer: dropped([
        { name: 'Photos', size: 0, folder: true },
        { name: 'Beach.jpg', size: 10 },
      ]),
    })

    expect(await screen.findByText('Shared 2 files. Copy each invite to send them.')).toBeInTheDocument()
    const folder = (await within(sharesSection()).findByText('Photos')).closest('li')!
    expect(within(folder).getByText(/30 files/)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says when a dropped folder has nothing in it', async () => {
    const { bridge } = setup()
    bridge.folders = { Empty: { name: 'Empty', sizeBytes: 0, itemCount: 0 } }
    await screen.findByText(en['shares.empty'])

    fireEvent.drop(app(), { dataTransfer: dropped([{ name: 'Empty', size: 0, folder: true }]) })

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.share.folderEmpty'])
  })

  it('says why a file was not shared', async () => {
    setup({ shares: [video] })
    await screen.findByRole('button', { name: `Details of ${video.name}` })

    fireEvent.drop(app(), { dataTransfer: dropped([{ name: video.name, size: 10 }]) })

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.share.duplicate'])
  })

  it('points to the file dialog when the window cannot take dropped files', async () => {
    const { bridge } = setup()
    bridge.handOverDrop = () => false
    await screen.findByText(en['shares.empty'])

    fireEvent.drop(app(), { dataTransfer: dropped([{ name: 'Beach.jpg', size: 10 }]) })

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.share.dropFailed'])
  })

  it('hides the drop area when the files leave the window', () => {
    setup()
    fireEvent.dragOver(app(), { dataTransfer: dropped([{ name: 'Beach.jpg', size: 10 }]) })
    fireEvent.dragLeave(app(), { relatedTarget: null })
    expect(screen.queryByText(en['drop.here'])).not.toBeInTheDocument()
  })

  it('ignores dragging things that are not files', () => {
    setup()
    fireEvent.dragOver(app(), { dataTransfer: { types: ['text/plain'] } })
    expect(screen.queryByText(en['drop.here'])).not.toBeInTheDocument()
  })
})
