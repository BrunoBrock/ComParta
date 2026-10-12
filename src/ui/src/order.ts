import { getLanguage } from './i18n/index.ts'

export type SortOrder = 'added' | 'name' | 'size'
export const sortOrders: readonly SortOrder[] = ['added', 'name', 'size']

/**
 * Files in the order the person chose. The app lists them oldest first, so "added" turns that
 * round; names compare the way people read them ("photo 2" before "photo 10", accents ignored);
 * a size not known yet goes last.
 */
export function inOrder<T>(
  items: readonly T[],
  order: SortOrder,
  name: (item: T) => string,
  size: (item: T) => number | null,
): T[] {
  if (order === 'added') return [...items].reverse()
  if (order === 'name') {
    const collator = new Intl.Collator(getLanguage(), { numeric: true, sensitivity: 'base' })
    return [...items].sort((a, b) => collator.compare(name(a), name(b)))
  }
  return [...items].sort((a, b) => (size(b) ?? -1) - (size(a) ?? -1))
}
