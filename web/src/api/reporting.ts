import { apiClient } from './client'

/**
 * Matches KpiReportResponse in src/Cmms.Api/ReportingEndpoints.cs. A `null` numeric field means
 * the metric is mathematically undefined for this window/scope (zero failures, or a per-asset-only
 * metric with no asset selected) — render it as "not available", never coerce to 0.
 */
export interface KpiReport {
  siteId: string
  assetId: string | null
  fromUtc: string
  toUtc: string
  mtbfHours: number | null
  mttrHours: number | null
  mdtHours: number | null
  operationalAvailability: number | null
  inherentAvailability: number | null
  plannedMaintenancePercentage: number | null
  preventiveWorkOrderCount: number
  correctiveWorkOrderCount: number
  totalPartsCost: number | null
  costsMasked: boolean
  openBacklogCount: number
  overduePreventivePlanCount: number
}

export interface GetKpisParams {
  siteId: string
  fromUtc: string
  toUtc: string
  assetId?: string | null
}

export function getKpis(params: GetKpisParams): Promise<KpiReport> {
  const search = new URLSearchParams({
    siteId: params.siteId,
    fromUtc: params.fromUtc,
    toUtc: params.toUtc,
  })
  if (params.assetId) search.set('assetId', params.assetId)
  return apiClient.get<KpiReport>(`/reports/kpis?${search.toString()}`)
}

// ---------- Formatting helpers (shared between the Dashboard ribbon and the Reports table) ----------

const NOT_AVAILABLE = '—'

export function formatHours(value: number | null): string {
  if (value === null) return NOT_AVAILABLE
  if (value >= 1000) return `${(value / 1000).toFixed(1)}k h`
  return `${value.toFixed(1)} h`
}

export function formatPercent(fraction: number | null, digits = 0): string {
  if (fraction === null) return NOT_AVAILABLE
  return `${(fraction * 100).toFixed(digits)}%`
}

export function formatPercentValue(percent: number | null, digits = 0): string {
  if (percent === null) return NOT_AVAILABLE
  return `${percent.toFixed(digits)}%`
}

export function formatCost(value: number | null, masked: boolean): string {
  if (masked) return 'Hidden'
  if (value === null) return NOT_AVAILABLE
  return value.toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })
}

export { NOT_AVAILABLE }
