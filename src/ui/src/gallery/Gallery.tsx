import { Copy, Download, FolderOpen, Inbox, Info, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import Button from '../components/ui/Button.tsx'
import Card from '../components/ui/Card.tsx'
import Dialog from '../components/ui/Dialog.tsx'
import EmptyState from '../components/ui/EmptyState.tsx'
import FileIcon from '../components/ui/FileIcon.tsx'
import IconButton from '../components/ui/IconButton.tsx'
import ProgressBar from '../components/ui/ProgressBar.tsx'
import Row from '../components/ui/Row.tsx'
import Sheet from '../components/ui/Sheet.tsx'
import StatusChip from '../components/ui/StatusChip.tsx'
import Toast from '../components/ui/Toast.tsx'

const sampleNames = ['Birthday party.mp4', 'Holiday photos.zip', 'Recipe.pdf', 'Song.mp3', 'Notes.txt', 'Picture.jpg']

/**
 * Every base component in every state, for building and checking the design (epic 8 story 1).
 * Development only: open the app with `?gallery`. The production build leaves it out.
 */
export default function Gallery() {
  const [dialog, setDialog] = useState(false)
  const [sheet, setSheet] = useState(false)
  const [toast, setToast] = useState(true)

  return (
    <main className="gallery">
      <h1>Components</h1>

      <section aria-labelledby="g-buttons">
        <h2 id="g-buttons">Buttons</h2>
        <div className="gallery-row">
          <Button variant="primary" icon={Plus}>
            Share a file
          </Button>
          <Button icon={Download}>Receive or download</Button>
          <Button variant="quiet" icon={FolderOpen}>
            Open folder
          </Button>
          <Button variant="danger" icon={Trash2}>
            Delete
          </Button>
          <Button variant="primary" disabled>
            Not now
          </Button>
          <IconButton icon={Info} label="Details" />
        </div>
      </section>

      <section aria-labelledby="g-chips">
        <h2 id="g-chips">Status chips</h2>
        <div className="gallery-row">
          <StatusChip tone="ok" text="Ready to send" />
          <StatusChip tone="busy" text="Getting ready" />
          <StatusChip tone="warn" text="Not found" />
          <StatusChip tone="paused" text="Paused" />
        </div>
      </section>

      <section aria-labelledby="g-progress">
        <h2 id="g-progress">Progress</h2>
        <ProgressBar label="Known size" value={45} max={100} />
        <ProgressBar label="Size not known yet" />
      </section>

      <section aria-labelledby="g-icons">
        <h2 id="g-icons">File icons</h2>
        <div className="gallery-row">
          {sampleNames.map((name) => (
            <span key={name} className="gallery-icon">
              <FileIcon name={name} />
              <span>{name}</span>
            </span>
          ))}
        </div>
      </section>

      <section aria-labelledby="g-surfaces">
        <h2 id="g-surfaces">Card and rows</h2>
        <Card>A card holds one thing.</Card>
        <div className="gallery-rows">
          <Row>
            <span>First row</span>
            <Button variant="quiet" icon={Copy}>
              Copy
            </Button>
          </Row>
          <Row>
            <span>Second row</span>
          </Row>
        </div>
      </section>

      <section aria-labelledby="g-empty">
        <h2 id="g-empty">Empty state</h2>
        <EmptyState
          icon={Inbox}
          text="Nothing here yet."
          action={
            <Button variant="primary" icon={Plus}>
              Share a file
            </Button>
          }
        />
      </section>

      <section aria-labelledby="g-windows">
        <h2 id="g-windows">Windows</h2>
        <div className="gallery-row">
          <Button onClick={() => setDialog(true)}>Open a dialog</Button>
          <Button onClick={() => setSheet(true)}>Open a sheet</Button>
          <Button onClick={() => setToast(true)}>Show a toast</Button>
        </div>
      </section>

      {dialog && (
        <Dialog labelledBy="g-dialog" onClose={() => setDialog(false)}>
          <h2 id="g-dialog">A dialog</h2>
          <p>Escape closes it and focus goes back to the button.</p>
          <div className="dialog-actions">
            <Button onClick={() => setDialog(false)} autoFocus>
              Close
            </Button>
          </div>
        </Dialog>
      )}
      {sheet && (
        <Sheet labelledBy="g-sheet" onClose={() => setSheet(false)}>
          <h2 id="g-sheet">A sheet</h2>
          <Button onClick={() => setSheet(false)}>Close</Button>
        </Sheet>
      )}
      {toast && <Toast text="Invite copied" closeLabel="Close this message" onClose={() => setToast(false)} />}
    </main>
  )
}
