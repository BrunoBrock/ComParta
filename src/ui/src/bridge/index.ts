import type { Bridge } from './bridge.ts'
import type { ConnectionHealthDto } from './contract.generated.ts'
import { createFakeBridge } from './fakeBridge.ts'
import { createHostBridge, isInsideApp } from './hostBridge.ts'

export type { Bridge } from './bridge.ts'
export type * from './contract.generated.ts'

const healths: readonly ConnectionHealthDto[] = ['good', 'limited', 'blocked']

/**
 * The real bridge inside the app; the fake one in a normal browser. In the browser,
 * `?connection=limited` or `?connection=blocked` shows the other connection states, and
 * `?stopped=both`, `sending` or `receiving` starts with the speed bar's Stop already chosen.
 */
export function getBridge(): Bridge {
  if (isInsideApp()) return createHostBridge()
  const asked = new URLSearchParams(window.location.search).get('connection')
  const connection = healths.find((health) => health === asked)
  const stoppedParam = new URLSearchParams(window.location.search).get('stopped')
  const stopped = {
    sendingStopped: stoppedParam === 'both' || stoppedParam === 'sending',
    receivingStopped: stoppedParam === 'both' || stoppedParam === 'receiving',
  }
  return createFakeBridge({ stepMs: 700, systemLanguage: navigator.language, connection, stopped })
}
