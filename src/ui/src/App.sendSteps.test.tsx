import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { railButton, setup, sharesSection } from './test/appHarness.tsx'
import { accessibilityProblems } from './test/axe.ts'

const steps = () => screen.getByRole('dialog', { name: /./ })

describe('the guided first send', () => {
  it('goes from getting ready to the invite, and copies the message', async () => {
    const { bridge, user } = setup({ sendStepsSeen: 0, stepMs: 200 })

    await user.click(await railButton(en['shares.add']))

    // The fake takes 400 ms to get the file ready, so step 2 is seen before step 3.
    expect(await within(steps()).findByRole('heading', { name: en['steps.ready'] })).toBeInTheDocument()
    expect(screen.getByText('Step 2 of 3')).toBeInTheDocument()
    expect(within(steps()).getByRole('progressbar', { name: en['steps.ready'] })).toBeInTheDocument()

    expect(await screen.findByText('Step 3 of 3')).toBeInTheDocument()
    expect(within(steps()).getByText(/wants to send you/)).toBeInTheDocument()
    expect(await accessibilityProblems(steps())).toEqual([])
    await user.click(within(steps()).getByRole('button', { name: en['steps.copy'] }))

    expect(within(steps()).getByRole('button', { name: en['steps.copied'] })).toBeInTheDocument()
    expect(bridge.copiedInvite).toMatch(/wants to send you/)
    await user.click(within(steps()).getByRole('button', { name: en['steps.done'] }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    const stored = await bridge.invoke('settings.get')
    expect(stored.ok && stored.value.sendStepsSeen).toBe(1)
  })

  it('shows the steps for three sends, then goes straight to the file dialog', async () => {
    const { bridge, user } = setup({ sendStepsSeen: 2 })

    await user.click(await railButton(en['shares.add']))
    await user.click(await within(steps()).findByRole('button', { name: en['steps.done'] }))
    await waitFor(async () => {
      const stored = await bridge.invoke('settings.get')
      expect(stored.ok && stored.value.sendStepsSeen).toBe(3)
    })

    await user.click(await railButton(en['shares.add']))

    await waitFor(() => expect(within(sharesSection()).getAllByRole('listitem')).toHaveLength(2))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('stops showing the steps once the person opts out', async () => {
    const { bridge, user } = setup({ sendStepsSeen: 0 })

    await user.click(await railButton(en['shares.add']))
    await user.click(await within(steps()).findByRole('checkbox', { name: en['steps.hide'] }))
    await user.click(within(steps()).getByRole('button', { name: en['steps.done'] }))

    await waitFor(async () => {
      const stored = await bridge.invoke('settings.get')
      expect(stored.ok && stored.value.sendStepsSeen).toBe(3)
    })
    await user.click(await railButton(en['shares.add']))
    await waitFor(() => expect(within(sharesSection()).getAllByRole('listitem')).toHaveLength(2))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('closes with nothing shared when the person cancels the file dialog', async () => {
    const { bridge, user } = setup({ sendStepsSeen: 0 })
    bridge.nextPick = null

    await user.click(await railButton(en['shares.add']))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText(en['shares.empty'])).toBeInTheDocument()
    const stored = await bridge.invoke('settings.get')
    expect(stored.ok && stored.value.sendStepsSeen).toBe(0)
  })

  it('closes and explains when the file cannot be shared', async () => {
    const { bridge, user } = setup({ sendStepsSeen: 0 })
    bridge.nextPick = { name: 'Empty.txt', sizeBytes: 0 }

    await user.click(await railButton(en['shares.add']))

    expect(await screen.findByRole('alert')).toHaveTextContent(en['error.file.unreadable'])
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
