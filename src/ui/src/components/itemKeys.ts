import type { KeyboardEvent } from 'react'

const forward = new Set(['ArrowDown', 'ArrowRight'])
const back = new Set(['ArrowUp', 'ArrowLeft'])

/**
 * Arrow keys, Home and End move between the files of one list (UX design, Accessibility). Only
 * a focused file reacts; the keys keep their usual meaning inside its buttons.
 */
export function moveBetweenItems(event: KeyboardEvent<HTMLUListElement>) {
  const target = event.target as HTMLElement
  if (!target.matches('li.item')) return
  const items = [...event.currentTarget.querySelectorAll<HTMLElement>(':scope > li.item')]
  const at = items.indexOf(target)
  let next: number
  if (forward.has(event.key)) next = Math.min(at + 1, items.length - 1)
  else if (back.has(event.key)) next = Math.max(at - 1, 0)
  else if (event.key === 'Home') next = 0
  else if (event.key === 'End') next = items.length - 1
  else return
  event.preventDefault()
  items[next].focus()
}
