import { fireEvent, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ShareDto, TransferDto } from './bridge/contract.generated.ts'
import { notSending } from './bridge/fakeBridge.ts'
import { seekSeconds, swipePixels } from './components/MediaViewer.tsx'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { goToReceived, setup, type User, video } from './test/appHarness.tsx'

const picture = (id: string, name: string): ShareDto => ({ id, name, sizeBytes: 2_000, status: 'ready', ...notSending })
// Listed oldest first by the app; the screen shows the newest first.
const beach = picture('p1', 'beach.jpg')
const cake = picture('p2', 'cake.png')
const dog = picture('p3', 'dog.webp')
const letterShare = picture('d1', 'letter.pdf')
const arrived: TransferDto = {
  id: 't1',
  name: 'from grandma.jpg',
  sizeBytes: 2_000,
  receivedBytes: 2_000,
  state: 'done',
  bytesPerSecond: 0,
  secondsLeft: null,
  neededBytes: null,
  risky: false,
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
    const { user } = setup({ shares: [beach, letterShare, cake, dog] })

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

  it('says a document cannot be played here, and Open with another app still hands it over', async () => {
    const letter: TransferDto = { ...arrived, id: 't2', name: 'letter.pdf' }
    const { bridge, user } = setup({ shares: [letterShare], transfers: [letter] })

    await openPicture(user, letterShare.name)
    const viewer = screen.getByRole('dialog', { name: letterShare.name })
    expect(await within(viewer).findByRole('alert')).toHaveTextContent(en['error.media.notPlayable'])
    await user.click(within(viewer).getByRole('button', { name: en['viewer.cancel'] }))

    await user.click(screen.getByRole('button', { name: en['details.close'] }))
    await goToReceived(user)
    const details = await openDetails(user, letter.name!)
    await user.click(within(details).getByRole('button', { name: en['received.openWith'] }))
    expect(bridge.openedReceived).toEqual(['t2'])
  })

  it('opens what arrived from its card and on a double-click, moving through the playable ones only', async () => {
    const song: TransferDto = { ...arrived, id: 't4', name: 'song.mp3' }
    const letter: TransferDto = { ...arrived, id: 't2', name: 'letter.pdf' }
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue()
    const { user } = setup({ transfers: [arrived, letter, song] })
    await goToReceived(user)

    await user.click(await screen.findByRole('button', { name: en['received.openFor'].replace('{name}', song.name!) }))
    const viewer = await screen.findByRole('dialog', { name: song.name! })
    expect(within(viewer).getByText('1 of 2')).toBeInTheDocument()
    await user.click(within(viewer).getByRole('button', { name: en['viewer.next'] }))
    expect(await within(viewer).findByRole('img', { name: arrived.name! })).toBeInTheDocument()
    await user.click(within(viewer).getByRole('button', { name: en['viewer.close'] }))

    await user.dblClick(screen.getByText(arrived.name!, { selector: '.visually-hidden, .name-start' }))
    expect(await within(await screen.findByRole('dialog', { name: arrived.name! })).findByRole('img')).toBeInTheDocument()
    vi.restoreAllMocks()
  })
})

