import type { ErrorCode } from '../bridge/contract.generated.ts'
import { en, type MessageKey } from './en.ts'

export type { MessageKey }

const catalog: Record<MessageKey, string> = en

/** Looks a message up in the catalog and fills in its {placeholders}. */
export function t(key: MessageKey, values: Record<string, string | number> = {}): string {
  return catalog[key].replace(/\{(\w+)\}/g, (whole, name: string) =>
    name in values ? String(values[name]) : whole,
  )
}

export function errorMessage(code: ErrorCode): string {
  return t(`error.${code}`)
}

const number = (value: number, digits: number) =>
  value.toLocaleString('en', { minimumFractionDigits: digits, maximumFractionDigits: digits })

/** Sizes cross the bridge as whole bytes; the UI chooses how to show them. */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return t('size.bytes', { value: number(bytes, 0) })
  if (bytes < 1024 ** 2) return t('size.kb', { value: number(bytes / 1024, 0) })
  if (bytes < 1024 ** 3) return t('size.mb', { value: number(bytes / 1024 ** 2, 1) })
  return t('size.gb', { value: number(bytes / 1024 ** 3, 2) })
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
