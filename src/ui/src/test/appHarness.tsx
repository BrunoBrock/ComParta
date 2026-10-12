/** What every screen test shares: a fresh app on the fake bridge, sample files, and ways around the screen. */
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach } from 'vitest'
import App from '../App.tsx'
import type { ShareDto } from '../bridge/contract.generated.ts'
import { createFakeBridge, type FakeBridgeOptions } from '../bridge/fakeBridge.ts'
import { en } from '../i18n/en.ts'
import { setLanguage } from '../i18n/index.ts'

export const video: ShareDto = { id: 's1', name: 'Birthday party.mp4', sizeBytes: 1_288_490_188, status: 'ready' }
export const album: ShareDto = { id: 's2', name: 'Holiday photos.zip', sizeBytes: 2048, status: 'unavailable' }

beforeEach(() => {
  localStorage.clear()
  setLanguage('en')
})

export function setup(options: FakeBridgeOptions = {}) {
  // The guided send is tested on its own; elsewhere Share files goes straight to the file dialog.
  const bridge = createFakeBridge({ sendStepsSeen: 3, ...options })
  render(<App bridge={bridge} />)
  return { bridge, user: userEvent.setup() }
}

export type User = ReturnType<typeof userEvent.setup>

export const sharesSection = () => screen.getByRole('region', { name: en['shares.heading'] })
export const transfersSection = () => screen.getByRole('region', { name: en['transfers.heading'] })
export const navButton = (label: string) =>
  within(screen.getByRole('navigation', { name: en['nav.label'] })).getByRole('button', {
    name: new RegExp(`^${label}`),
  })
/** One of the rail's two actions; empty sections offer the same buttons, so the rail is named. */
export const railButton = async (label: string) =>
  within(await screen.findByRole('complementary')).getByRole('button', { name: label })
export const goToReceived = (user: User) => user.click(navButton(en['nav.received']))

/** Types the text into the receive box and continues; stops at the confirmation or the problem. */
export async function enterInvite(user: User, invite: string) {
  await user.click(await railButton(en['transfers.open']))
  const dialog = screen.getByRole('dialog', { name: en['receive.title'] })
  await user.type(within(dialog).getByLabelText(en['transfers.inviteLabel']), invite)
  await user.click(within(dialog).getByRole('button', { name: en['receive.continue'] }))
}

export async function pasteInvite(user: User, invite: string) {
  await enterInvite(user, invite)
  const confirm = await screen.findByRole('dialog', { name: en['confirm.title'] })
  await user.click(within(confirm).getByRole('button', { name: en['transfers.receive'] }))
}
