import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { expect, type Locator, type Page, test } from '@playwright/test'
import { en } from '../src/i18n/en.ts'
import { ptBR } from '../src/i18n/pt-BR.ts'

// The phone layout (UX design, Phone; epic 7 story 4), checked where it is drawn: a real browser
// engine at 360 by 740, on the fake bridge.

const axeSource = createRequire(import.meta.url).resolve('axe-core/axe.min.js')
/** The UX design's `--target`: anything a finger presses is at least this tall and wide. */
const target = 44
const longName = 'Aniversário da vovó Maria com toda a família reunida em 2024 (versão completa).mp4'

async function open(page: Page, query = '') {
  await page.goto(`./${query}`)
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
}

/** The page never scrolls sideways: nothing is wider than the phone. */
async function expectNoSidewaysScroll(page: Page) {
  const widths = await page.evaluate(() => ({ page: document.documentElement.scrollWidth, screen: window.innerWidth }))
  expect(widths.page).toBeLessThanOrEqual(widths.screen)
}

/**
 * Every control a finger can press inside `scope`, with its size; a check box or radio counts with
 * the label around it, which is what takes the tap.
 */
async function smallTargets(scope: Locator) {
  return scope.evaluate((root, min) => {
    const controls = root.querySelectorAll<HTMLElement>(
      'button, a[href], input:not([type="hidden"]), select, textarea, summary, [role="button"], [role="menuitem"], [role="menuitemradio"]',
    )
    const small: string[] = []
    for (const control of controls) {
      const shown = control.getClientRects().length > 0 && getComputedStyle(control).visibility !== 'hidden'
      if (!shown) continue
      const pressed = control.matches('input[type="checkbox"], input[type="radio"]') ? (control.closest('label') ?? control) : control
      const { width, height } = pressed.getBoundingClientRect()
      // Visually hidden helpers (1 by 1 px) are for screen readers, not fingers.
      if (width <= 1 && height <= 1) continue
      if (Math.round(width) < min || Math.round(height) < min) {
        const name = control.getAttribute('aria-label') ?? control.textContent?.trim() ?? control.tagName
        small.push(`${name} (${Math.round(width)} × ${Math.round(height)})`)
      }
    }
    return small
  }, target)
}

/** Pairs of things inside one file's card or row that are drawn over each other. */
async function overlaps(page: Page) {
  return page.locator('main').evaluate((main) => {
    const found: string[] = []
    for (const item of main.querySelectorAll('li')) {
      const parts = [...item.querySelectorAll<HTMLElement>('button, .item-name, .item-detail, .chip, .item-speed')]
        .filter((part) => part.getClientRects().length > 0)
        .map((part) => ({ part, box: part.getBoundingClientRect() }))
      for (const [i, a] of parts.entries())
        for (const b of parts.slice(i + 1)) {
          if (a.part.contains(b.part) || b.part.contains(a.part)) continue
          const across = Math.min(a.box.right, b.box.right) - Math.max(a.box.left, b.box.left)
          const down = Math.min(a.box.bottom, b.box.bottom) - Math.max(a.box.top, b.box.top)
          if (across > 1 && down > 1)
            found.push(`${a.part.textContent?.trim() || a.part.getAttribute('aria-label')} / ${b.part.textContent?.trim() || b.part.getAttribute('aria-label')}`)
        }
    }
    return found
  })
}

/** axe in a real engine, which also checks colour contrast (jsdom cannot). */
async function accessibilityProblems(page: Page) {
  await page.addScriptTag({ path: axeSource })
  return page.evaluate(async () => {
    const axe = (window as unknown as { axe: { run: (context: Document, options: object) => Promise<{ violations: { id: string; nodes: { target: string[] }[] }[] }> } }).axe
    const result = await axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] })
    return result.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)
  })
}

/** Shares one sample file through the floating ＋ and the guided send, as a person would on a phone. */
async function shareThroughTheGuide(page: Page) {
  await page.getByRole('button', { name: en['fab.open'] }).tap()
  await page.getByRole('menuitem', { name: en['shares.add'] }).tap()
  const steps = page.getByRole('dialog')
  await expect(steps.getByRole('button', { name: en['steps.done'] })).toBeVisible()
  await expectInsideTheScreen(page, steps)
  expect(await smallTargets(steps)).toEqual([])
  await steps.getByRole('button', { name: en['steps.done'] }).tap()
  await expect(steps).toBeHidden()
}

