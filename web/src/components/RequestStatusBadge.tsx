import { Ban, CheckCircle2, Circle, XCircle, type LucideIcon } from 'lucide-react'
import type { RequestStatus } from '../api/requests'

const statusConfig: Record<RequestStatus, { label: string; icon: LucideIcon; className: string }> = {
  New: { label: 'New', icon: Circle, className: 'border-status-info/30 bg-status-info/10 text-status-info' },
  Converted: {
    label: 'Converted',
    icon: CheckCircle2,
    className: 'border-status-success/30 bg-status-success/10 text-status-success',
  },
  Rejected: { label: 'Rejected', icon: XCircle, className: 'border-status-danger/30 bg-status-danger/10 text-status-danger' },
  Cancelled: { label: 'Cancelled', icon: Ban, className: 'border-status-neutral/30 bg-status-neutral/10 text-status-neutral' },
}

export function RequestStatusBadge({ status }: { status: RequestStatus }) {
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
