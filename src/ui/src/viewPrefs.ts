import type { ViewMode } from './components/ItemView.tsx'

// Until the settings store exists (epic 6 story 2), the chosen view is a per-window convenience
// kept by the web view. It is view state, not product state (AD-4), and the screen works without it.
const key = 'comparta.view'

export function loadView(): ViewMode {
  try {
    return localStorage.getItem(key) === 'list' ? 'list' : 'grid'
  } catch {
    return 'grid'
  }
}

export function saveView(view: ViewMode): void {
  try {
    localStorage.setItem(key, view)
  } catch {
    // Storage can be unavailable; the choice then lasts until the window closes.
  }
}
