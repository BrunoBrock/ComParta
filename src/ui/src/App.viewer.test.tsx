import { fireEvent, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { ShareDto, TransferDto } from './bridge/contract.generated.ts'
import { notSending } from './bridge/fakeBridge.ts'
import { swipePixels } from './components/PictureViewer.tsx'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { goToReceived, setup, type User, video } from './test/appHarness.tsx'

const picture = (id: string, name: string): ShareDto => ({ id, name, sizeBytes: 2_000, status: 'ready', ...notSending })
// Listed oldest first by the app; the screen shows the newest first.
const beach = picture('p1', 'beach.jpg')
const cake = picture('p2', 'cake.png')
const dog = picture('p3', 'dog.webp')
const arrived: TransferDto = {
  id: 't1',
  name: 'from grandma.jpg',
  sizeBytes: 2_000,
  receivedBytes: 2_000,
  state: 'done',
  bytesPerSecond: 0,
  secondsLeft: null,
  neededBytes: null,
}

async function openDetails(user: User, name: string, words: Record<string, string> = en) {
  await user.click(await screen.findByRole('button', { name: words['details.openFor'].replace('{name}', name) }))
  return screen.getByRole('dialog', { name: words['details.title'].replace('{name}', name) })
}

async function openPicture(user: User, name: string) {
  const details = await openDetails(user, name)
  await user.click(within(details).getByRole('button', { name: en['received.open'] }))
  return screen.findByRole('dialog', { name })
}

const shown = async (viewer: HTMLElement) => within(viewer).findByRole('img')

describe('the picture viewer', () => {
  it('shows a shared picture full size from the address the app gives, never a path, and Close goes back', async () => {
    const { user } = setup({ shares: [beach] })

    const viewer = await openPicture(user, beach.name)

    const image = await shown(viewer)
    expect(image).toHaveAttribute('alt', beach.name)
    expect(image.getAttribute('src')).toMatch(/^https:\/\/media\.comparta\.invalid\/[0-9a-f]{32}$/)
    await user.click(within(viewer).getByRole('button', { name: en['viewer.close'] }))
    expect(screen.queryByRole('dialog', { name: beach.name })).not.toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: en['details.title'].replace('{name}', beach.name) })).toBeInTheDocument()
  })

  it('moves between the pictures in the order the screen shows them, by arrow keys and buttons, skipping other files', async () => {
    const { user } = setup({ shares: [beach, video, cake, dog] })

    const viewer = await openPicture(user, cake.name)
    expect(await shown(viewer)).toHaveAttribute('alt', cake.name)
    expect(within(viewer).getByText('2 of 3')).toBeInTheDocument()
    // A real window moves focus into the viewer when it opens; jsdom's stand-in does not.
    within(viewer).getByRole('button', { name: en['viewer.close'] }).focus()

    await user.keyboard('{ArrowLeft}')
    expect(await within(viewer).findByRole('img', { name: dog.name })).toBeInTheDocument()
    expect(within(viewer).queryByRole('button', { name: en['viewer.previous'] })).not.toBeInTheDocument()
    await user.keyboard('{ArrowLeft}')
    expect(await within(viewer).findByRole('img', { name: dog.name })).toBeInTheDocument()

    await user.click(within(viewer).getByRole('button', { name: en['viewer.next'] }))
    await user.keyboard('{ArrowRight}')
    expect(await within(viewer).findByRole('img', { name: beach.name })).toBeInTheDocument()
    expect(within(viewer).getByText('3 of 3')).toBeInTheDocument()
    expect(within(viewer).queryByRole('button', { name: en['viewer.next'] })).not.toBeInTheDocument()
  })

  it('zooms with the buttons and keys, back to the whole picture with 0', async () => {
    const { user } = setup({ shares: [beach] })
    const viewer = await openPicture(user, beach.name)
    const image = await shown(viewer)
    const zoomOut = within(viewer).getByRole('button', { name: en['viewer.zoomOut'] })
    expect(zoomOut).toBeDisabled()

    await user.click(within(viewer).getByRole('button', { name: en['viewer.zoomIn'] }))
    expect(image.style.width).toBe('150%')
    await user.keyboard('+')
    expect(image.style.width).toBe('225%')
    await user.keyboard('-')
    expect(image.style.width).toBe('150%')
    await user.click(zoomOut)
    expect(image.style.width).toBe('100%')
    expect(zoomOut).toBeDisabled()

    fireEvent.wheel(within(viewer).getByRole('img').parentElement!, { deltaY: -1 })
    expect(image.style.width).toBe('150%')
    await user.keyboard('0')
    expect(image.style.width).toBe('100%')
  })

  it('moves to the next or previous picture with a swipe, but not with a tap', async () => {
    const { user } = setup({ shares: [beach, cake] })
    const viewer = await openPicture(user, cake.name)
    const stage = (await shown(viewer)).parentElement!

    fireEvent.pointerDown(stage, { clientX: 300 })
    fireEvent.pointerUp(stage, { clientX: 300 - swipePixels })
    expect(await within(viewer).findByRole('img', { name: beach.name })).toBeInTheDocument()

    fireEvent.pointerDown(stage, { clientX: 300 })
    fireEvent.pointerUp(stage, { clientX: 310 })
    expect(await within(viewer).findByRole('img', { name: beach.name })).toBeInTheDocument()

    fireEvent.pointerDown(stage, { clientX: 100 })
    fireEvent.pointerUp(stage, { clientX: 100 + swipePixels })
    expect(await within(viewer).findByRole('img', { name: cake.name })).toBeInTheDocument()
  })

  it('closes with Esc', async () => {
    const { user } = setup({ shares: [beach] })
    const viewer = await openPicture(user, beach.name)

    fireEvent(viewer, new Event('cancel', { cancelable: true }))

    expect(screen.queryByRole('dialog', { name: beach.name })).not.toBeInTheDocument()
  })

  it('opens a picture that arrived inside ComParta instead of in another app, in Portuguese too', async () => {
    const { bridge, user } = setup({ language: 'pt-BR', transfers: [arrived] })
    await goToReceived(user)

    const details = await openDetails(user, arrived.name!, ptBR)
    await user.click(within(details).getByRole('button', { name: ptBR['received.open'] }))

    const viewer = await screen.findByRole('dialog', { name: arrived.name! })
    expect(await shown(viewer)).toHaveAttribute('alt', arrived.name)
    expect(within(viewer).getByRole('button', { name: ptBR['viewer.close'] })).toBeInTheDocument()
    expect(bridge.openedReceived).toEqual([])
  })

  it('leaves other files to the app that opens them, and offers no viewer for a shared video yet', async () => {
    const document: TransferDto = { ...arrived, id: 't2', name: 'letter.pdf' }
    const { bridge, user } = setup({ shares: [video], transfers: [document] })

    const shareDetails = await openDetails(user, video.name)
    expect(within(shareDetails).queryByRole('button', { name: en['received.open'] })).not.toBeInTheDocument()
    await user.click(within(shareDetails).getByRole('button', { name: en['details.close'] }))

    await goToReceived(user)
    const details = await openDetails(user, document.name!)
    await user.click(within(details).getByRole('button', { name: en['received.open'] }))
    expect(bridge.openedReceived).toEqual(['t2'])
  })
})
