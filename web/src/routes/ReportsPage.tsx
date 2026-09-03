import { Download, TriangleAlert } from 'lucide-react'
import { listAssets } from '../api/assets'
import { ApiError } from '../api/client'
import { formatCost, formatHours, formatPercent, formatPercentValue, getKpis, NOT_AVAILABLE, type KpiReport } from '../api/reporting'
import { useAuth } from '../auth/useAuth'
import { useAsync } from '../hooks/useAsync'
import { SiteAndDateFilters, useReportFilters } from './ReportFilters'

interface ReportRow {
  label: string
  value: string
  note?: string
}

function buildRows(kpis: KpiReport, hasAsset: boolean): ReportRow[] {
  const perAssetNote = hasAsset ? undefined : 'Select an asset — averaging this across a whole site is not mathematically defensible'
  return [
    { label: 'MTBF (Mean Time Between Failures)', value: formatHours(kpis.mtbfHours), note: kpis.mtbfHours === null ? perAssetNote ?? 'No failures in this window — undefined, not zero' : undefined },
    { label: 'MTTR (Mean Time To Repair)', value: formatHours(kpis.mttrHours), note: kpis.mttrHours === null ? perAssetNote ?? 'No failures in this window — undefined, not zero' : undefined },
    { label: 'MDT (Mean Downtime, incl. logistics/parts wait)', value: formatHours(kpis.mdtHours), note: kpis.mdtHours === null ? perAssetNote : undefined },
    { label: 'Operational Availability (Ao)', value: formatPercent(kpis.operationalAvailability, 1), note: kpis.operationalAvailability === null ? perAssetNote : undefined },
    { label: 'Inherent Availability (Ai = MTBF / (MTBF + MTTR))', value: formatPercent(kpis.inherentAvailability, 1), note: kpis.inherentAvailability === null ? (perAssetNote ?? 'Depends on MTBF being defined') : undefined },
    { label: 'Planned Maintenance % (wrench-time proxy)', value: formatPercentValue(kpis.plannedMaintenancePercentage, 1), note: kpis.plannedMaintenancePercentage === null ? 'No completed Work Orders in this window' : undefined },
    { label: 'Preventive Work Orders completed', value: String(kpis.preventiveWorkOrderCount) },
    { label: 'Corrective Work Orders completed', value: String(kpis.correctiveWorkOrderCount) },
    { label: 'Total Parts Cost', value: formatCost(kpis.totalPartsCost, kpis.costsMasked) },
    { label: 'Open Backlog (live count, not period-scoped)', value: String(kpis.openBacklogCount) },
    { label: 'Overdue Preventive Plans (live count, not period-scoped)', value: String(kpis.overduePreventivePlanCount) },
  ]
}

function downloadCsv(kpis: KpiReport, rows: ReportRow[]) {
  const header = `Site,${kpis.siteId},Asset,${kpis.assetId ?? 'All'},From,${kpis.fromUtc},To,${kpis.toUtc}\n`
  const body = rows.map((r) => `"${r.label.replace(/"/g, '""')}","${r.value}"`).join('\n')
  const blob = new Blob([header + 'Metric,Value\n' + body], { type: 'text/csv' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `cmms-kpi-report-${kpis.fromUtc.slice(0, 10)}-to-${kpis.toUtc.slice(0, 10)}.csv`
  link.click()
  URL.revokeObjectURL(url)
}

/**
 * docs/04-frontend-ia.md § "Reports/KPIs" — "table-first with export, chart-second. B2B ops tools
 * get their reports exported to spreadsheets far more than admired as visualizations." Every
 * formula here is documented in docs/01-domain-and-workflows.md's "KPI formulas" section and
 * computed server-side in src/Cmms.Api/ReportingEndpoints.cs — this page renders that response
 * as-is, it never recomputes or reformats a number into a different meaning.
 */
export function ReportsPage() {
  const { user } = useAuth()
  const filters = useReportFilters(user?.siteMemberships)

  const { status: assetsStatus, data: allAssets } = useAsync(() => listAssets(), [])
  const scopedAssets = (allAssets ?? []).filter((a) => a.siteId === filters.value.siteId)

  const { status, data, error, reload } = useAsync(
    () =>
      filters.value.siteId
        ? getKpis({
            siteId: filters.value.siteId,
            fromUtc: filters.value.fromUtc,
            toUtc: filters.value.toUtc,
            assetId: filters.value.assetId,
          })
        : Promise.reject(new Error('no-site')),
    [filters.value.siteId, filters.value.fromUtc, filters.value.toUtc, filters.value.assetId],
  )

  const rows = status === 'success' ? buildRows(data, !!filters.value.assetId) : []

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between border-b border-border px-6 py-4">
        <div>
          <h1 className="text-lg font-semibold text-text-primary">Reports / KPIs</h1>
          <p className="text-sm text-text-secondary">Sourced from SMRP / ISO 14224 / EN 13306 definitions — see docs/01 for each formula.</p>
        </div>
        <button
          type="button"
          disabled={status !== 'success'}
          onClick={() => status === 'success' && downloadCsv(data, rows)}
          className="inline-flex items-center gap-1.5 rounded-sm border border-border px-3 py-1.5 text-sm text-text-primary hover:border-border-strong disabled:opacity-60"
        >
          <Download className="h-3.5 w-3.5" strokeWidth={1.75} />
          Export CSV
        </button>
      </div>

      <SiteAndDateFilters siteMemberships={user?.siteMemberships} filters={filters} assets={assetsStatus === 'success' ? scopedAssets : undefined} />

      <div className="flex-1 overflow-auto px-6 py-6">
        {!filters.value.siteId && (
          <p className="text-sm text-text-secondary">No site membership found for your account.</p>
        )}

        {filters.value.siteId && status === 'loading' && <p className="text-sm text-text-secondary">Loading…</p>}

        {filters.value.siteId && status === 'error' && (
          <div className="flex flex-col items-center gap-2 py-10 text-center">
            <TriangleAlert className="h-5 w-5 text-status-danger" strokeWidth={1.5} />
            <p className="text-sm text-text-primary">{error instanceof ApiError ? error.message : 'Could not load the report.'}</p>
            <button type="button" onClick={reload} className="text-xs text-accent underline">
              Retry
            </button>
          </div>
        )}

        {filters.value.siteId && status === 'success' && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] border-collapse text-sm">
              <thead className="border-b border-border text-left text-xs text-text-secondary">
                <tr>
                  <th className="py-2 pr-4 font-medium">Metric</th>
                  <th className="py-2 pr-4 font-medium">Value</th>
                  <th className="py-2 font-medium">Note</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.label} className="border-b border-border/60 last:border-0">
                    <td className="py-2 pr-4 text-text-primary">{row.label}</td>
                    <td className="py-2 pr-4 font-mono tabular-nums text-text-primary">{row.value === NOT_AVAILABLE ? <span className="text-text-secondary">{NOT_AVAILABLE}</span> : row.value}</td>
                    <td className="py-2 text-xs text-text-secondary">{row.note ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