async function expectInsideTheScreen(page: Page, element: Locator) {
  const box = (await element.boundingBox())!
  const screen = page.viewportSize()!
  expect(box.x).toBeGreaterThanOrEqual(0)
  expect(box.x + box.width).toBeLessThanOrEqual(screen.width + 0.5)
}

test('the home screen has the bottom navigation and the floating ＋ above it', async ({ page }) => {
  await open(page)
  const nav = page.getByRole('navigation', { name: en['nav.label'] })
  const navBox = (await nav.boundingBox())!
  expect(navBox.y + navBox.height).toBeGreaterThan(740 - 120)
  await expect(nav.getByRole('button', { name: new RegExp(`^${en['nav.shared']}`) })).toBeVisible()
  await expect(nav.getByRole('button', { name: new RegExp(`^${en['nav.received']}`) })).toBeVisible()
  // The rail's two big buttons give way to the ＋ on a phone.
  await expect(page.getByRole('complementary').getByRole('button', { name: en['shares.add'] })).toBeHidden()

  const fab = (await page.getByRole('button', { name: en['fab.open'] }).boundingBox())!
  const bar = (await page.locator('.rail').boundingBox())!
  expect(fab.y + fab.height).toBeLessThanOrEqual(bar.y)

  await expectNoSidewaysScroll(page)
  expect(await smallTargets(page.locator('body'))).toEqual([])
  expect(await accessibilityProblems(page)).toEqual([])
})

test('shared files show two to a row, and as a list, without sideways scrolling', async ({ page }) => {
  await open(page)
  for (let i = 0; i < 3; i++) await shareThroughTheGuide(page)

  const cards = page.getByRole('region', { name: en['shares.heading'] }).getByRole('listitem')
  await expect(cards).toHaveCount(3)
  const columns = new Set<number>()
  for (const card of await cards.all()) columns.add(Math.round((await card.boundingBox())!.x))
  expect(columns.size).toBe(2)

  await expectNoSidewaysScroll(page)
  expect(await overlaps(page)).toEqual([])
  expect(await smallTargets(page.locator('body'))).toEqual([])
  expect(await accessibilityProblems(page)).toEqual([])

  await page.getByRole('button', { name: en['view.list'] }).tap()
  await expectNoSidewaysScroll(page)
  expect(await overlaps(page)).toEqual([])
  expect(await smallTargets(page.locator('body'))).toEqual([])
})

test('receiving a file with a long name from the ＋ fits the phone', async ({ page }) => {
  await open(page)
  await page.getByRole('button', { name: en['fab.open'] }).tap()
  await page.getByRole('menuitem', { name: en['transfers.open'] }).tap()

  const receive = page.getByRole('dialog', { name: en['receive.title'] })
  await expectInsideTheScreen(page, receive)
  expect(await smallTargets(receive)).toEqual([])
  await receive.getByLabel(en['transfers.inviteLabel']).fill(`https://example.org/download/${encodeURIComponent(longName)}`)
  await receive.getByRole('button', { name: en['receive.continue'] }).tap()

  const confirm = page.getByRole('dialog', { name: en['confirm.title'] })
  await expectInsideTheScreen(page, confirm)
  expect(await smallTargets(confirm)).toEqual([])
  await confirm.getByRole('button', { name: en['transfers.receive'] }).tap()

  const received = page.getByRole('region', { name: en['transfers.heading'] })
  await expect(received.getByRole('listitem')).toHaveCount(1)
  await expectNoSidewaysScroll(page)
  expect(await overlaps(page)).toEqual([])
  expect(await smallTargets(page.locator('body'))).toEqual([])
  expect(await accessibilityProblems(page)).toEqual([])
})

test('details open as a sheet that fills the phone', async ({ page }) => {
  await open(page)
  await shareThroughTheGuide(page)
  await page.getByRole('button', { name: /^Details of / }).first().tap()

  const sheet = page.getByRole('dialog', { name: /^Details of / })
  const box = (await sheet.boundingBox())!
  expect([box.x, box.y, Math.round(box.width), Math.round(box.height)]).toEqual([0, 0, 360, 740])
  expect(await smallTargets(sheet)).toEqual([])
  expect(await accessibilityProblems(page)).toEqual([])
})

