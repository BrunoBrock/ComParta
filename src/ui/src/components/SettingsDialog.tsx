import { useState } from 'react'
import { formatRate, type Language, languageNames, languages, t } from '../i18n/index.ts'
import { readHelper } from '../helper.ts'
import { maxPublicServers, readPublicServers } from '../publicServers.ts'
import Button from './ui/Button.tsx'
import Dialog from './ui/Dialog.tsx'

export type Theme = 'system' | 'light' | 'dark'
const themes: readonly Theme[] = ['system', 'light', 'dark']
/** As core's MaxNameLength: the name has to fit in an invite message. */
export const maxNameLength = 40

/** The most ComParta receives and sends each second, in bytes; null for no limit (epic 14 story 4). */
export interface SpeedLimits {
  receive: number | null
  send: number | null
}
/** The limits offered, from 256 KB to 20 MB a second. */
const speedChoices = [256 * 1024, 512 * 1024, 1024 ** 2, 2 * 1024 ** 2, 5 * 1024 ** 2, 10 * 1024 ** 2, 20 * 1024 ** 2]

interface SettingsDialogProps {
  language: Language
  onLanguage: (language: Language) => void
  startWithWindows: boolean
  onStartWithWindows: (on: boolean) => void
  publicServers: readonly string[]
  /** Saves the servers; true once they are stored. */
  onPublicServers: (servers: string[]) => Promise<boolean>
  helperAddress: string | null
  helperKey: string | null
  /** Saves the helper computer, or turns it off with null; true once it is stored. */
  onHelper: (helper: { address: string; key: string } | null) => Promise<boolean>
  yourName: string
  /** Saves the name others see in invites; true once it is stored. */
  onYourName: (name: string) => Promise<boolean>
  theme: Theme
  onTheme: (theme: Theme) => void
  /** The step-by-step help runs when the person shares a file. */
  guidedSend: boolean
  onGuidedSend: (on: boolean) => void
  speedLimits: SpeedLimits
  onSpeedLimits: (limits: SpeedLimits) => void
  /** The name of the folder new received files go to, never its path (AD-7). */
  receivedFolder: string
  /** The person chose that folder; false while it is the usual one. */
  receivedFolderChosen: boolean
  /** Opens Windows' folder dialog; the problem to show, or null when it went well or was closed. */
  onChooseReceivedFolder: () => Promise<string | null>
  /** New files go to the usual folder again; the problem to show, or null. */
  onUsualReceivedFolder: () => Promise<string | null>
  version: string
  onClose: () => void
}

/**
 * The Settings page (epic 12 story 1, UX design, Settings): your name, the language, the theme,
 * start with Windows, the guided send, where received files go (story 2), the public servers that
 * help others find shared files, the helper computer that passes them on (AD-23) and About.
 */
