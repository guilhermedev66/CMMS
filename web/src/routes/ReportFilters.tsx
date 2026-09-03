import { useMemo, useState } from 'react'
import type { Asset } from '../api/assets'
import type { SiteMembership } from '../api/auth'

export interface ReportFilterValue {
  siteId: string | null
  fromUtc: string
  toUtc: string
  assetId: string | null
}

const DATE_RANGE_PRESETS = [
  { label: '7d', days: 7 },
  { label: '30d', days: 30 },
  { label: '90d', days: 90 },
] as const

function presetRange(days: number): { fromUtc: string; toUtc: string } {
  const to = new Date()
  const from = new Date(to.getTime() - days * 24 * 60 * 60 * 1000)
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() }
}

/**
 * Site + date-range + optional-asset filter bar shared by DashboardPage and ReportsPage. A
 * company-wide Admin with zero explicit `siteMemberships` has no site to pick — this codebase has
 * no `sites.manage`/list-all-sites endpoint yet (a gap carried since M2, not introduced here), so
 * that case is surfaced honestly rather than silently defaulting to a fabricated site.
 */
export function useReportFilters(siteMemberships: SiteMembership[] | undefined): {
  value: ReportFilterValue
  setSiteId: (siteId: string) => void
  setAssetId: (assetId: string | null) => void
  setDays: (days: number) => void
  selectedDays: number
} {
  const [siteId, setSiteIdState] = useState<string | null>(siteMemberships?.[0]?.siteId ?? null)
  const [assetId, setAssetId] = useState<string | null>(null)
  const [selectedDays, setSelectedDays] = useState<number>(30)
  const range = useMemo(() => presetRange(selectedDays), [selectedDays])

  return {
    value: { siteId, fromUtc: range.fromUtc, toUtc: range.toUtc, assetId },
    setSiteId: (id: string) => {
      setSiteIdState(id)
      setAssetId(null) // an asset from a different site is meaningless once the site changes
    },
    setAssetId,
    setDays: setSelectedDays,
    selectedDays,
  }
}

export function SiteAndDateFilters({
  siteMemberships,
  filters,
  assets,
}: {
  siteMemberships: SiteMembership[] | undefined
  filters: ReturnType<typeof useReportFilters>
  /** Already scoped to the selected site — pass `undefined` while assets haven't loaded yet. */
  assets: Asset[] | undefined
}) {
  const sites = siteMemberships ?? []
  return (
    <div className="flex flex-wrap items-center gap-3 border-b border-border bg-surface-raised px-6 py-3">
      <label className="flex items-center gap-2 text-xs text-text-secondary">
        Site
        <select
          value={filters.value.siteId ?? ''}
          onChange={(e) => filters.setSiteId(e.target.value)}
          className="rounded-sm border border-border bg-surface px-2 py-1 text-sm text-text-primary focus:border-border-strong focus:outline-none"
        >
          {sites.length === 0 && <option value="">No site available</option>}
          {sites.map((m) => (
            <option key={m.siteId} value={m.siteId}>
              {m.siteName}
            </option>
          ))}
        </select>
      </label>

      <div className="flex items-center gap-1 text-xs text-text-secondary">
        {DATE_RANGE_PRESETS.map((preset) => (
          <button
            key={preset.days}
            type="button"
            onClick={() => filters.setDays(preset.days)}
            className={`rounded-sm border px-2 py-1 ${
              filters.selectedDays === preset.days
                ? 'border-accent bg-accent/10 text-accent'
                : 'border-border text-text-secondary hover:border-border-strong hover:text-text-primary'
            }`}
          >
            {preset.label}
          </button>
        ))}
      </div>

      <label className="flex items-center gap-2 text-xs text-text-secondary">
        Asset (for reliability metrics)
        <select
          value={filters.value.assetId ?? ''}
          onChange={(e) => filters.setAssetId(e.target.value || null)}
          disabled={!assets || assets.length === 0}
          className="min-w-[10rem] rounded-sm border border-border bg-surface px-2 py-1 text-sm text-text-primary focus:border-border-strong focus:outline-none disabled:opacity-60"
        >
          <option value="">None selected</option>
          {(assets ?? []).map((asset) => (
            <option key={asset.id} value={asset.id}>
              {asset.tag} — {asset.name}
            </option>
          ))}
        </select>
      </label>
    </div>
  )
}
