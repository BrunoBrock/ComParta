import type { ReactNode } from 'react'
import Dialog from './Dialog.tsx'

interface SheetProps {
  labelledBy: string
  onClose: () => void
  children: ReactNode
}

/** A dialog that rises from the bottom and fills a phone screen; on wide windows, a side panel. */
export default function Sheet({ labelledBy, onClose, children }: SheetProps) {
  return (
    <Dialog labelledBy={labelledBy} className="sheet" onClose={onClose}>
      {children}
    </Dialog>
  )
}
