import { mkdtempSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, type Page, test } from '@playwright/test'
import { sampleFakeLinkInvite } from '../src/bridge/fakeBridge.ts'
import { en } from '../src/i18n/en.ts'
import { qrModules } from '../src/qr.ts'

// Pass an invite with a QR code (epic 7 story 9), in a real browser engine on the fake bridge: the
// phone's camera is the browser's stand-in camera, playing a picture of a QR code, and the screen
// reads it with the same code the Android web view runs. The Android check in CI covers asking the
// phone for the camera; a real phone reading a PC's screen is in the pull request's list.

/** A video file of a QR code held in front of the camera: one still frame, in the format browsers take for a stand-in camera. */
function cameraShowing(text: string) {
  const modules = qrModules(text)!
  const width = 640
  const height = 480
  const cell = Math.floor((height - 40) / (modules.length + 8))
  const left = Math.floor((width - modules.length * cell) / 2)
  const top = Math.floor((height - modules.length * cell) / 2)
  // Brightness on white paper, then the two colour planes at no colour.
  const light = Buffer.alloc(width * height, 235)
  modules.forEach((row, y) =>
    row.forEach((dark, x) => {
      if (!dark) return
      for (let dy = 0; dy < cell; dy++) light.fill(16, (top + y * cell + dy) * width + left + x * cell, (top + y * cell + dy) * width + left + (x + 1) * cell)
    }),
  )
  const colour = Buffer.alloc((width / 2) * (height / 2) * 2, 128)
  const file = join(mkdtempSync(join(tmpdir(), 'comparta-camera-')), 'qr.y4m')
  writeFileSync(file, Buffer.concat([Buffer.from(`YUV4MPEG2 W${width} H${height} F10:1 Ip A1:1 C420jpeg\nFRAME\n`), light, colour]))
  return file
}

// Node loads the package's own build, which is the function itself.
const jsQR = createRequire(import.meta.url)('jsqr') as typeof import('jsqr').default

const chromium = process.env.CHROMIUM_PATH
const androidPhone = 'Mozilla/5.0 (Linux; Android 14; Pixel 7 Build/UQ1A; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/130.0.0.0 Mobile Safari/537.36'

test.use({
  userAgent: androidPhone,
  permissions: ['camera'],
  launchOptions: {
    ...(chromium ? { executablePath: chromium } : {}),
    args: [
      '--use-fake-device-for-media-stream',
      '--use-fake-ui-for-media-stream',
      `--use-file-for-fake-video-capture=${cameraShowing(sampleFakeLinkInvite)}`,
    ],
  },
})

async function openReceive(page: Page) {
  await page.goto('./')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await page.getByRole('button', { name: en['fab.open'] }).tap()
  await page.getByRole('menuitem', { name: en['transfers.open'] }).tap()
  return page.getByRole('dialog', { name: en['receive.title'] })
}

test('scanning an invite shown as a QR code opens Get this file? and receives it', async ({ page }) => {
  const receive = await openReceive(page)
  const scan = receive.getByRole('button', { name: en['receive.scan'] })
  // Easy to press: the UX design's 44 pixels each way.
  const box = (await scan.boundingBox())!
  expect(Math.min(box.width, box.height)).toBeGreaterThanOrEqual(44)

  await scan.tap()

  const confirm = page.getByRole('dialog', { name: en['confirm.title'] })
  await expect(confirm).toBeVisible({ timeout: 20_000 })
  await expect(confirm).toContainText('Birthday party.mp4')
  // The camera is off once the code is read.
  expect(await page.evaluate(() => document.querySelector('video.qr-camera'))).toBeNull()
  await confirm.getByRole('button', { name: en['transfers.receive'] }).tap()
  await expect(page.getByRole('region', { name: en['transfers.heading'] }).getByRole('listitem')).toHaveCount(1)
})

test('the QR code a share shows is read back as that file’s invite', async ({ page }) => {
  await page.goto('./')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await page.getByRole('button', { name: en['fab.open'] }).tap()
  await page.getByRole('menuitem', { name: en['shares.add'] }).tap()
  await page.getByRole('dialog').getByRole('button', { name: en['steps.done'] }).tap()
  await page.getByRole('button', { name: /^Details of / }).first().tap()
  const details = page.getByRole('dialog', { name: /^Details of / })
  const [before, after] = en['details.title'].split('{name}')
  const title = (await details.getByRole('heading').textContent())!
  const name = title.slice(before.length, title.length - after.length)
  await details.getByRole('button', { name: en['shares.showQr'] }).tap()

  const code = page.getByRole('img', { name: en['qr.label'].replace('{name}', name) })
  await expect(code).toBeVisible()
  // What a camera sees: the code as the browser drew it, a picture read pixel by pixel.
  const picture = await code.evaluate(async (svg) => {
    const image = new Image()
    image.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(new XMLSerializer().serializeToString(svg))}`
    await image.decode()
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = 400
    const context = canvas.getContext('2d')!
    context.drawImage(image, 0, 0, 400, 400)
    return Array.from(context.getImageData(0, 0, 400, 400).data)
  })
  const read = jsQR(Uint8ClampedArray.from(picture), 400, 400)?.data
  expect(read).toContain(encodeURIComponent(name))

  // Scanned on another phone, it opens Get this file? for the same file.
  await page.getByRole('dialog', { name: en['qr.title'].replace('{name}', name) }).getByRole('button', { name: en['details.close'] }).tap()
  await details.getByRole('button', { name: en['details.close'] }).tap()
  await page.getByRole('button', { name: en['fab.open'] }).tap()
  await page.getByRole('menuitem', { name: en['transfers.open'] }).tap()
  const receive = page.getByRole('dialog', { name: en['receive.title'] })
  await receive.getByLabel(en['transfers.inviteLabel']).fill(read!)
  await receive.getByRole('button', { name: en['receive.continue'] }).tap()
  await expect(page.getByRole('dialog', { name: en['confirm.title'] })).toContainText(name)
})
