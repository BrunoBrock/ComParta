import type { Bridge } from './bridge.ts'
import { createFakeBridge } from './fakeBridge.ts'
import { createHostBridge, isInsideApp } from './hostBridge.ts'

export type { Bridge } from './bridge.ts'
export type * from './contract.generated.ts'

/** The real bridge inside the app; the fake one in a normal browser. */
export function getBridge(): Bridge {
  return isInsideApp() ? createHostBridge() : createFakeBridge({ stepMs: 700, systemLanguage: navigator.language })
}