describe('the song and video player', () => {
  const song = picture('m1', 'our song.mp3')

  afterEach(() => {
    vi.restoreAllMocks()
  })

  /** jsdom cannot play; these stand in for the web view's player. */
  function fakePlayback() {
    const played = vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(function (this: HTMLMediaElement) {
      Object.defineProperty(this, 'paused', { value: false, configurable: true })
      return Promise.resolve()
    })
    const paused = vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(function (this: HTMLMediaElement) {
      Object.defineProperty(this, 'paused', { value: true, configurable: true })
    })
    return { played, paused }
  }

  it('plays a shared video with the usual controls from the address the app gives', async () => {
    fakePlayback()
    const { user } = setup({ shares: [video] })

    const viewer = await openPicture(user, video.name)

    const player = await within(viewer).findByLabelText(video.name, { selector: 'video' })
    expect(player).toHaveAttribute('controls')
    expect(player.getAttribute('src')).toMatch(/^https:\/\/media\.comparta\.invalid\/[0-9a-f]{32}$/)
    expect(within(viewer).queryByRole('button', { name: en['viewer.zoomIn'] })).not.toBeInTheDocument()
  })

  it('plays and pauses with Space, goes back and forward with the arrows, and turns the sound off with M', async () => {
    const { played, paused } = fakePlayback()
    const { user } = setup({ shares: [song] })
    const viewer = await openPicture(user, song.name)
    const player = (await within(viewer).findByLabelText(song.name, { selector: 'audio' })) as HTMLAudioElement
    Object.defineProperty(player, 'duration', { value: 60, configurable: true })
    Object.defineProperty(player, 'paused', { value: true, configurable: true })
    let time = 0
    Object.defineProperty(player, 'currentTime', { get: () => time, set: (value: number) => (time = value), configurable: true })
    within(viewer).getByRole('button', { name: en['viewer.close'] }).focus()

    await user.keyboard(' ')
    expect(played).toHaveBeenCalledTimes(1)
    await user.keyboard(' ')
    expect(paused).toHaveBeenCalledTimes(1)

    await user.keyboard('{ArrowRight}{ArrowRight}')
    expect(time).toBe(2 * seekSeconds)
    await user.keyboard('{ArrowLeft}{ArrowLeft}{ArrowLeft}')
    expect(time).toBe(0)
    time = 58
    await user.keyboard('{ArrowRight}')
    expect(time).toBe(60)

    await user.keyboard('m')
    expect(player.muted).toBe(true)
    await user.keyboard('M')
    expect(player.muted).toBe(false)
  })

  it('stops playing when the viewer closes or moves to another file', async () => {
    const { paused } = fakePlayback()
    const { user } = setup({ shares: [song, video] })
    const viewer = await openPicture(user, video.name)
    await within(viewer).findByLabelText(video.name, { selector: 'video' })

    await user.click(within(viewer).getByRole('button', { name: en['viewer.next'] }))
    expect(await within(viewer).findByLabelText(song.name, { selector: 'audio' })).toBeInTheDocument()
    expect(paused).toHaveBeenCalledTimes(1)

    await user.click(within(viewer).getByRole('button', { name: en['viewer.close'] }))
    expect(paused).toHaveBeenCalledTimes(2)
  })

  it('fills the screen with a video on F, and a song does not try', async () => {
    fakePlayback()
    const fullscreen = vi.fn(() => Promise.resolve())
    HTMLElement.prototype.requestFullscreen = fullscreen
    const { user } = setup({ shares: [song, video] })
    const viewer = await openPicture(user, video.name)
    await within(viewer).findByLabelText(video.name, { selector: 'video' })
    within(viewer).getByRole('button', { name: en['viewer.close'] }).focus()

    await user.keyboard('f')
    expect(fullscreen).toHaveBeenCalledTimes(1)

    await user.keyboard('{PageDown}')
    await within(viewer).findByLabelText(song.name, { selector: 'audio' })
    within(viewer).getByRole('button', { name: en['viewer.close'] }).focus()
    await user.keyboard('f')
    expect(fullscreen).toHaveBeenCalledTimes(1)
  })
})

describe('when a file cannot be played', () => {
  it('says so and opens the folder with the shared file selected, then goes back home', async () => {
    const { bridge, user } = setup({ shares: [beach] })
    const details = await openDetails(user, beach.name)

    bridge.failNextWith = 'media.notPlayable'
    await user.click(within(details).getByRole('button', { name: en['received.open'] }))

    const viewer = await screen.findByRole('dialog', { name: beach.name })
    expect(await within(viewer).findByRole('alert')).toHaveTextContent(en['error.media.notPlayable'])
    await user.click(within(viewer).getByRole('button', { name: en['viewer.openFolder'] }))
    expect(bridge.revealedShares).toEqual([beach.id])
    expect(screen.queryByRole('dialog', { name: beach.name })).not.toBeInTheDocument()
  })

  it('leaves things as they were on Cancel, in Portuguese too', async () => {
    const { bridge, user } = setup({ language: 'pt-BR', transfers: [arrived] })
    await goToReceived(user)
    const details = await openDetails(user, arrived.name!, ptBR)

    bridge.failNextWith = 'media.notPlayable'
    await user.click(within(details).getByRole('button', { name: ptBR['received.open'] }))

    const viewer = await screen.findByRole('dialog', { name: arrived.name! })
    expect(await within(viewer).findByRole('alert')).toHaveTextContent(ptBR['error.media.notPlayable'])
    await user.click(within(viewer).getByRole('button', { name: ptBR['viewer.cancel'] }))
    expect(screen.queryByRole('dialog', { name: arrived.name! })).not.toBeInTheDocument()
    expect(bridge.revealedReceived).toEqual([])
    expect(bridge.openedReceived).toEqual([])
  })

  it('offers the folder of a received file when it stops playing part way', async () => {
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue()
    const film: TransferDto = { ...arrived, id: 't3', name: 'film.mp4' }
    const { bridge, user } = setup({ transfers: [film] })
    await goToReceived(user)
    const details = await openDetails(user, film.name!)
    await user.click(within(details).getByRole('button', { name: en['received.open'] }))
    const viewer = await screen.findByRole('dialog', { name: film.name! })

    fireEvent.error(await within(viewer).findByLabelText(film.name!, { selector: 'video' }))

    expect(await within(viewer).findByRole('alert')).toHaveTextContent(en['error.media.notPlayable'])
    expect(within(viewer).queryByLabelText(film.name!, { selector: 'video' })).not.toBeInTheDocument()
    await user.click(within(viewer).getByRole('button', { name: en['viewer.openFolder'] }))
    expect(bridge.revealedReceived).toEqual(['t3'])
    vi.restoreAllMocks()
  })

  it('shows any other problem as a message and goes back home', async () => {
    const { bridge, user } = setup({ shares: [beach] })
    const details = await openDetails(user, beach.name)

    bridge.failNextWith = 'file.unreadable'
    await user.click(within(details).getByRole('button', { name: en['received.open'] }))

    expect(await screen.findByText(en['error.file.unreadable'])).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: beach.name })).not.toBeInTheDocument()
  })
})
