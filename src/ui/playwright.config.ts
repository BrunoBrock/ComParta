import { defineConfig } from '@playwright/test'

/**
 * The screen in a real browser engine at a phone's size (epic 7 story 4): what jsdom cannot see,
 * such as layout, sideways scrolling, touch target sizes and colour contrast. It runs the page on
 * the fake bridge, as `npm run dev` does. Edge by default, the engine of the Windows app's web view;
 * set CHROMIUM_PATH to use another Chromium instead.
 */
const chromium = process.env.CHROMIUM_PATH

export default defineConfig({
  testDir: './e2e',
  testMatch: '*.e2e.ts',
  timeout: 60_000,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? 'line' : 'list',
  use: {
    baseURL: 'http://localhost:5179/',
    channel: chromium ? undefined : 'msedge',
    launchOptions: chromium ? { executablePath: chromium } : {},
    // A small Android phone: 360 by 740 CSS pixels, touch, no mouse.
    viewport: { width: 360, height: 740 },
    hasTouch: true,
    isMobile: true,
    deviceScaleFactor: 2,
    locale: 'en-US',
  },
  webServer: {
    command: 'npx vite --port 5179 --strictPort',
    url: 'http://localhost:5179/',
    reuseExistingServer: !process.env.CI,
  },
})
