import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { toastMs } from '../components/ui/Toast.tsx'
import { accessibilityProblems } from '../test/axe.ts'
import Gallery from './Gallery.tsx'

describe('the component gallery', () => {
  it('shows every component with no accessibility problem', async () => {
    render(<Gallery />)

    expect(await accessibilityProblems()).toEqual([])
  })

  it('opens and closes a dialog and a sheet, and gives focus back', async () => {
    const user = userEvent.setup()
    render(<Gallery />)
    const opener = screen.getByRole('button', { name: 'Open a dialog' })

    await user.click(opener)
    expect(screen.getByRole('dialog', { name: 'A dialog' })).toBeInTheDocument()
    expect(await accessibilityProblems()).toEqual([])
    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(opener).toHaveFocus()

    await user.click(screen.getByRole('button', { name: 'Open a sheet' }))
    expect(screen.getByRole('dialog', { name: 'A sheet' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('lets a toast go after six seconds, and shows it again on request', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    try {
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
      render(<Gallery />)
      expect(screen.getByRole('status')).toHaveTextContent('Invite copied')

      await act(() => vi.advanceTimersByTimeAsync(toastMs))
      expect(screen.queryByRole('status')).not.toBeInTheDocument()

      await user.click(screen.getByRole('button', { name: 'Show a toast' }))
      expect(screen.getByRole('status')).toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: 'Close this message' }))
      expect(screen.queryByRole('status')).not.toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })
})
