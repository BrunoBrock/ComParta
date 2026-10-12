/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

// Read from disk: the test runner does not load stylesheets.
const css = readFileSync(join(import.meta.dirname, 'tokens.css'), 'utf8')

// The tokens as written: the light :root block, the explicit dark block and the system dark block.

function block(selector: string): Record<string, string> {
  const start = css.indexOf(`${selector} {`)
  const body = css.slice(start, css.indexOf('}', start))
  return Object.fromEntries([...body.matchAll(/(--color-[\w-]+):\s*(#[0-9a-f]{6})/gi)].map((m) => [m[1], m[2]]))
}

const light = block(':root')
const dark = { ...light, ...block(":root[data-theme='dark']") }

function luminance(hex: string) {
  const channel = (i: number) => {
    const value = parseInt(hex.slice(i, i + 2), 16) / 255
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5)
}

function contrast(a: string, b: string) {
  const [high, low] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (high + 0.05) / (low + 0.05)
}

// Every text-on-background pair the screen uses; all are text, so all need 4.5:1 (WCAG 2.2 AA).
const pairs: [string, string][] = [
  ['--color-text', '--color-bg'],
  ['--color-text', '--color-surface'],
  ['--color-text', '--color-primary-soft'],
  ['--color-text', '--color-warn-soft'],
  ['--color-text', '--color-info-soft'],
  ['--color-text-muted', '--color-bg'],
  ['--color-text-muted', '--color-surface'],
  ['--color-text-muted', '--color-warn-soft'],
  ['--color-on-primary', '--color-primary'],
  ['--color-on-danger', '--color-danger'],
  ['--color-primary', '--color-surface'],
  ['--color-primary', '--color-primary-soft'],
  ['--color-ok', '--color-surface'],
  ['--color-busy', '--color-surface'],
  ['--color-warn', '--color-surface'],
  ['--color-warn', '--color-warn-soft'],
  ['--color-danger', '--color-surface'],
  ['--color-bg', '--color-text'],
]

describe.each([
  ['light', light],
  ['dark', dark],
])('the %s colours', (_, theme) => {
  it.each(pairs)('%s on %s is readable', (text, background) => {
    expect(theme[text], text).toBeDefined()
    expect(theme[background], background).toBeDefined()
    expect(contrast(theme[text], theme[background])).toBeGreaterThanOrEqual(4.5)
  })

  it('has the same tokens as the other theme', () => {
    expect(Object.keys(theme).sort()).toEqual(Object.keys(light).sort())
  })
})

it('uses the same dark colours whether the system or the person chose dark', () => {
  const start = css.indexOf("@media (prefers-color-scheme: dark)")
  const system = Object.fromEntries(
    [...css.slice(start, css.indexOf("}\n}", start)).matchAll(/(--color-[\w-]+):\s*(#[0-9a-f]{6})/gi)].map((m) => [m[1], m[2]]),
  )
  expect(system).toEqual(block(":root[data-theme='dark']"))
})
