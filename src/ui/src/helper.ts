/** The shortest key a helper computer takes; core refuses shorter ones (SettingsUseCases.MinHelperKeyLength). */
export const minHelperKeyLength = 16
const maxKeyLength = 200
const maxAddressLength = 300

/**
 * Reads the helper computer the person typed, by the same rules as core: both empty turns the
 * helper off; otherwise an http or https address with a server name and no sign-in, and a key of
 * at least 16 characters with no spaces.
 */
export function readHelper(
  address: string,
  key: string,
): { helper: { address: string; key: string } | null } | { invalid: true } {
  const a = address.trim()
  const k = key.trim()
  if (a === '' && k === '') return { helper: null }
  if (k.length < minHelperKeyLength || k.length > maxKeyLength || /[\s\p{Cc}]/u.test(k)) return { invalid: true }
  if (a.length > maxAddressLength || /\p{Cc}/u.test(a)) return { invalid: true }
  let url: URL
  try {
    url = new URL(a)
  } catch {
    return { invalid: true }
  }
  if (!['http:', 'https:'].includes(url.protocol) || url.hostname === '' || url.username !== '' || url.password !== '')
    return { invalid: true }
  return { helper: { address: a, key: k } }
}
