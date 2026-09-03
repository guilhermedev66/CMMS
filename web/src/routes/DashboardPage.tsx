import { AlertTriangle, CalendarClock, ClipboardList, Radio, TriangleAlert, Wrench, X } from 'lucide-react'
import { Link } from 'react-router-dom'
import { listAssets } from '../api/assets'
import { ApiError } from '../api/client'
import { listMaintenancePlans } from '../api/maintenancePlans'
import { formatCost, formatPercent, formatPercentValue, getKpis } from '../api/reporting'
import { listWorkOrders } from '../api/workOrders'
import { useAuth } from '../auth/useAuth'
import { PriorityBadge } from '../components/PriorityBadge'
import { useAsync } from '../hooks/useAsync'
import { useWorkOrderDispatch } from '../realtime/workOrderDispatchConnection'
import { SiteAndDateFilters, useReportFilters } from './ReportFilters'

/**
 * docs/04-frontend-ia.md § "Key screens" — "Dashboard — role-aware. Planner/Admin view: a KPI
 * ribbon ... plus one 'attention needed' list ... not a wall of decorative chart widgets.
 * Technician view: 'assigned to me today,' nothing else."
 */
export function DashboardPage() {
  const { user } = useAuth()
  if (!user) return null

  const isPlannerOrAdmin = user.isAdmin || (user.siteMemberships ?? []).some((m) => m.role === 'Planner' || m.role === 'Admin')
  return isPlannerOrAdmin ? <PlannerDashboard /> : <TechnicianDashboard />
}

// ---------- Technician: "assigned to me today," nothing else ----------

