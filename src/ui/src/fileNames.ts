/** The extension in lower case, or '' when the name has none. */
export function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.')
  return dot > 0 && dot < name.length - 1 ? name.slice(dot + 1).toLowerCase() : ''
}

/**
 * Keeps the last few characters (the extension and a little before it) apart from the rest, so a
 * long name can be shortened in the middle. Short names come back whole as the second part.
 */
export function splitName(name: string): [string, string] {
  if (name.length <= 20) return ['', name]
  return [name.slice(0, -10), name.slice(-10)]
}