export default function SettingsDialog({
  language,
  onLanguage,
  startWithWindows,
  onStartWithWindows,
  publicServers,
  onPublicServers,
  helperAddress,
  helperKey,
  onHelper,
  yourName,
  onYourName,
  theme,
  onTheme,
  guidedSend,
  onGuidedSend,
  speedLimits,
  onSpeedLimits,
  receivedFolder,
  receivedFolderChosen,
  onChooseReceivedFolder,
  onUsualReceivedFolder,
  version,
  onClose,
}: SettingsDialogProps) {
  const [folderProblem, setFolderProblem] = useState<string | null>(null)
  const [name, setName] = useState(yourName)
  const [nameProblem, setNameProblem] = useState(false)
  const [address, setAddress] = useState(helperAddress ?? '')
  const [key, setKey] = useState(helperKey ?? '')
  const [helperProblem, setHelperProblem] = useState(false)
  const [servers, setServers] = useState(() => publicServers.join('\n'))
  const [problem, setProblem] = useState<string | null>(null)

  async function saveServers() {
    const read = readPublicServers(servers)
    if ('invalid' in read) return setProblem(t('settings.serversInvalid', { line: read.invalid }))
    if ('tooMany' in read) return setProblem(t('settings.serversTooMany', { count: maxPublicServers }))
    setProblem(null)
    if (await onPublicServers(read.servers)) setServers(read.servers.join('\n'))
  }

  async function saveName() {
    // As core: control characters are refused; the length is kept by the field itself.
    // eslint-disable-next-line no-control-regex
    if (/[\u0000-\u001f\u007f-\u009f]/.test(name) || name.trim().length > maxNameLength) return setNameProblem(true)
    setNameProblem(false)
    if (await onYourName(name)) setName(name.trim())
  }

  async function saveHelper() {
    const read = readHelper(address, key)
    if ('invalid' in read) return setHelperProblem(true)
    setHelperProblem(false)
    if (await onHelper(read.helper)) {
      setAddress(read.helper?.address ?? '')
      setKey(read.helper?.key ?? '')
    }
  }

  return (
    <Dialog labelledBy="settings-title" onClose={onClose}>
      <h2 id="settings-title">{t('settings.title')}</h2>
      <label htmlFor="settings-name">{t('settings.name')}</label>
      <div className="settings-row">
        <input
          id="settings-name"
          type="text"
          autoComplete="name"
          maxLength={maxNameLength}
          value={name}
          placeholder={t('invite.someone')}
          aria-describedby="settings-name-hint"
          aria-invalid={nameProblem}
          onChange={(event) => setName(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') void saveName()
          }}
        />
        <Button onClick={() => void saveName()}>{t('settings.nameSave')}</Button>
      </div>
      <p id="settings-name-hint" className="hint">
        {t('settings.nameHint')}
      </p>
      {nameProblem && (
        <p role="alert" className="settings-problem">
          {t('settings.nameInvalid')}
        </p>
      )}
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
      <fieldset className="settings-theme">
        <legend>{t('settings.theme')}</legend>
        {themes.map((option) => (
          <label key={option} className="settings-check">
            <input
              type="radio"
              name="settings-theme"
              value={option}
              checked={theme === option}
              onChange={() => onTheme(option)}
            />
            {t(`settings.theme.${option}`)}
          </label>
        ))}
      </fieldset>
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
      <label className="settings-check">
        <input
          type="checkbox"
          checked={guidedSend}
          aria-describedby="settings-guided-hint"
          onChange={(event) => onGuidedSend(event.target.checked)}
        />
        {t('settings.guidedSend')}
      </label>
      <p id="settings-guided-hint" className="hint">
        {t('settings.guidedSendHint')}
      </p>
      <fieldset className="settings-received" aria-describedby="settings-received-hint">
        <legend>{t('settings.receivedFolder')}</legend>
        <p className="settings-received-name">{receivedFolder}</p>
        <p id="settings-received-hint" className="hint">
          {t('settings.receivedFolderHint')}
        </p>
        {folderProblem && (
          <p role="alert" className="settings-problem">
            {folderProblem}
          </p>
        )}
        <div className="settings-row">
          <Button onClick={() => void onChooseReceivedFolder().then(setFolderProblem)}>{t('settings.receivedFolderChange')}</Button>
          {receivedFolderChosen && (
            <Button onClick={() => void onUsualReceivedFolder().then(setFolderProblem)}>{t('settings.receivedFolderReset')}</Button>
          )}
        </div>
      </fieldset>
      <fieldset className="settings-speed" aria-describedby="settings-speed-hint">
        <legend>{t('settings.speed')}</legend>
        <p id="settings-speed-hint" className="hint">
          {t('settings.speedHint')}
        </p>
        {(['receive', 'send'] as const).map((way) => (
          <SpeedChoice
            key={way}
            id={`settings-${way}-limit`}
            label={t(way === 'receive' ? 'settings.receiveLimit' : 'settings.sendLimit')}
            limit={speedLimits[way]}
            onChange={(limit) => onSpeedLimits({ ...speedLimits, [way]: limit })}
          />
        ))}
      </fieldset>
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
      <fieldset className="settings-helper" aria-describedby="settings-helper-hint">
        <legend>{t('settings.helper')}</legend>
        <p id="settings-helper-hint" className="hint">
          {t('settings.helperHint')}
        </p>
        <label htmlFor="settings-helper-address">{t('settings.helperAddress')}</label>
        <input
          id="settings-helper-address"
          type="url"
          spellCheck={false}
          autoComplete="off"
          value={address}
          aria-invalid={helperProblem}
          onChange={(event) => setAddress(event.target.value)}
        />
        <label htmlFor="settings-helper-key">{t('settings.helperKey')}</label>
        <input
          id="settings-helper-key"
          type="password"
          spellCheck={false}
          autoComplete="off"
          value={key}
          aria-invalid={helperProblem}
          onChange={(event) => setKey(event.target.value)}
        />
        {helperProblem && (
          <p role="alert" className="settings-problem">
            {t('settings.helperInvalid')}
          </p>
        )}
        <Button onClick={() => void saveHelper()}>{t('settings.helperSave')}</Button>
      </fieldset>
      <section className="settings-about" aria-labelledby="settings-about-title">
        <h3 id="settings-about-title">{t('settings.about')}</h3>
        {version && <p>{t('settings.version', { version })}</p>}
        <p>{t('settings.openSource')}</p>
      </section>
      <div className="dialog-actions">
        <Button onClick={onClose} autoFocus>
          {t('settings.close')}
        </Button>
      </div>
    </Dialog>
  )
}

interface SpeedChoiceProps {
  id: string
  label: string
  limit: number | null
  onChange: (limit: number | null) => void
}

export function SpeedChoice({ id, label, limit, onChange }: SpeedChoiceProps) {
  // A limit stored by an earlier version that is not offered any more still shows as it is.
  const offered = limit === null || speedChoices.includes(limit) ? speedChoices : [...speedChoices, limit].sort((a, b) => a - b)
  return (
    <>
      <label htmlFor={id}>{label}</label>
      <select
        id={id}
        className="settings-select"
        value={limit ?? ''}
        onChange={(event) => onChange(event.target.value === '' ? null : Number(event.target.value))}
      >
        <option value="">{t('settings.noLimit')}</option>
        {offered.map((choice) => (
          <option key={choice} value={choice}>
            {formatRate(choice)}
          </option>
        ))}
      </select>
    </>
  )
}
