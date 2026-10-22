/**
 * The phone's back button (UX design, Phone): it closes what is on top first, an open menu before
 * the window under it, the way Escape does. Returns false when nothing was open, so the app leaves.
 * A window busy with something it started stays open and back does nothing.
 */
export function closeTopmost(): boolean {
  const menu = document.querySelector<HTMLElement>('[role="menu"]')
  if (menu) {
    menu.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    return true
  }
  const windows = document.querySelectorAll<HTMLDialogElement>('dialog[open]')
  const top = windows[windows.length - 1]
  if (!top) return false
  top.dispatchEvent(new Event('cancel', { cancelable: true }))
  return true
}
