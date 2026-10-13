/** At most this many public servers; core refuses more (SettingsUseCases.MaxPublicServers). */
export const maxPublicServers = 20
const maxLength = 300
const schemes = ['http:', 'https:', 'udp:']

/**
 * Reads the server addresses the person typed, one per line, by the same rules as core: blank
 * lines and repeats dropped; http, https or udp with a server name, a port for udp, no sign-in.
 * Returns the first line that is not an address, or the list when every line is one.
 */
export function readPublicServers(text: string): { servers: string[] } | { invalid: string } | { tooMany: true } {
  const servers: string[] = []
  for (const raw of text.split('\n')) {
    const line = raw.trim()
    if (line === '') continue
    if (!isServer(line)) return { invalid: line }
    if (!servers.some((kept) => kept.toLowerCase() === line.toLowerCase())) servers.push(line)
  }
  return servers.length > maxPublicServers ? { tooMany: true } : { servers }
}

function isServer(line: string): boolean {
  if (line.length > maxLength) return false
  let address: URL
  try {
    address = new URL(line)
  } catch {
    return false
  }
  return (
    schemes.includes(address.protocol) &&
    address.hostname !== '' &&
    address.username === '' &&
    address.password === '' &&
    (address.protocol !== 'udp:' || address.port !== '')
  )
}
