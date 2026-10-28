// Drives the running ComParta.exe through its own screen, over the web view's debugging port, for
// check-zip.ps1. Each step sends a command the way the screen does (the same InvokeDotNet call to
// BridgeHost), so it goes through the real router, use cases, engine and storage, and listens for
// the events the screen shows.
//   node drive-app.mjs <port> version
//   node drive-app.mjs <port> start-share      asks for the file dialog, as "Share a file" does
//   node drive-app.mjs <port> shared <name>    waits for the share to answer and show
//   node drive-app.mjs <port> quit
// Playwright comes from the screen's packages (src/ui), which build.ps1 installs.
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const here = path.dirname(fileURLToPath(import.meta.url))
const require = createRequire(path.join(here, '..', '..', 'src', 'ui', 'package.json'))
const { chromium } = require('@playwright/test')

const [port, step, name] = process.argv.slice(2)

async function connect() {
  const until = Date.now() + 120_000
  for (;;) {
    try {
      const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`)
      const page = browser.contexts().flatMap((context) => context.pages()).find((one) => new URL(one.url()).hostname === '0.0.0.1')
      if (page) {
        // The screen asked something first, so the bridge script is loaded.
        await page.waitForFunction(() => window.HybridWebView !== undefined, null, { timeout: 60_000 })
        return { browser, page }
      }
    } catch (error) {
      if (Date.now() > until) throw error
    }
    if (Date.now() > until) throw new Error('The screen never loaded in the web view.')
    await new Promise((resolve) => setTimeout(resolve, 2000))
  }
}

/** One command through the app's bridge, answered as the screen gets it. */
function invoke(page, command, args) {
  return page.evaluate(
    async ([command, args]) => {
      const reply = await window.HybridWebView.InvokeDotNet('Invoke', [command, JSON.stringify(args ?? null)])
      return typeof reply === 'string' ? JSON.parse(reply) : reply
    },
    [command, args],
  )
}

const { page } = await connect()
try {
  switch (step) {
    case 'version': {
      const settings = await invoke(page, 'settings.get')
      if (!settings.ok) throw new Error(`settings.get failed: ${JSON.stringify(settings)}`)
      console.log(settings.value.version)
      break
    }
    case 'start-share': {
      // Not awaited: the answer comes once the file dialog closes. Events are kept from now on.
      await page.evaluate(() => {
        window.__checkEvents = []
        window.addEventListener('HybridWebViewMessageReceived', (event) => {
          try {
            window.__checkEvents.push(JSON.parse(event.detail.message))
          } catch {
            // Not an event.
          }
        })
        window.__checkAdd = window.HybridWebView.InvokeDotNet('Invoke', ['shares.add', 'null'])
          .then((reply) => (typeof reply === 'string' ? JSON.parse(reply) : reply))
      })
      console.log('asked for the file dialog')
      break
    }
    case 'shared': {
      const added = await page.evaluate(() => window.__checkAdd)
      if (!added?.ok || added.value.share?.name !== name) throw new Error(`shares.add answered ${JSON.stringify(added)}`)
      // The screen hears about it as an event, and the list has it.
      await page.waitForFunction(
        (name) => window.__checkEvents.some((one) => one.event === 'shares.changed' && one.payload.some((share) => share.name === name)),
        name,
        { timeout: 60_000 },
      )
      const listed = await invoke(page, 'shares.list')
      if (!listed.ok || !listed.value.some((share) => share.name === name)) throw new Error(`shares.list answered ${JSON.stringify(listed)}`)
      console.log(`shared ${name}`)
      break
    }
    case 'quit': {
      const answer = await invoke(page, 'app.quit', { force: true })
      console.log(`quit: ${JSON.stringify(answer)}`)
      break
    }
    default:
      throw new Error(`Unknown step: ${step}`)
  }
} catch (error) {
  console.error(error)
  process.exit(1)
}
// Lets go of the debugging connection without closing anything; the app keeps running.
process.exit(0)