function TechnicianDashboard() {
  const { user } = useAuth()
  const { status, data, error, reload } = useAsync(() => listWorkOrders(), [])

  const assignedToMe = (data ?? []).filter(
    (wo) => wo.assigneeId === user?.id && (wo.status === 'Scheduled' || wo.status === 'InProgress'),
  )

  return (
    <div className="flex h-full flex-col">
      <div className="border-b border-border px-6 py-4">
        <h1 className="text-lg font-semibold text-text-primary">Assigned to you</h1>
        <p className="text-sm text-text-secondary">Work Orders you've claimed or started.</p>
      </div>
      <div className="flex-1 overflow-auto px-6 py-6">
        {status === 'loading' && <p className="text-sm text-text-secondary">Loading…</p>}
        {status === 'error' && (
          <div className="flex flex-col items-center gap-2 py-10 text-center">
            <TriangleAlert className="h-5 w-5 text-status-danger" strokeWidth={1.5} />
            <p className="text-sm text-text-primary">{error instanceof ApiError ? error.message : 'Could not load Work Orders.'}</p>
            <button type="button" onClick={reload} className="text-xs text-accent underline">
              Retry
            </button>
          </div>
        )}
        {status === 'success' && assignedToMe.length === 0 && (
          <p className="text-sm text-text-secondary">Nothing assigned to you right now.</p>
        )}
        {status === 'success' && assignedToMe.length > 0 && (
          <ul className="flex flex-col gap-2">
            {assignedToMe.map((wo) => (
              <li key={wo.id}>
                <Link
                  to={`/work-orders/${wo.id}`}
                  className="flex items-center justify-between gap-3 rounded-sm border border-border bg-surface-raised px-4 py-3 hover:border-border-strong"
                >
                  <div>
                    <p className="text-sm text-text-primary">{wo.title}</p>
                    <p className="text-xs text-text-secondary">{wo.status}</p>
                  </div>
                  <PriorityBadge priority={wo.priority} />
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}

// ---------- Planner/Admin: KPI ribbon + attention-needed list + live dispatch feed ----------

function PlannerDashboard() {
  const { user } = useAuth()
  const filters = useReportFilters(user?.siteMemberships)
  const { connectionState, events, alerts, dismissAlert } = useWorkOrderDispatch(true)

  const { status: assetsStatus, data: allAssets } = useAsync(() => listAssets(), [])
  const scopedAssets = (allAssets ?? []).filter((a) => a.siteId === filters.value.siteId)

  const kpiState = useAsync(
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

  const plansState = useAsync(() => listMaintenancePlans(), [])
  const workOrdersState = useAsync(() => listWorkOrders(), [])

  const overduePlans = (plansState.data ?? []).filter(
    (p) => p.status === 'Active' && new Date(p.nextDueAtUtc) < new Date() && p.siteId === filters.value.siteId,
  )
  const highPriorityOpen = (workOrdersState.data ?? []).filter(
    (wo) =>
      wo.siteId === filters.value.siteId &&
      wo.priority === 'P1' &&
      (wo.status === 'Open' || wo.status === 'Scheduled' || wo.status === 'InProgress'),
  )

  return (
    <div className="flex h-full flex-col">
      <div className="border-b border-border px-6 py-4">
        <div className="flex items-center justify-between">
          <h1 className="text-lg font-semibold text-text-primary">Operations Dashboard</h1>
          <LiveIndicator state={connectionState} />
        </div>
        <p className="text-sm text-text-secondary">Live KPI ribbon and attention-needed items — see the Reports page for the full breakdown.</p>
      </div>

      <SiteAndDateFilters siteMemberships={user?.siteMemberships} filters={filters} assets={assetsStatus === 'success' ? scopedAssets : undefined} />

      {alerts.length > 0 && (
        <div className="flex flex-col gap-2 border-b border-status-danger/30 bg-status-danger/5 px-6 py-3">
          {alerts.map((alert) => (
            <div key={`${alert.workOrderId}-${alert.receivedAtUtc}`} className="flex items-center justify-between gap-3 text-sm">
              <div className="flex items-center gap-2 text-status-danger">
                <AlertTriangle className="h-4 w-4 shrink-0" strokeWidth={2} />
                <span className="font-medium">Emergency:</span>
                <Link to={`/work-orders/${alert.workOrderId}`} className="underline hover:no-underline">
                  {alert.title}
                </Link>
                <span className="text-text-secondary">just became actionable ({alert.priority})</span>
              </div>
              <button type="button" onClick={() => dismissAlert(alert.workOrderId)} aria-label="Dismiss" className="text-text-secondary hover:text-text-primary">
                <X className="h-3.5 w-3.5" strokeWidth={2} />
              </button>
            </div>
          ))}
        </div>
      )}

      <div className="flex-1 overflow-auto px-6 py-6">
        {!filters.value.siteId ? (
          <p className="text-sm text-text-secondary">
            No site membership found for your account — a company-wide Admin currently needs at least one explicit site
            membership to use this dashboard (there's no company-wide "all sites" reporting view yet).
          </p>
        ) : (
          <div className="flex flex-col gap-8">
            <section>
              <h2 className="mb-3 text-sm font-semibold text-text-primary">KPI ribbon</h2>
              {kpiState.status === 'loading' && <p className="text-sm text-text-secondary">Loading KPIs…</p>}
              {kpiState.status === 'error' && (
                <p className="text-sm text-status-danger">{kpiState.error instanceof ApiError ? kpiState.error.message : 'Could not load KPIs.'}</p>
              )}
              {kpiState.status === 'success' && (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
                  <KpiTile
                    label="Operational Availability"
                    value={formatPercent(kpiState.data.operationalAvailability)}
                    hint={filters.value.assetId ? undefined : 'Select an asset'}
                  />
                  <KpiTile
                    label="Planned Maintenance %"
                    value={formatPercentValue(kpiState.data.plannedMaintenancePercentage)}
                  />
                  <KpiTile
                    label="Preventive / Corrective"
                    value={`${kpiState.data.preventiveWorkOrderCount} / ${kpiState.data.correctiveWorkOrderCount}`}
                  />
                  <KpiTile label="Parts Cost" value={formatCost(kpiState.data.totalPartsCost, kpiState.data.costsMasked)} />
                  <KpiTile label="Open Backlog" value={String(kpiState.data.openBacklogCount)} />
                  <KpiTile label="Overdue Preventive" value={String(kpiState.data.overduePreventivePlanCount)} tone={kpiState.data.overduePreventivePlanCount > 0 ? 'warning' : undefined} />
                </div>
              )}
            </section>

            <section>
              <h2 className="mb-3 text-sm font-semibold text-text-primary">Attention needed</h2>
              {highPriorityOpen.length === 0 && overduePlans.length === 0 ? (
                <p className="text-sm text-text-secondary">Nothing needs attention right now.</p>
              ) : (
                <ul className="flex flex-col gap-2">
                  {highPriorityOpen.map((wo) => (
                    <li key={wo.id}>
                      <Link to={`/work-orders/${wo.id}`} className="flex items-center gap-3 rounded-sm border border-status-danger/30 bg-status-danger/5 px-4 py-2.5 text-sm hover:border-status-danger/60">
                        <Wrench className="h-4 w-4 shrink-0 text-status-danger" strokeWidth={1.75} />
                        <span className="flex-1 text-text-primary">{wo.title}</span>
                        <span className="text-xs text-text-secondary">{wo.assigneeId ? 'Assigned' : 'Unassigned'}</span>
                        <PriorityBadge priority={wo.priority} />
                      </Link>
                    </li>
                  ))}
                  {overduePlans.map((plan) => (
                    <li key={plan.id}>
                      <Link to="/planning" className="flex items-center gap-3 rounded-sm border border-status-warning/30 bg-status-warning/5 px-4 py-2.5 text-sm hover:border-status-warning/60">
                        <CalendarClock className="h-4 w-4 shrink-0 text-status-warning" strokeWidth={1.75} />
                        <span className="flex-1 text-text-primary">{plan.title}</span>
                        <span className="text-xs text-text-secondary">Overdue since {new Date(plan.nextDueAtUtc).toLocaleDateString()}</span>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </section>

            <section>
              <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold text-text-primary">
                <Radio className="h-4 w-4" strokeWidth={1.75} />
                Live activity this session
              </h2>
              {events.length === 0 ? (
                <p className="text-sm text-text-secondary">No live updates yet — events appear here as Work Orders change.</p>
              ) : (
                <ul className="flex flex-col gap-1">
                  {events.map((event) => (
                    <li key={`${event.workOrderId}-${event.receivedAtUtc}`} className="flex items-center gap-3 border-b border-border/60 py-1.5 text-xs">
                      <span className="font-mono text-text-secondary tabular-nums">{new Date(event.receivedAtUtc).toLocaleTimeString()}</span>
                      <ClipboardList className="h-3.5 w-3.5 shrink-0 text-text-secondary" strokeWidth={1.75} />
                      <Link to={`/work-orders/${event.workOrderId}`} className="text-text-primary hover:underline">
                        Work Order {event.workOrderId.slice(0, 8)}
                      </Link>
                      <span className="text-text-secondary">{event.action} → {event.status}</span>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>
        )}
      </div>
    </div>
  )
}

function KpiTile({ label, value, hint, tone }: { label: string; value: string; hint?: string; tone?: 'warning' }) {
  return (
    <div className={`rounded-sm border p-3 ${tone === 'warning' ? 'border-status-warning/30 bg-status-warning/5' : 'border-border bg-surface-raised'}`}>
      <p className="text-xs text-text-secondary">{label}</p>
      <p className="mt-1 font-mono text-lg font-semibold tabular-nums text-text-primary">{value}</p>
      {hint && <p className="mt-0.5 text-[11px] text-text-secondary">{hint}</p>}
    </div>
  )
}

function LiveIndicator({ state }: { state: 'connecting' | 'connected' | 'disconnected' }) {
  const config = {
    connecting: { label: 'Connecting…', className: 'text-text-secondary' },
    connected: { label: 'Live', className: 'text-status-success' },
    disconnected: { label: 'Offline', className: 'text-text-secondary' },
  }[state]
  return (
    <span className={`flex items-center gap-1.5 text-xs ${config.className}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${state === 'connected' ? 'bg-status-success' : 'bg-border-strong'}`} />
      {config.label}
    </span>
  )
}
