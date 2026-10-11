import { Languages } from 'lucide-react'
import { type Language, languageNames, languages, t } from '../i18n/index.ts'

interface LanguageMenuProps {
  language: Language
  onChange: (language: Language) => void
}

/** Until the Settings screen exists (epic 12), the language is chosen here, in the header. */
export default function LanguageMenu({ language, onChange }: LanguageMenuProps) {
  return (
    <label className="language-menu">
      <Languages size={18} aria-hidden="true" />
      <span className="visually-hidden">{t('language.label')}</span>
      <select value={language} onChange={(event) => onChange(event.target.value as Language)}>
        {languages.map((option) => (
          <option key={option} value={option} lang={option}>
            {languageNames[option]}
          </option>
        ))}
      </select>
    </label>
  )
}
