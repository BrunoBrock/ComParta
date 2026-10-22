import { useEffect, useState } from 'react'
import type { Bridge, ErrorCode, TransferOptionsDto } from '../bridge/index.ts'
import { formatSize, t } from '../i18n/index.ts'
import { type SpeedLimits, SpeedChoice } from './SettingsDialog.tsx'
import Dialog from './ui/Dialog.tsx'

interface MoreOptionsDialogProps {
  bridge: Bridge
  id: string
  name: string
  /** It has arrived: the files can no longer change, and it is shared or not. */
  arrived: boolean
  speedLimits: SpeedLimits
  onSpeedLimits: (limits: SpeedLimits) => void
  onClose: () => void
  onError: (code: ErrorCode) => void
}

/** How often the open window asks again, so the files show as soon as they are known. */
export const optionsRefreshMs = 1000

/**
 * What a qBittorrent user looks for beyond progress and pause, kept out of the main screen
 * (epic 14 story 4): which files of a download to get, whether to share it once it has arrived,
 * and the speed limits for all of ComParta.
 */
export default function MoreOptionsDialog({ bridge, id, name, arrived, speedLimits, onSpeedLimits, onClose, onError }: MoreOptionsDialogProps) {
  const [options, setOptions] = useState<TransferOptionsDto | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let active = true
    const load = async () => {
      const result = await bridge.invoke('transfers.options', { id })
      if (active && result.ok) setOptions(result.value)
    }
    void load()
    const timer = setInterval(load, optionsRefreshMs)
    return () => {
      active = false
      clearInterval(timer)
    }
  }, [bridge, id])

  async function change(next: { skipped: number[] | null; keepSharing: boolean | null }, shown: TransferOptionsDto) {
    setBusy(true)
    setOptions(shown)
    try {
      const result = await bridge.invoke('transfers.setOptions', { id, ...next })
      if (!result.ok) onError(result.error.code)
      const fresh = await bridge.invoke('transfers.options', { id })
      if (fresh.ok) setOptions(fresh.value)
    } finally {
      setBusy(false)
    }
  }

  function toggleFile(index: number, wanted: boolean) {
    const files = options!.files!.map((file) => (file.index === index ? { ...file, wanted } : file))
    const skipped = files.filter((file) => !file.wanted).map((file) => file.index)
    void change({ skipped, keepSharing: null }, { ...options!, files })
  }

  const files = options?.files ?? null
  const wantedCount = files?.filter((file) => file.wanted).length ?? 0

  return (
    <Dialog labelledBy="options-title" className="details more-options" onClose={onClose}>
      <h2 id="options-title">{t('options.title', { name })}</h2>
      {options !== null && (
        <>
          {!arrived && (
            <fieldset className="options-files" aria-describedby="options-files-hint">
              <legend>{t('options.files')}</legend>
              {files === null ? (
                <p id="options-files-hint" className="hint">
                  {t('options.filesWaiting')}
                </p>
              ) : (
                <>
                  <p id="options-files-hint" className="hint">
                    {t('options.filesHint')}
                  </p>
                  <ul className="options-file-list">
                    {files.map((file) => (
                      <li key={file.index}>
                        <label>
                          <input
                            type="checkbox"
                            checked={file.wanted}
                            // The last file ticked stays ticked; cancelling the download gets none.
                            disabled={busy || (file.wanted && wantedCount === 1)}
                            onChange={(event) => toggleFile(file.index, event.target.checked)}
                          />
                          <span className="options-file-name">{file.path}</span>{' '}
                          <span className="hint">{formatSize(file.sizeBytes)}</span>
                        </label>
                      </li>
                    ))}
                  </ul>
                </>
              )}
            </fieldset>
          )}
          <fieldset className="options-sharing">
            <legend>{t('options.sharing')}</legend>
            {!arrived || options.sharing ? (
              <>
                <label className="settings-check">
                  <input
                    type="checkbox"
                    checked={options.keepSharing}
                    disabled={busy}
                    aria-describedby="options-sharing-hint"
                    onChange={(event) =>
                      void change(
                        { skipped: null, keepSharing: event.target.checked },
                        { ...options, keepSharing: event.target.checked },
                      )
                    }
                  />
                  {t('options.keepSharing')}
                </label>
                <p id="options-sharing-hint" className="hint">
                  {t('options.keepSharingHint')}
                </p>
              </>
            ) : (
              <p>{t('options.notShared')}</p>
            )}
          </fieldset>
        </>
      )}
      <fieldset className="settings-speed" aria-describedby="options-speed-hint">
        <legend>{t('options.speed')}</legend>
        <p id="options-speed-hint" className="hint">
          {t('options.speedHint')}
        </p>
        {(['receive', 'send'] as const).map((way) => (
          <SpeedChoice
            key={way}
            id={`options-${way}-limit`}
            label={t(way === 'receive' ? 'settings.receiveLimit' : 'settings.sendLimit')}
            limit={speedLimits[way]}
            onChange={(limit) => onSpeedLimits({ ...speedLimits, [way]: limit })}
          />
        ))}
      </fieldset>
      <div className="dialog-actions">
        <button type="button" className="primary" onClick={onClose} autoFocus>
          {t('options.close')}
        </button>
      </div>
    </Dialog>
  )
}
