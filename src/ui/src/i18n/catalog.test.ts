import { describe, expect, it } from 'vitest'
import { errorCodes } from '../bridge/contract.generated.ts'
import { en } from './en.ts'
import { formatSize } from './index.ts'

// Words a person should never have to learn (AD-5). The same list is enforced for code in
// tests/ComParta.Core.Tests/GuardTests.cs.
const technicalTerms = ['seed', 'peer', 'leech', 'swarm', 'tracker', 'magnet', 'torrent', 'hash', 'dht']

describe('the translation catalog', () => {
  it('uses no technical file-sharing term', () => {
    for (const [key, text] of Object.entries(en)) {
      for (const term of technicalTerms) {
        expect(text.toLowerCase(), `${key} contains "${term}"`).not.toMatch(new RegExp(`\\b${term}(s|ed|er|ers|ing)?\\b`))
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
