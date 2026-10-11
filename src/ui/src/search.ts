/** Lower case without accents, so "aniversario" finds "Aniversário". */
export function fold(text: string): string {
  return text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()
}

export function matches(name: string, query: string): boolean {
  const wanted = fold(query.trim())
  return wanted === '' || fold(name).includes(wanted)
}
