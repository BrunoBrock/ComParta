import { afterEach, describe, expect, it } from 'vitest'
import { errorCodes } from '../bridge/contract.generated.ts'
import { en } from './en.ts'
import { catalogs, formatSize, formatTimeLeft, languages, pickLanguage, setLanguage } from './index.ts'

afterEach(() => setLanguage('en'))

/** Fails with the names of the keys that one catalog has and the other lacks. */
function differences(source: Record<string, string>, other: Record<string, string>) {
  return {
    missing: Object.keys(source).filter((key) => !(key in other)),
    extra: Object.keys(other).filter((key) => !(key in source)),
  }
}

// Words a person should never have to learn (AD-5). The same list is enforced for code in
// tests/ComParta.Core.Tests/GuardTests.cs.
const technicalTerms = ['seed', 'peer', 'leech', 'swarm', 'tracker', 'magnet', 'torrent', 'hash', 'dht']
// The same idea in Portuguese, plus the English words, which Brazilian forums borrow.
const portugueseTerms = ['semente', 'semear', 'par', 'pares', 'rastreador', 'enxame', ...technicalTerms]

const hasTerm = (text: string, term: string) =>
  new RegExp(`(^|[^\\p{L}])${term}(s|ed|er|ers|ing)?($|[^\\p{L}])`, 'iu').test(text)

describe('the translation catalog', () => {
  it('uses no technical file-sharing term', () => {
    for (const [key, text] of Object.entries(en)) {
      for (const term of technicalTerms) {
        expect(hasTerm(text, term), `${key} contains "${term}"`).toBe(false)
      }
    }
  })

  it('uses no technical file-sharing term in Portuguese', () => {
    for (const [key, text] of Object.entries(catalogs['pt-BR'])) {
      for (const term of portugueseTerms) {
        expect(hasTerm(text, term), `${key} contains "${term}"`).toBe(false)
      }
    }
  })

  it('catches a forbidden Portuguese word', () => {
    expect(portugueseTerms.some((term) => hasTerm('Você é uma semente', term))).toBe(true)
    expect(portugueseTerms.some((term) => hasTerm('Abra para receber', term))).toBe(false)
  })

  it.each(languages.filter((language) => language !== 'en'))('%s has exactly the English keys', (language) => {
    expect(differences(en, catalogs[language])).toEqual({ missing: [], extra: [] })
  })

  it('notices a missing or extra key', () => {
    const { 'app.title': _, ...withoutTitle } = catalogs['pt-BR']
    expect(differences(en, { ...withoutTitle, 'made.up': 'x' })).toEqual({ missing: ['app.title'], extra: ['made.up'] })
  })

  it('keeps every placeholder in every language', () => {
    const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort()
    for (const language of languages) {
      for (const [key, text] of Object.entries(en)) {
        expect(placeholders(catalogs[language][key as keyof typeof en]), `${language} ${key}`).toEqual(placeholders(text))
      }
    }
  })

  it('has a message for every error code', () => {
    const catalog: Record<string, string> = en
    for (const code of errorCodes) {
      expect(catalog[`error.${code}`], `error.${code} is missing`).toBeTruthy()
    }
  })

  it('has no message for an error code that does not exist', () => {
    const known = new Set<string>(errorCodes)
    for (const key of Object.keys(en).filter((k) => k.startsWith('error.'))) {
      expect(known.has(key.slice('error.'.length)), `${key} matches no error code`).toBe(true)
    }
  })
})

describe('sizes', () => {
  it.each([
    [0, '0 bytes'],
    [1023, '1,023 bytes'],
    [1024, '1 KB'],
    [482_113, '471 KB'],
    [5 * 1024 ** 2, '5.0 MB'],
    [1_288_490_188, '1.20 GB'],
  ])('shows %d bytes as %s', (bytes, expected) => {
    expect(formatSize(bytes)).toBe(expected)
  })
})

describe('in Brazilian Portuguese', () => {
  it('writes sizes with a decimal comma', () => {
    setLanguage('pt-BR')
    expect(formatSize(1_288_490_188)).toBe('1,20 GB')
    expect(formatSize(5 * 1024 ** 2)).toBe('5,0 MB')
    expect(formatSize(1023)).toBe('1.023 bytes')
  })

  it('says how long is left', () => {
    setLanguage('pt-BR')
    expect(formatTimeLeft(600)).toBe('faltam cerca de 10 minutos')
  })
})

describe('choosing the language', () => {
  it.each([
    [null, 'pt-BR', 'pt-BR'],
    [null, 'pt-PT', 'pt-BR'],
    [null, 'fr-FR', 'en'],
    [null, 'en-GB', 'en'],
    ['en', 'pt-BR', 'en'],
    ['pt-BR', 'en-US', 'pt-BR'],
    ['xx', 'de', 'en'],
  ])('saved %s with system %s speaks %s', (saved, system, expected) => {
    expect(pickLanguage(saved, system)).toBe(expected)
  })
})

describe('time left', () => {
  it.each([
    [0, 'less than a minute left'],
    [59, 'less than a minute left'],
    [80, 'about 1 minute left'],
    [600, 'about 10 minutes left'],
    [3600, 'about 1 hour left'],
    [5 * 3600 + 100, 'about 5 hours left'],
    [3 * 86400, 'more than a day left'],
  ])('shows %d seconds as %s', (seconds, expected) => {
    expect(formatTimeLeft(seconds)).toBe(expected)
  })
})
