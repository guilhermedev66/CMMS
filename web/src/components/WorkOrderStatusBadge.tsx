import {
  Archive,
  Ban,
  CheckCircle2,
  Circle,
  FileEdit,
  Wrench,
  type LucideIcon,
} from 'lucide-react'
import type { WorkOrderStatus } from '../api/workOrders'

// Same 5-color semantic vocabulary as Assets' StatusBadge/CriticalityBadge —
// 7 states share it in groups (icon + label carry the finer distinction
// within a color, per docs/04: "never color alone"). No OnHold entry: this
// M2 slice's backend has no OnHold state (see api/workOrders.ts's doc comment).
const statusConfig: Record<WorkOrderStatus, { label: string; icon: LucideIcon; className: string }> = {
  Draft: { label: 'Draft', icon: FileEdit, className: 'border-status-neutral/30 bg-status-neutral/10 text-status-neutral' },
  Open: { label: 'Open', icon: Circle, className: 'border-status-info/30 bg-status-info/10 text-status-info' },
  Scheduled: {
    label: 'Scheduled',
    icon: Circle,
    className: 'border-status-info/30 bg-status-info/10 text-status-info',
  },
  InProgress: {
    label: 'In Progress',
    icon: Wrench,
    className: 'border-status-success/30 bg-status-success/10 text-status-success',
  },
  Completed: {
    label: 'Completed',
    icon: CheckCircle2,
    className: 'border-status-success/30 bg-status-success/10 text-status-success',
  },
  Closed: { label: 'Closed', icon: Archive, className: 'border-status-neutral/30 bg-status-neutral/10 text-status-neutral' },
  Cancelled: { label: 'Cancelled', icon: Ban, className: 'border-status-danger/30 bg-status-danger/10 text-status-danger' },
}

export function WorkOrderStatusBadge({ status }: { status: WorkOrderStatus }) {
  const { label, icon: Icon, className } = statusConfig[status]
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-sm border px-2 py-0.5 text-xs font-medium whitespace-nowrap ${className}`}
    >
      <Icon className="h-3.5 w-3.5" strokeWidth={2} />
      {label}
    </span>
  )
}
