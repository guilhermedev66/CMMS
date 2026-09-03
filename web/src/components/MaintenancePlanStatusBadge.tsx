import { CheckCircle2, PauseCircle, type LucideIcon } from 'lucide-react'
import type { MaintenancePlanStatus } from '../api/maintenancePlans'

const statusConfig: Record<MaintenancePlanStatus, { label: string; icon: LucideIcon; className: string }> = {
  Active: { label: 'Active', icon: CheckCircle2, className: 'border-status-success/30 bg-status-success/10 text-status-success' },
  Paused: { label: 'Paused', icon: PauseCircle, className: 'border-status-neutral/30 bg-status-neutral/10 text-status-neutral' },
}

export function MaintenancePlanStatusBadge({ status }: { status: MaintenancePlanStatus }) {
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
