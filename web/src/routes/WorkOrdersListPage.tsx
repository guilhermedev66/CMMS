import { Plus, RotateCw, TriangleAlert } from 'lucide-react'
import { useMemo, useState } from 'react'
import { getLocationPath, listAssets, listLocations, type Asset, type Location } from '../api/assets'
import { ApiError } from '../api/client'
import { listWorkOrders, type WorkOrder } from '../api/workOrders'
import { useAuth } from '../auth/useAuth'
import { PriorityBadge } from '../components/PriorityBadge'
import { StatusTransitionMenu } from '../components/StatusTransitionMenu'
import { WorkOrderStatusBadge } from '../components/WorkOrderStatusBadge'
import { useAsync } from '../hooks/useAsync'
import { NewWorkOrderDialog } from './NewWorkOrderDialog'

/**
 * Grid view only (docs/04-frontend-ia.md's "default for planners" mode). The Kanban board with
 * drag-and-drop is deferred — same documented-scope-cut pattern as the rest of this M2 slice (see
 * src/Cmms.Api/WorkOrdersEndpoints.cs's doc comment) — this Grid already exercises every guarded
 * transition via StatusTransitionMenu, which is what the M2 DoD actually requires.
 */
export function WorkOrdersListPage() {
  const { user } = useAuth()
  const { status, data, error, reload } = useAsync(
    () =>
      Promise.all([listWorkOrders(), listAssets(), listLocations()]).then(
        ([workOrders, assets, locations]) => ({ workOrders, assets, locations }),
      ),
    [],
  )

  const [showNew, setShowNew] = useState(false)

  const siteNameById = useMemo(
    () => new Map((user?.siteMemberships ?? []).map((m) => [m.siteId, m.siteName])),
    [user],
  )

  function describeTarget(workOrder: WorkOrder, assets: Asset[], locations: Location[]) {
    if (workOrder.assetId) {
      const asset = assets.find((a) => a.id === workOrder.assetId)
      return asset ? `${asset.tag} — ${asset.name}` : 'Asset'
    }
    if (workOrder.locationId) return getLocationPath(locations, workOrder.locationId)
    return '—'
  }

  return (
    <div className="flex h-full flex-col">
      <div className="flex flex-col gap-1 border-b border-border px-6 py-4">
        <div className="flex items-baseline justify-between gap-2">
          <div className="flex items-baseline gap-2">
            <h1 className="text-lg font-semibold text-text-primary">Work Orders</h1>
            {data && (
              <span className="font-mono text-xs text-text-secondary tabular-nums">{data.workOrders.length}</span>
            )}
          </div>
          <button
            type="button"
            onClick={() => setShowNew(true)}
            disabled={!user || user.siteMemberships.length === 0}
            className="inline-flex items-center gap-1.5 rounded-sm bg-accent px-3 py-1.5 text-sm font-medium text-accent-contrast disabled:opacity-60"
          >
            <Plus className="h-3.5 w-3.5" strokeWidth={2} />
            New Work Order
          </button>
        </div>
        <p className="text-sm text-text-secondary">
          Guarded lifecycle: Draft → Open → Scheduled (self-claim) → In Progress → Completed → Closed.
        </p>
      </div>

      <div className="flex-1 overflow-auto">
        {status === 'loading' && <p className="px-6 py-10 text-center text-sm text-text-secondary">Loading Work Orders…</p>}

        {status === 'error' && (
          <div className="flex flex-col items-center gap-3 px-6 py-16 text-center">
            <TriangleAlert className="h-6 w-6 text-status-danger" strokeWidth={1.5} />
            <p className="text-sm text-text-primary">
              {error instanceof ApiError ? error.message : 'Could not load Work Orders.'}
            </p>
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

        {status === 'success' && data.workOrders.length === 0 && (
          <p className="px-6 py-10 text-center text-sm text-text-secondary">No Work Orders yet.</p>
        )}

        {status === 'success' && data.workOrders.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[960px] border-collapse text-sm">
              <thead className="sticky top-0 border-b border-border bg-surface-raised text-left text-xs text-text-secondary">
                <tr>
                  <th className="px-6 py-2 font-medium">Title</th>
                  <th className="px-3 py-2 font-medium">Target</th>
                  <th className="px-3 py-2 font-medium">Site</th>
                  <th className="px-3 py-2 font-medium">Priority</th>
                  <th className="px-3 py-2 font-medium">Status</th>
                  <th className="px-3 py-2 font-medium">Assignee</th>
                  <th className="px-6 py-2 font-medium">Actions</th>
                </tr>
              </thead>
              <tbody>
                {data.workOrders.map((workOrder) => (
                  <WorkOrderRow
                    key={workOrder.id}
                    workOrder={workOrder}
                    target={describeTarget(workOrder, data.assets, data.locations)}
                    siteName={siteNameById.get(workOrder.siteId) ?? '—'}
                    currentUserId={user?.id}
                    onChanged={reload}
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {showNew && (
        <NewWorkOrderDialog
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

function WorkOrderRow({
  workOrder,
  target,
  siteName,
  currentUserId,
  onChanged,
}: {
  workOrder: WorkOrder
  target: string
  siteName: string
  currentUserId: string | undefined
  onChanged: () => void
}) {
  return (
    <tr className="border-b border-border last:border-b-0 hover:bg-surface">
      <td className="px-6 py-2 text-text-primary">{workOrder.title}</td>
      <td className="px-3 py-2 text-text-secondary">{target}</td>
      <td className="px-3 py-2 text-text-secondary">{siteName}</td>
      <td className="px-3 py-2">
        <PriorityBadge priority={workOrder.priority} />
      </td>
      <td className="px-3 py-2">
        <WorkOrderStatusBadge status={workOrder.status} />
      </td>
      <td className="px-3 py-2 text-text-secondary">
        {workOrder.assigneeId ? (workOrder.assigneeId === currentUserId ? 'You' : 'Assigned') : '—'}
      </td>
      <td className="px-6 py-2">
        <StatusTransitionMenu
          workOrder={workOrder}
          onChanged={onChanged}
        />
      </td>
    </tr>
  )
}
