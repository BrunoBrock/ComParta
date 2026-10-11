import { CircleCheck, CirclePause, LoaderCircle, TriangleAlert } from 'lucide-react'

export type ChipTone = 'ok' | 'busy' | 'warn' | 'paused'

interface StatusChipProps {
  tone: ChipTone
  text: string
}

const icons = { ok: CircleCheck, busy: LoaderCircle, warn: TriangleAlert, paused: CirclePause }

/** A short state word with an icon, so colour is never the only signal. */
export default function StatusChip({ tone, text }: StatusChipProps) {
  const Icon = icons[tone]
  return (
    <span className={`chip chip-${tone}`}>
      <Icon size={16} aria-hidden="true" />
      {text}
    </span>
  )
}
