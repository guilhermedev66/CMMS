import type { AssetCriticality } from '../api/assets'

// Criticality is shown as its classification letter (the domain-standard
// A/B/C convention), not a generic icon — colors come from the semantic
// status tokens, never raw hex.
const criticalityConfig: Record<AssetCriticality, { description: string; className: string }> = {
  A: {
    description: 'Critical — highest priority for maintenance response',
    className: 'border-status-danger/30 bg-status-danger/10 text-status-danger',
  },
  B: {
    description: 'Important — moderate priority for maintenance response',
    className: 'border-status-warning/30 bg-status-warning/10 text-status-warning',
  },
  C: {
    description: 'Minor — low priority for maintenance response',
    className: 'border-status-neutral/30 bg-status-neutral/10 text-status-neutral',
  },
}

export function CriticalityBadge({ criticality }: { criticality: AssetCriticality }) {
  const { description, className } = criticalityConfig[criticality]
  return (
    <span
      title={description}
      className={`inline-flex h-5 w-5 items-center justify-center rounded-sm border font-mono text-xs font-semibold ${className}`}
    >
      {criticality}
      <span className="sr-only"> — {description}</span>
    </span>
  )
}
