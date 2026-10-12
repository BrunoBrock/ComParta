import type { Bridge } from './bridge.ts'
import type { CommandResult, EventEnvelope } from './contract.generated.ts'

declare global {
  interface Window {
    HybridWebView?: {
      InvokeDotNet(method: string, args?: unknown[]): Promise<unknown>
    }
    chrome?: { webview?: unknown }
  }
}

const unexpected: CommandResult<never> = { ok: false, error: { code: 'unexpected' } }

/** True when the page is running inside the app's window rather than a normal browser. */
export function isInsideApp(): boolean {
  return window.location.hostname === '0.0.0.1' || window.chrome?.webview !== undefined
}

function loadHostScript(): Promise<void> {
  return new Promise((resolve, reject) => {
    if (window.HybridWebView) {
      resolve()
      return
    }
    const script = document.createElement('script')
    // Served by the app's web view itself, not by the UI build.
    script.src = '_framework/hybridwebview.js'
    script.onload = () => resolve()
    script.onerror = () => reject(new Error('The app bridge script did not load.'))
    document.head.appendChild(script)
  })
}

/** The real bridge: one `Invoke` method on the app's BridgeHost, one raw-message envelope back. */
export function createHostBridge(): Bridge {
  const ready = loadHostScript()

  return {
    async invoke(command: string, args?: unknown) {
      try {
        await ready
        const reply = await window.HybridWebView!.InvokeDotNet('Invoke', [command, JSON.stringify(args ?? null)])
        const result = (typeof reply === 'string' ? JSON.parse(reply) : reply) as CommandResult<never>
        // The app leaves out a null value; the contract promises null, as the fake bridge gives.
        if (result.ok && !('value' in result)) return { ok: true, value: null } as CommandResult<never>
        return result
      } catch {
        return unexpected
      }
    },

    handOverDrop(token: string, files: File[]) {
      // WebView2 gives the files to the window with their real paths; the page never sees one (AD-7).
      const webview = window.chrome?.webview as
        | { postMessageWithAdditionalObjects?: (message: string, objects: File[]) => void }
        | undefined
      if (typeof webview?.postMessageWithAdditionalObjects !== 'function') return false
      webview.postMessageWithAdditionalObjects(`comparta-drop:${token}`, files)
      return true
    },

    subscribe(listener) {
      const onMessage = (event: Event) => {
        const message = (event as CustomEvent<{ message: string }>).detail?.message
        if (typeof message !== 'string') return
        try {
          listener(JSON.parse(message) as EventEnvelope)
        } catch {
          // Not one of ours; ignore it.
        }
      }
      window.addEventListener('HybridWebViewMessageReceived', onMessage)
      return () => window.removeEventListener('HybridWebViewMessageReceived', onMessage)
    },
  } as Bridge
}
