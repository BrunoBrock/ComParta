import { act, render } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Bridge } from '../../bridge/index.ts'
import Thumbnail from './Thumbnail.tsx'

const picture = 'data:image/png;base64,AQID'

function bridgeAnswering(value: string | null) {
  let answer: (result: { ok: true; value: string | null }) => void = () => {}
  const invoke = vi.fn(() => new Promise((resolve) => (answer = resolve)))
  const bridge = { invoke, subscribe: () => () => {} } as unknown as Bridge
  return { bridge, invoke, answer: () => act(async () => answer({ ok: true, value })) }
}

afterEach(() => vi.unstubAllGlobals())

describe('Thumbnail', () => {
  it('shows the file icon until the picture arrives', async () => {
    const { bridge, invoke, answer } = bridgeAnswering(picture)
    const { container } = render(<Thumbnail bridge={bridge} kind="share" id="s1" name="Beach.jpg" ready />)

    expect(container.querySelector('.file-icon')).not.toBeNull()
    expect(invoke).toHaveBeenCalledWith('thumbnails.get', { kind: 'share', id: 's1', size: 240 })
    await answer()

    expect(container.querySelector('img')).toHaveAttribute('src', picture)
    expect(container.querySelector('img')).toHaveAttribute('alt', '')
  })

  it('keeps the icon when the system has no picture', async () => {
    const { bridge, answer } = bridgeAnswering(null)
    const { container } = render(<Thumbnail bridge={bridge} kind="transfer" id="t1" name="Clip.mp4" ready />)
    await answer()

    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('.file-icon')).not.toBeNull()
  })

  it('does not ask for files that are not photos or videos, or not there yet', () => {
    const { bridge, invoke } = bridgeAnswering(picture)
    render(<Thumbnail bridge={bridge} kind="share" id="s1" name="Letter.pdf" ready />)
    render(<Thumbnail bridge={bridge} kind="transfer" id="t1" name="Clip.mp4" ready={false} />)

    expect(invoke).not.toHaveBeenCalled()
  })

  it('waits until the card is on screen', async () => {
    let seen: (entries: { isIntersecting: boolean }[]) => void = () => {}
    const disconnect = vi.fn()
    vi.stubGlobal(
      'IntersectionObserver',
      class {
        constructor(callback: typeof seen) {
          seen = callback
        }
        observe() {}
        disconnect = disconnect
      },
    )
    const { bridge, invoke } = bridgeAnswering(picture)
    render(<Thumbnail bridge={bridge} kind="share" id="s1" name="Beach.jpg" ready />)

    expect(invoke).not.toHaveBeenCalled()
    seen([{ isIntersecting: false }])
    expect(invoke).not.toHaveBeenCalled()
    seen([{ isIntersecting: true }])

    expect(invoke).toHaveBeenCalledTimes(1)
    expect(disconnect).toHaveBeenCalled()
  })
})
