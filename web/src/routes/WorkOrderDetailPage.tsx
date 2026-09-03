import { ArrowLeft, RotateCw, TriangleAlert } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { getLocationPath, listAssets, listLocations } from '../api/assets'
import { ApiError } from '../api/client'
import { getWorkOrder } from '../api/workOrders'
import { PriorityBadge } from '../components/PriorityBadge'
import { StatusTransitionMenu } from '../components/StatusTransitionMenu'
import { WorkOrderStatusBadge } from '../components/WorkOrderStatusBadge'
import { useAsync } from '../hooks/useAsync'
import { useAuth } from '../auth/useAuth'
import { WorkOrderExecutionPanel } from './WorkOrderExecutionPanel'

const tabs = ['Overview', 'Execution'] as const
type Tab = (typeof tabs)[number]

export function WorkOrderDetailPage() {
  const { workOrderId } = useParams<{ workOrderId: string }>()
  const { user } = useAuth()
  const [activeTab, setActiveTab] = useState<Tab>('Overview')

  const { status, data, error, reload } = useAsync(
    () =>
      Promise.all([getWorkOrder(workOrderId!), listAssets(), listLocations()]).then(
        ([workOrder, assets, locations]) => ({ workOrder, assets, locations }),
      ),
    [workOrderId],
  )

  if (status === 'loading') {
    return <p className="px-6 py-16 text-center text-sm text-text-secondary">Loading Work Order…</p>
  }

  if (status === 'error') {
    const notFound = error instanceof ApiError && error.status === 404
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3 px-6 py-16 text-center">
        {notFound ? (
          <>
            <h1 className="text-base font-semibold text-text-primary">Work Order not found</h1>
            <p className="max-w-sm text-sm text-text-secondary">
              No Work Order matches “{workOrderId}”, or you don't have access to it.
            </p>
          </>
        ) : (
          <>
            <TriangleAlert className="h-6 w-6 text-status-danger" strokeWidth={1.5} />
            <p className="text-sm text-text-primary">
              {error instanceof ApiError ? error.message : 'Could not load this Work Order.'}
            </p>
            <button
              type="button"
              onClick={reload}
              className="inline-flex items-center gap-1.5 rounded-sm border border-border px-3 py-1.5 text-sm text-text-primary hover:border-border-strong"
            >
              <RotateCw className="h-3.5 w-3.5" strokeWidth={1.75} />
              Retry
            </button>
          </>
        )}
        <Link to="/work-orders" className="text-sm text-accent hover:underline">
          Back to Work Orders
        </Link>
      </div>
    )
  }

  const { workOrder, assets, locations } = data
  const asset = workOrder.assetId ? assets.find((a) => a.id === workOrder.assetId) : undefined
  const target = asset ? `${asset.tag} — ${asset.name}` : getLocationPath(locations, workOrder.locationId)
  const membership = user?.siteMemberships.find((m) => m.siteId === workOrder.siteId)
  const effectiveRole = user?.isAdmin ? 'Admin' : membership?.role

  return (
    <div className="flex h-full flex-col">
      <div className="border-b border-border px-6 py-4">
        <Link to="/work-orders" className="mb-3 inline-flex items-center gap-1 text-sm text-text-secondary hover:text-text-primary">
          <ArrowLeft className="h-3.5 w-3.5" strokeWidth={1.75} />
          Work Orders
        </Link>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0">
            <h1 className="text-lg font-semibold text-text-primary">{workOrder.title}</h1>
            <p className="mt-1 text-sm text-text-secondary">{target}</p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            <PriorityBadge priority={workOrder.priority} />
            <WorkOrderStatusBadge status={workOrder.status} />
          </div>
        </div>
      </div>

      <div className="overflow-x-auto border-b border-border px-6">
        <div className="flex min-w-max gap-4" role="tablist" aria-label="Work Order detail sections">
          {tabs.map((tab) => (
            <button
              key={tab}
              type="button"
              role="tab"
              aria-selected={activeTab === tab}
              onClick={() => setActiveTab(tab)}
              className={`border-b-2 px-1 py-3 text-sm whitespace-nowrap transition-colors ${
                activeTab === tab
                  ? 'border-accent font-medium text-accent'
                  : 'border-transparent text-text-secondary hover:text-text-primary'
              }`}
            >
              {tab}
            </button>
          ))}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-6">
        {activeTab === 'Overview' && (
          <div className="py-6">
            <div className="mb-6 max-w-xs">
              <p className="mb-1 text-xs text-text-secondary">Actions</p>
              <StatusTransitionMenu workOrder={workOrder} onChanged={reload} />
            </div>

            <dl className="grid grid-cols-1 gap-x-8 gap-y-4 sm:grid-cols-2">
              <Field label="Description" value={workOrder.description ?? '—'} />
              <Field label="Execution cycle" value={<span className="font-mono tabular-nums">{workOrder.executionCycle}</span>} />
              <Field label="Assignee" value={workOrder.assigneeId ? <span className="font-mono text-xs tabular-nums">{workOrder.assigneeId}</span> : 'Unassigned'} />
              <Field
                label="Created"
                value={new Date(workOrder.createdAtUtc).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}
              />
              {workOrder.wrenchStartAtUtc && (
                <Field label="Started" value={new Date(workOrder.wrenchStartAtUtc).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })} />
              )}
              {workOrder.completedAtUtc && (
                <Field label="Completed" value={new Date(workOrder.completedAtUtc).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })} />
              )}
              {workOrder.closedAtUtc && (
                <Field label="Closed" value={new Date(workOrder.closedAtUtc).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })} />
              )}
              {workOrder.cancelReason && <Field label="Cancel reason" value={workOrder.cancelReason} />}
              {workOrder.reopenReason && <Field label="Reopen reason" value={workOrder.reopenReason} />}
            </dl>
          </div>
        )}

        {activeTab === 'Execution' && (
          <WorkOrderExecutionPanel workOrder={workOrder} currentUserId={user?.id} effectiveRole={effectiveRole} />
        )}
      </div>
    </div>
  )
}

function Field({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-text-secondary">{label}</dt>
      <dd className="mt-0.5 text-sm text-text-primary">{value}</dd>
    </div>
  )
}
