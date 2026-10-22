import { useEffect, useState } from 'react'
import type { Bridge, ErrorCode, ReceivedFileDto, TransferDto } from '../bridge/index.ts'
import { formatSize, t } from '../i18n/index.ts'
import Dialog from './ui/Dialog.tsx'

interface ReceivedFolderDialogProps {
  bridge: Bridge
  transfer: TransferDto
  onError: (code: ErrorCode) => void
  onClose: () => void
}

/** The files inside a received folder, with their places in it; the folder itself can be shown in its folder. */
export default function ReceivedFolderDialog({ bridge, transfer, onError, onClose }: ReceivedFolderDialogProps) {
  const [files, setFiles] = useState<ReceivedFileDto[] | null>(null)

  useEffect(() => {
    let active = true
    bridge.invoke('received.files', { id: transfer.id }).then((result) => {
      if (!active) return
      if (result.ok) setFiles(result.value)
      else onError(result.error.code)
    })
    return () => {
      active = false
    }
  }, [bridge, transfer.id, onError])

  async function reveal() {
    const result = await bridge.invoke('received.reveal', { id: transfer.id })
    if (!result.ok) onError(result.error.code)
  }

  return (
    <Dialog labelledBy="received-folder-title" className="received-folder" onClose={onClose}>
      <h2 id="received-folder-title">{t('folderView.title', { name: transfer.name ?? '' })}</h2>
      {files !== null &&
        (files.length === 0 ? (
          <p>{t('folderView.empty')}</p>
        ) : (
          <ul className="received-folder-files">
            {files.map((file) => (
              <li key={file.relativePath}>
                <span>{file.relativePath}</span> <span>{formatSize(file.sizeBytes)}</span>
              </li>
            ))}
          </ul>
        ))}
      <div className="dialog-actions">
        <button type="button" onClick={reveal}>
          {t('received.reveal')}
        </button>
        <button type="button" className="primary" onClick={onClose} autoFocus>
          {t('folderView.close')}
        </button>
      </div>
    </Dialog>
  )
}
