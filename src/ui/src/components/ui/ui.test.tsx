import { fireEvent, render, screen } from '@testing-library/react'
import { Plus } from 'lucide-react'
import { describe, expect, it, vi } from 'vitest'
import Button from './Button.tsx'
import Dialog from './Dialog.tsx'
import IconButton from './IconButton.tsx'
import ProgressBar from './ProgressBar.tsx'

describe('the base components', () => {
  it('gives a button its variant, and submits only when asked', () => {
    render(
      <form>
        <Button variant="primary" icon={Plus}>
          Share
        </Button>
        <Button submit>Send</Button>
      </form>,
    )

    expect(screen.getByRole('button', { name: 'Share' })).toHaveClass('primary')
    expect(screen.getByRole('button', { name: 'Share' })).toHaveAttribute('type', 'button')
    expect(screen.getByRole('button', { name: 'Send' })).toHaveAttribute('type', 'submit')
    expect(screen.getByRole('button', { name: 'Send' })).not.toHaveAttribute('class')
  })

  it('names an icon-only button for screen readers and as a tooltip', () => {
    render(<IconButton icon={Plus} label="Add" />)

    expect(screen.getByRole('button', { name: 'Add' })).toHaveAttribute('title', 'Add')
  })

  it('shows the whole percentage beside the bar, and none while the size is unknown', () => {
    const { rerender } = render(<ProgressBar label="Film" value={999} max={1000} />)
    expect(screen.getByText('99%')).toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: 'Film' })).toHaveAttribute('value', '999')

    rerender(<ProgressBar label="Film" />)
    expect(screen.queryByText(/%/)).not.toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: 'Film' })).not.toHaveAttribute('value')
  })

  it('ignores Escape while the dialog is busy', () => {
    const onClose = vi.fn()
    const { rerender } = render(
      <Dialog labelledBy="t" onClose={onClose} busy>
        <h2 id="t">Deleting</h2>
      </Dialog>,
    )
    fireEvent(screen.getByRole('dialog'), new Event('cancel', { cancelable: true }))
    expect(onClose).not.toHaveBeenCalled()

    rerender(
      <Dialog labelledBy="t" onClose={onClose}>
        <h2 id="t">Deleting</h2>
      </Dialog>,
    )
    fireEvent(screen.getByRole('dialog'), new Event('cancel', { cancelable: true }))
    expect(onClose).toHaveBeenCalledOnce()
  })
})
