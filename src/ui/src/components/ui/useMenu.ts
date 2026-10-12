import { type KeyboardEvent, useEffect, useRef, useState } from 'react'

/**
 * A button that opens a short menu: the first item takes focus, the arrow keys move through the
 * items, Escape closes and gives focus back to the button, and a click elsewhere closes it.
 */
export function useMenu() {
  const [open, setOpen] = useState(false)
  const menu = useRef<HTMLUListElement>(null)
  const toggle = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return
    menu.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
    const onPointer = (event: PointerEvent) => {
      const target = event.target as Node
      if (!menu.current?.contains(target) && !toggle.current?.contains(target)) setOpen(false)
    }
    document.addEventListener('pointerdown', onPointer)
    return () => document.removeEventListener('pointerdown', onPointer)
  }, [open])

  /** Closes the menu, gives focus back to its button, then runs the chosen item. */
  function choose(action: () => void) {
    setOpen(false)
    toggle.current?.focus()
    action()
  }

  function onMenuKey(event: KeyboardEvent<HTMLUListElement>) {
    const items = [...(menu.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? [])]
    const at = items.indexOf(document.activeElement as HTMLButtonElement)
    if (event.key === 'Escape') {
      event.preventDefault()
      setOpen(false)
      toggle.current?.focus()
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      items[(at + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length]?.focus()
    }
  }

  return {
    open,
    menuProps: { ref: menu, role: 'menu', onKeyDown: onMenuKey },
    toggleProps: {
      ref: toggle,
      'aria-haspopup': 'menu' as const,
      'aria-expanded': open,
      onClick: () => setOpen((value) => !value),
    },
    choose,
  }
}
