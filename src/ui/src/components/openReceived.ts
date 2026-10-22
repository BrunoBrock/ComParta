import type { Bridge, ErrorCode } from '../bridge/index.ts'

/**
 * Opens a received file. A file that can run a program is refused the first time with
 * `received.confirmRisky`; the screen asks the person again and calls this with `confirmed` set.
 * Returns the error code when the file did not open, or null when it did.
 */
export async function openReceived(bridge: Bridge, id: string, confirmed = false): Promise<ErrorCode | null> {
  const result = await bridge.invoke('received.open', { id, confirmed })
  return result.ok ? null : result.error.code
}
