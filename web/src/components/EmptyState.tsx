import type { NavItem } from '../nav'

export function EmptyState({ label, icon: Icon, description, milestone }: NavItem) {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-3 px-6 py-16 text-center">
      <div className="flex h-12 w-12 items-center justify-center rounded-md border border-border bg-surface-raised text-text-secondary">
        <Icon className="h-6 w-6" strokeWidth={1.5} />
      </div>
      <h1 className="text-base font-semibold text-text-primary">{label}</h1>
      <p className="max-w-sm text-sm text-text-secondary">{description}</p>
      <p className="font-mono text-xs text-status-info">{milestone}</p>
    </div>
  )
}
