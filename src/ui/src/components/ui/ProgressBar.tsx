import { percentOf } from '../../i18n/index.ts'

interface ProgressBarProps {
  /** What is progressing, read out with the bar. */
  label: string
  /** Leave both out while the total is not known yet: the bar moves without a number. */
  value?: number
  max?: number
}

/** A bar with its whole percentage beside it, so the number is never only in the picture. */
export default function ProgressBar({ label, value, max }: ProgressBarProps) {
  if (value === undefined || !max) {
    return (
      <div className="progress-bar">
        <progress aria-label={label} />
      </div>
    )
  }
  const percent = percentOf(value, max)
  return (
    <div className="progress-bar">
      <progress value={value} max={max} aria-label={label} />
      <span className="progress-percent" aria-hidden="true">
        {percent}%
      </span>
    </div>
  )
}
