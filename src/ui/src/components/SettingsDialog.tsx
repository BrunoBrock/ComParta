import { useState } from 'react'
import { type Language, languageNames, languages, t } from '../i18n/index.ts'
import { maxPublicServers, readPublicServers } from '../publicServers.ts'
import Button from './ui/Button.tsx'
import Dialog from './ui/Dialog.tsx'

interface SettingsDialogProps {
  language: Language
  onLanguage: (language: Language) => void
  startWithWindows: boolean
  onStartWithWindows: (on: boolean) => void
  publicServers: readonly string[]
  /** Saves the servers; true once they are stored. */
  onPublicServers: (servers: string[]) => Promise<boolean>
  onClose: () => void
}

/**
 * The settings there are so far: the language, start with Windows and the public servers that
 * help others find shared files. Epic 12 adds the rest (UX design, Settings).
 */
export default function SettingsDialog({
  language,
  onLanguage,
  startWithWindows,
  onStartWithWindows,
  publicServers,
  onPublicServers,
  onClose,
}: SettingsDialogProps) {
  const [servers, setServers] = useState(() => publicServers.join('\n'))
  const [problem, setProblem] = useState<string | null>(null)

  async function saveServers() {
    const read = readPublicServers(servers)
    if ('invalid' in read) return setProblem(t('settings.serversInvalid', { line: read.invalid }))
    if ('tooMany' in read) return setProblem(t('settings.serversTooMany', { count: maxPublicServers }))
    setProblem(null)
    if (await onPublicServers(read.servers)) setServers(read.servers.join('\n'))
  }

  return (
    <Dialog labelledBy="settings-title" onClose={onClose}>
      <h2 id="settings-title">{t('settings.title')}</h2>
      <label htmlFor="settings-language">{t('language.label')}</label>
      <select
        id="settings-language"
        className="settings-select"
        value={language}
        aria-describedby="settings-language-hint"
        onChange={(event) => onLanguage(event.target.value as Language)}
      >
        {languages.map((option) => (
          <option key={option} value={option} lang={option}>
            {languageNames[option]}
          </option>
        ))}
      </select>
      <p id="settings-language-hint" className="hint">
        {t('settings.languageHint')}
      </p>
      <label className="settings-check">
        <input
          type="checkbox"
          checked={startWithWindows}
          aria-describedby="settings-start-hint"
          onChange={(event) => onStartWithWindows(event.target.checked)}
        />
        {t('settings.startWithWindows')}
      </label>
      <p id="settings-start-hint" className="hint">
        {t('settings.startWithWindowsHint')}
      </p>
      <label htmlFor="settings-servers">{t('settings.servers')}</label>
      <textarea
        id="settings-servers"
        rows={3}
        spellCheck={false}
        value={servers}
        aria-describedby="settings-servers-hint"
        aria-invalid={problem !== null}
        onChange={(event) => setServers(event.target.value)}
      />
      <p id="settings-servers-hint" className="hint">
        {t('settings.serversHint')}
      </p>
      {problem && (
        <p role="alert" className="settings-problem">
          {problem}
        </p>
      )}
      <Button onClick={() => void saveServers()}>{t('settings.serversSave')}</Button>
      <div className="dialog-actions">
        <Button onClick={onClose} autoFocus>
          {t('settings.close')}
        </Button>
      </div>
    </Dialog>
  )
}
