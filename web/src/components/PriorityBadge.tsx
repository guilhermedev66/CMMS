import { AlertOctagon, ArrowDown, ArrowUp, Minus, type LucideIcon } from 'lucide-react'
import { priorityLabels, type Priority } from '../api/shared'

const priorityConfig: Record<Priority, { icon: LucideIcon; className: string }> = {
  P1: { icon: AlertOctagon, className: 'border-status-danger/30 bg-status-danger/10 text-status-danger' },
  P2: { icon: ArrowUp, className: 'border-status-warning/30 bg-status-warning/10 text-status-warning' },
  P3: { icon: Minus, className: 'border-status-info/30 bg-status-info/10 text-status-info' },
  P4: { icon: ArrowDown, className: 'border-status-neutral/30 bg-status-neutral/10 text-status-neutral' },
}

export function PriorityBadge({ priority }: { priority: Priority }) {
  const { icon: Icon, className } = priorityConfig[priority]
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-sm border px-2 py-0.5 text-xs font-medium whitespace-nowrap ${className}`}
    >
      <Icon className="h-3.5 w-3.5" strokeWidth={2} />
      {priorityLabels[priority]}
    </span>
  )
}
