import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'

export interface EmptyStateProps {
  label: string
  icon: LucideIcon
  description: string
  milestone: string
  /** Use 'h2' when nesting inside a page that already has its own h1 (e.g. a detail-page tab panel). */
  headingLevel?: 'h1' | 'h2'
  /** Tighter padding for use inside a tab panel rather than a full route page. */
  compact?: boolean
  children?: ReactNode
}

export function EmptyState({
  label,
  icon: Icon,
  description,
  milestone,
  headingLevel = 'h1',
  compact = false,
  children,
}: EmptyStateProps) {
  const Heading = headingLevel

  return (
    <div className={`flex flex-col items-center justify-center gap-3 text-center ${compact ? 'py-10' : 'h-full px-6 py-16'}`}>
      <div className="flex h-12 w-12 items-center justify-center rounded-md border border-border bg-surface-raised text-text-secondary">
        <Icon className="h-6 w-6" strokeWidth={1.5} />
      </div>
      <Heading className="text-base font-semibold text-text-primary">{label}</Heading>
      <p className="max-w-sm text-sm text-text-secondary">{description}</p>
      <p className="font-mono text-xs text-status-info">{milestone}</p>
      {children}
    </div>
  )
}
