import type { ErrorCode } from '../bridge/contract.generated.ts'
import { en, type Catalog, type MessageKey } from './en.ts'
import { ptBR } from './pt-BR.ts'

export type { MessageKey }

/** The languages ComParta speaks, in the order the language menu lists them. */
export const languages = ['en', 'pt-BR'] as const
export type Language = (typeof languages)[number]

/** Each language's own name, so a person can find theirs whatever is showing. */
export const languageNames: Record<Language, string> = { en: 'English', 'pt-BR': 'Português (Brasil)' }

export const catalogs: Record<Language, Catalog> = { en, 'pt-BR': ptBR }

let current: Language = 'en'

/** Switches every message and number to this language. The app re-renders after calling it. */
export function setLanguage(language: Language) {
  current = language
}

export function getLanguage(): Language {
  return current
}

/**
 * The saved choice when there is one, else the system language when ComParta speaks it (any
 * Portuguese gets Brazilian Portuguese), else English.
 */
export function pickLanguage(saved: string | null | undefined, system: string | null | undefined): Language {
  const known = (tag: string | null | undefined): Language | null => {
    if (!tag) return null
    const exact = languages.find((language) => language.toLowerCase() === tag.toLowerCase())
    if (exact) return exact
    return tag.toLowerCase().split('-')[0] === 'pt' ? 'pt-BR' : tag.toLowerCase().startsWith('en') ? 'en' : null
  }
  return known(saved) ?? known(system) ?? 'en'
}

/** Looks a message up in the current language's catalog and fills in its {placeholders}. */
export function t(key: MessageKey, values: Record<string, string | number> = {}): string {
  return catalogs[current][key].replace(/\{(\w+)\}/g, (whole, name: string) =>
    name in values ? String(values[name]) : whole,
  )
}

export function errorMessage(code: ErrorCode): string {
  return t(`error.${code}`)
}

const number = (value: number, digits: number) =>
  new Intl.NumberFormat(current, { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(value)

/** Sizes cross the bridge as whole bytes; the UI chooses how to show them. */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return t('size.bytes', { value: number(bytes, 0) })
  if (bytes < 1024 ** 2) return t('size.kb', { value: number(bytes / 1024, 0) })
  if (bytes < 1024 ** 3) return t('size.mb', { value: number(bytes / 1024 ** 2, 1) })
  return t('size.gb', { value: number(bytes / 1024 ** 3, 2) })
}

/** A speed, such as "2.4 MB/s". */
export function formatRate(bytesPerSecond: number): string {
  return t('speed.rate', { size: formatSize(bytesPerSecond) })
}

/** A moment the person will recognise, such as "Oct 12, 2026, 10:33 AM", in their language. */
export function formatDateTime(iso: string): string {
  return new Intl.DateTimeFormat(current, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso))
}

/** Whole percent, rounded down so a file never reads 100% before it has all arrived. */
export function percentOf(received: number, total: number | null): number {
  return total ? Math.floor((received / total) * 100) : 0
}

/** Rough on purpose: people want "about 5 minutes", not a countdown. */
export function formatTimeLeft(seconds: number): string {
  if (seconds < 60) return t('transfers.left.seconds')
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return minutes === 1 ? t('transfers.left.minute') : t('transfers.left.minutes', { count: minutes })
  const hours = Math.round(minutes / 60)
  if (hours < 24) return hours === 1 ? t('transfers.left.hour') : t('transfers.left.hours', { count: hours })
  return t('transfers.left.days')
}
