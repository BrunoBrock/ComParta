import { screen, waitFor, within } from '@testing-library/react'
import jsQR from 'jsqr'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { drawQr } from './qr.ts'
import { accessibilityProblems } from './test/axe.ts'
import { album, railButton, setup, video, type User } from './test/appHarness.tsx'

// Pass an invite with a QR code (epic 7 story 9): the details window shows the invite as a code,
// and on a phone Receive reads one with the camera.

type Words = Record<keyof typeof en, string>

async function openDetails(user: User, words: Words, name = video.name) {
  await user.click(await screen.findByRole('button', { name: words['details.openFor'].replace('{name}', name) }))
  return screen.getByRole('dialog', { name: words['details.title'].replace('{name}', name) })
}

/** Reads the code drawn on the screen as a camera would: square by square, into a picture for jsQR. */
function readDrawnCode(svg: Element) {
  const size = Number(svg.getAttribute('viewBox')!.split(' ')[2])
  const scale = 4
  const width = size * scale
  const pixels = new Uint8ClampedArray(width * width * 4).fill(255)
  for (const [, x, y] of svg.querySelector('path')!.getAttribute('d')!.matchAll(/M(\d+) (\d+)/g))
    for (let dy = 0; dy < scale; dy++)
      for (let dx = 0; dx < scale; dx++) {
        const at = ((Number(y) * scale + dy) * width + Number(x) * scale + dx) * 4
        pixels.fill(0, at, at + 3)
      }
  return jsQR(pixels, width, width)?.data
}

describe('show an invite as a QR code', () => {
  it('draws the same link Copy link copies, as a code a camera reads', async () => {
    const { bridge, user } = setup({ shares: [video] })
    const details = await openDetails(user, en)

    await user.click(await within(details).findByRole('button', { name: en['shares.showQr'] }))

    const window = screen.getByRole('dialog', { name: en['qr.title'].replace('{name}', video.name) })
    const code = await within(window).findByRole('img', { name: en['qr.label'].replace('{name}', video.name) })
    await user.click(within(details).getByRole('button', { name: en['shares.copyLink'] }))
    expect(readDrawnCode(code)).toBe(bridge.copiedInvite)
    expect(bridge.copiedInvite).toContain(encodeURIComponent(video.name))
    expect(window).toHaveTextContent(en['transfers.open'])
    expect(window).toHaveTextContent(en['receive.scan'])
    expect(await accessibilityProblems()).toEqual([])
  })

  it('closes back to the button that opened it, in Portuguese too', async () => {
    const { user } = setup({ language: 'pt-BR', shares: [video] })
    const details = await openDetails(user, ptBR)
    const show = await within(details).findByRole('button', { name: ptBR['shares.showQr'] })

    await user.click(show)
    const window = screen.getByRole('dialog', { name: ptBR['qr.title'].replace('{name}', video.name) })
    await within(window).findByRole('img', { name: ptBR['qr.label'].replace('{name}', video.name) })
    expect(window).toHaveTextContent(ptBR['qr.hint'].split('{')[0])
    await user.click(within(window).getByRole('button', { name: ptBR['details.close'] }))

    expect(window).not.toBeInTheDocument()
    expect(show).toHaveFocus()
    expect(screen.getByRole('dialog', { name: ptBR['details.title'].replace('{name}', video.name) })).toBeInTheDocument()
  })

  it('is not offered while the file cannot be sent', async () => {
    const { user } = setup({ shares: [album] })
    await openDetails(user, en, album.name)

    await screen.findByText(en['details.size'])
    expect(screen.queryByRole('button', { name: en['shares.showQr'] })).not.toBeInTheDocument()
  })

  it('says what went wrong when the file went away before the code was drawn', async () => {
    const { bridge, user } = setup({ shares: [video] })
    const details = await openDetails(user, en)
    const show = await within(details).findByRole('button', { name: en['shares.showQr'] })

    bridge.failNextWith = 'share.notFound'
    await user.click(show)

    expect(await screen.findByText(en['error.share.notFound'])).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: en['qr.title'].replace('{name}', video.name) })).not.toBeInTheDocument()
  })

  it('gives up on a link too long for any QR code instead of drawing a broken one', () => {
    expect(drawQr('x'.repeat(3000))).toBeNull()
    expect(drawQr(`magnet:?xt=urn:btih:${'a'.repeat(40)}&dn=Natal.mp4`)?.size).toBeGreaterThan(21)
  })
})

describe('scan a QR code in Receive', () => {
  const realAgent = navigator.userAgent

  afterEach(() => {
    Object.defineProperty(navigator, 'userAgent', { value: realAgent, configurable: true })
    Object.defineProperty(navigator, 'mediaDevices', { value: undefined, configurable: true })
    vi.restoreAllMocks()
  })

  /** A phone: an Android web view whose camera answers as `camera` says. */
  function onAPhone(camera: () => Promise<MediaStream>) {
    Object.defineProperty(navigator, 'userAgent', { value: 'Mozilla/5.0 (Linux; Android 14; wv) Chrome/130.0', configurable: true })
    // The test page draws nothing, so no picture is read here; reading a code is checked in a real browser (e2e/scan.e2e.ts).
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined)
    const getUserMedia = vi.fn(camera)
    Object.defineProperty(navigator, 'mediaDevices', { value: { getUserMedia }, configurable: true })
    return getUserMedia
  }

  async function openReceive(user: User) {
    await user.click(await railButton(en['transfers.open']))
    return screen.getByRole('dialog', { name: en['receive.title'] })
  }

  it('is not offered on Windows, which has paste and the download file', async () => {
    const { user } = setup()
    const receive = await openReceive(user)

    expect(within(receive).queryByRole('button', { name: en['receive.scan'] })).not.toBeInTheDocument()
  })

  it('asks for the camera only once the person taps Scan, and turns it off on Back', async () => {
    const stopped = vi.fn()
    const getUserMedia = onAPhone(async () => ({ getTracks: () => [{ stop: stopped }] }) as unknown as MediaStream)
    const { user } = setup()
    const receive = await openReceive(user)
    expect(getUserMedia).not.toHaveBeenCalled()

    await user.click(within(receive).getByRole('button', { name: en['receive.scan'] }))

    const scanning = screen.getByRole('dialog', { name: en['scan.title'] })
    expect(getUserMedia).toHaveBeenCalledWith({ video: { facingMode: 'environment' }, audio: false })
    expect(within(scanning).getByLabelText(en['scan.camera'])).toBeInTheDocument()
    expect(scanning).toHaveTextContent(en['scan.hint'])
    expect(await accessibilityProblems()).toEqual([])

    await user.click(within(scanning).getByRole('button', { name: en['scan.back'] }))
    await waitFor(() => expect(stopped).toHaveBeenCalled())
    expect(screen.getByRole('dialog', { name: en['receive.title'] })).toBeInTheDocument()
    expect(screen.getByLabelText(en['transfers.inviteLabel'])).toBeInTheDocument()
  })

  it('says plainly when the camera is refused, and paste still works', async () => {
    onAPhone(() => Promise.reject(new DOMException('denied', 'NotAllowedError')))
    const { user } = setup()
    const receive = await openReceive(user)

    await user.click(within(receive).getByRole('button', { name: en['receive.scan'] }))

    expect(await screen.findByRole('alert')).toHaveTextContent(en['scan.noCamera'])
    await user.click(screen.getByRole('button', { name: en['scan.back'] }))
    expect(screen.getByLabelText(en['transfers.inviteLabel'])).toBeInTheDocument()
  })
})
