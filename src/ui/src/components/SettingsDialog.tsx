import { type Language, languageNames, languages, t } from '../i18n/index.ts'
import Button from './ui/Button.tsx'
import Dialog from './ui/Dialog.tsx'

interface SettingsDialogProps {
  language: Language
  onLanguage: (language: Language) => void
  onClose: () => void
}

/** The settings there are so far: the language. Epic 12 adds the rest (UX design, Settings). */
export default function SettingsDialog({ language, onLanguage, onClose }: SettingsDialogProps) {
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
      <div className="dialog-actions">
        <Button onClick={onClose} autoFocus>
          {t('settings.close')}
        </Button>
      </div>
    </Dialog>
  )
}
