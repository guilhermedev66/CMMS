import { Archive, CheckCircle2, TriangleAlert, type LucideIcon } from 'lucide-react'
import type { AssetStatus } from '../api/assets'

// Status is communicated by icon + label + color together (docs/04:
// "never color alone") — colors come from the semantic status tokens, never
// raw hex.
const statusConfig: Record<AssetStatus, { label: string; icon: LucideIcon; className: string }> = {
  InService: {
    label: 'In Service',
    icon: CheckCircle2,
    className: 'border-status-success/30 bg-status-success/10 text-status-success',
  },
  OutOfService: {
    label: 'Out of Service',
    icon: TriangleAlert,
    className: 'border-status-danger/30 bg-status-danger/10 text-status-danger',
  },
  Retired: {
    label: 'Retired',
    icon: Archive,
    className: 'border-status-neutral/30 bg-status-neutral/10 text-status-neutral',
  },
}

export function StatusBadge({ status }: { status: AssetStatus }) {
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