test('Help and Settings fit the phone and are easy to press', async ({ page }) => {
  await open(page)
  for (const name of [en['nav.help'], en['nav.settings']]) {
    await page.getByRole('banner').getByRole('button', { name }).tap()
    const window = page.getByRole('dialog')
    await expectInsideTheScreen(page, window)
    expect(await smallTargets(window)).toEqual([])
    expect(await accessibilityProblems(page)).toEqual([])
    await page.keyboard.press('Escape')
    await expect(window).toBeHidden()
  }
})

test.describe('in Portuguese, whose words are longer', () => {
  test.use({ locale: 'pt-BR' })

  test('the screen still fits', async ({ page }) => {
    await open(page)
    await expect(page.getByRole('navigation', { name: ptBR['nav.label'] })).toBeVisible()
    await shareThroughTheGuidePt(page)
    await expectNoSidewaysScroll(page)
    expect(await smallTargets(page.locator('body'))).toEqual([])
  })
})

async function shareThroughTheGuidePt(page: Page) {
  await page.getByRole('button', { name: ptBR['fab.open'] }).tap()
  await page.getByRole('menuitem', { name: ptBR['shares.add'] }).tap()
  await page.getByRole('dialog').getByRole('button', { name: ptBR['steps.done'] }).tap()
}

/** What the app's web view would read: a small photo for a picture, a short clip for a video. */
const media = {
  photo: fileURLToPath(new URL('./media/photo.jpg', import.meta.url)),
  clip: fileURLToPath(new URL('./media/clip.webm', import.meta.url)),
}

/** Answers the addresses the fake bridge gives, as the app's web view does with the file asked for. */
async function serveMedia(page: Page) {
  await page.route('https://media.comparta.invalid/**', (route) =>
    route.request().resourceType() === 'image'
      ? route.fulfill({ path: media.photo, contentType: 'image/jpeg' })
      : route.fulfill({ path: media.clip, contentType: 'video/webm' }),
  )
}

/** The viewer fills the phone, its Close is large, and Close goes back to where it was opened. */
async function expectViewerFillsThePhoneAndCloses(page: Page, viewer: Locator) {
  const box = (await viewer.boundingBox())!
  expect([box.x, box.y, Math.round(box.width), Math.round(box.height)]).toEqual([0, 0, 360, 740])
  const close = viewer.getByRole('button', { name: en['viewer.close'] })
  const closeBox = (await close.boundingBox())!
  expect(Math.min(closeBox.width, closeBox.height)).toBeGreaterThanOrEqual(48)
  expect(await smallTargets(viewer)).toEqual([])
  await expectNoSidewaysScroll(page)
  await close.tap()
  await expect(viewer).toBeHidden()
}

test('a shared video plays and a received photo shows full screen, and Close goes back (epic 7 story 12)', async ({ page }) => {
  await serveMedia(page)
  await open(page)

  // The first sample the guided send shares is a video.
  await shareThroughTheGuide(page)
  await page.getByRole('button', { name: /^Details of / }).first().tap()
  const sheet = page.getByRole('dialog', { name: /^Details of / })
  await sheet.getByRole('button', { name: en['received.open'] }).tap()
  const player = page.getByRole('dialog', { name: 'Birthday party.mp4', exact: true })
  await expect.poll(() => player.locator('video').evaluate((video: HTMLVideoElement) => video.readyState)).toBeGreaterThan(0)
  await expectViewerFillsThePhoneAndCloses(page, player)
  await expect(sheet).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(sheet).toBeHidden()

  // A photo arrives, and opens from its card in Received.
  await page.getByRole('button', { name: en['fab.open'] }).tap()
  await page.getByRole('menuitem', { name: en['transfers.open'] }).tap()
  const receive = page.getByRole('dialog', { name: en['receive.title'] })
  await receive.getByLabel(en['transfers.inviteLabel']).fill('https://example.org/download/Beach%20day.jpg')
  await receive.getByRole('button', { name: en['receive.continue'] }).tap()
  await page.getByRole('dialog', { name: en['confirm.title'] }).getByRole('button', { name: en['transfers.receive'] }).tap()
  const openPhoto = page.getByRole('button', { name: en['received.openFor'].replace('{name}', 'Beach day.jpg'), exact: true })
  await expect(openPhoto).toBeVisible({ timeout: 30_000 })
  await openPhoto.tap()
  const photo = page.getByRole('dialog', { name: 'Beach day.jpg', exact: true })
  await expect.poll(() => photo.getByRole('img').evaluate((image: HTMLImageElement) => image.naturalWidth)).toBeGreaterThan(0)
  await expectViewerFillsThePhoneAndCloses(page, photo)
})
