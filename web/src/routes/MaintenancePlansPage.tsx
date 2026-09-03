import { Plus, RotateCw, TriangleAlert } from 'lucide-react'
import { useMemo, useState } from 'react'
import { listAssets, type Asset } from '../api/assets'
import { ApiError } from '../api/client'
import {
  listMaintenancePlans,
  pauseMaintenancePlan,
  resumeMaintenancePlan,
  type MaintenancePlan,
} from '../api/maintenancePlans'
import { useAuth } from '../auth/useAuth'
import { MaintenancePlanStatusBadge } from '../components/MaintenancePlanStatusBadge'
import { useAsync } from '../hooks/useAsync'
import { NewMaintenancePlanDialog } from './NewMaintenancePlanDialog'

/**
 * Agenda-style list, not the full month/week calendar grid docs/04-frontend-ia.md describes —
 * same documented-scope-cut pattern as the Work Orders Kanban board (see WorkOrdersListPage's doc
 * comment). Every plan's next due date, recurrence, and pause/resume control is here; the M3 DoD
 * (idempotent generation, proven by MaintenancePlanGenerationTests) doesn't require a calendar
 * grid specifically.
 */
export function MaintenancePlansPage() {
  const { user } = useAuth()
  const { status, data, error, reload } = useAsync(
    () => Promise.all([listMaintenancePlans(), listAssets()]).then(([plans, assets]) => ({ plans, assets })),
    [],
  )

  const [showNew, setShowNew] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [rowError, setRowError] = useState<string | null>(null)

  const siteNameById = useMemo(
    () => new Map((user?.siteMemberships ?? []).map((m) => [m.siteId, m.siteName])),
    [user],
  )
  const canManage = (siteId: string) => user?.isAdmin || user?.siteMemberships.some((m) => m.siteId === siteId && m.role === 'Planner')

  function describeAsset(plan: MaintenancePlan, assets: Asset[]) {
    const asset = assets.find((a) => a.id === plan.assetId)
    return asset ? `${asset.tag} — ${asset.name}` : 'Asset'
  }

  async function handleToggle(plan: MaintenancePlan) {
    setBusyId(plan.id)
    setRowError(null)
    try {
      if (plan.status === 'Active') {
        await pauseMaintenancePlan(plan.id)
      } else {
        await resumeMaintenancePlan(plan.id)
      }
      reload()
    } catch (err) {
      setRowError(err instanceof ApiError ? err.message : 'Could not update this plan.')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="flex h-full flex-col">
      <div className="flex flex-col gap-1 border-b border-border px-6 py-4">
        <div className="flex items-baseline justify-between gap-2">
          <div className="flex items-baseline gap-2">
            <h1 className="text-lg font-semibold text-text-primary">Planning</h1>
            {data && <span className="font-mono text-xs text-text-secondary tabular-nums">{data.plans.length}</span>}
          </div>
          <button
            type="button"
            onClick={() => setShowNew(true)}
            disabled={!user || user.siteMemberships.length === 0}
            className="inline-flex items-center gap-1.5 rounded-sm bg-accent px-3 py-1.5 text-sm font-medium text-accent-contrast disabled:opacity-60"
          >
            <Plus className="h-3.5 w-3.5" strokeWidth={2} />
            New Plan
          </button>
        </div>
        <p className="text-sm text-text-secondary">
          Preventive maintenance plans, sorted by next due date. Generation runs automatically in the background.
        </p>
      </div>

      {rowError && <p className="border-b border-border bg-status-danger/5 px-6 py-2 text-sm text-status-danger">{rowError}</p>}

      <div className="flex-1 overflow-auto">
        {status === 'loading' && <p className="px-6 py-10 text-center text-sm text-text-secondary">Loading plans…</p>}

        {status === 'error' && (
          <div className="flex flex-col items-center gap-3 px-6 py-16 text-center">
            <TriangleAlert className="h-6 w-6 text-status-danger" strokeWidth={1.5} />
            <p className="text-sm text-text-primary">{error instanceof ApiError ? error.message : 'Could not load plans.'}</p>
            <button
              type="button"
              onClick={reload}
              className="inline-flex items-center gap-1.5 rounded-sm border border-border px-3 py-1.5 text-sm text-text-primary hover:border-border-strong"
            >
              <RotateCw className="h-3.5 w-3.5" strokeWidth={1.75} />
              Retry
            </button>
          </div>
        )}

        {status === 'success' && data.plans.length === 0 && (
          <p className="px-6 py-10 text-center text-sm text-text-secondary">No maintenance plans yet.</p>
        )}

        {status === 'success' && data.plans.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px] border-collapse text-sm">
              <thead className="sticky top-0 border-b border-border bg-surface-raised text-left text-xs text-text-secondary">
                <tr>
                  <th className="px-6 py-2 font-medium">Title</th>
                  <th className="px-3 py-2 font-medium">Asset</th>
                  <th className="px-3 py-2 font-medium">Site</th>
                  <th className="px-3 py-2 font-medium">Recurrence</th>
                  <th className="px-3 py-2 font-medium">Next Due</th>
                  <th className="px-3 py-2 font-medium">Status</th>
                  <th className="px-6 py-2 font-medium">Actions</th>
                </tr>
              </thead>
              <tbody>
                {data.plans.map((plan) => (
                  <tr key={plan.id} className="border-b border-border last:border-b-0 hover:bg-surface">
                    <td className="px-6 py-2 text-text-primary">{plan.title}</td>
                    <td className="px-3 py-2 text-text-secondary">{describeAsset(plan, data.assets)}</td>
                    <td className="px-3 py-2 text-text-secondary">{siteNameById.get(plan.siteId) ?? '—'}</td>
                    <td className="px-3 py-2 text-text-secondary">
                      {plan.recurrenceType} · every {plan.intervalDays}d
                    </td>
                    <td className="px-3 py-2 font-mono text-xs text-text-secondary tabular-nums">
                      {new Date(plan.nextDueAtUtc).toLocaleDateString()}
                      {plan.activeOccurrenceId && <span className="ml-1.5 text-status-info">(generated)</span>}
                    </td>
                    <td className="px-3 py-2">
                      <MaintenancePlanStatusBadge status={plan.status} />
                    </td>
                    <td className="px-6 py-2">
                      {canManage(plan.siteId) && (
                        <button
                          type="button"
                          disabled={busyId === plan.id}
                          onClick={() => void handleToggle(plan)}
                          className="rounded-sm border border-border px-2 py-1 text-xs text-text-primary hover:border-border-strong disabled:opacity-60"
                        >
                          {plan.status === 'Active' ? 'Pause' : 'Resume'}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {showNew && (
        <NewMaintenancePlanDialog
          onClose={() => setShowNew(false)}
          onCreated={() => {
            setShowNew(false)
            reload()
          }}
        />
      )}
    </div>
  )
}
