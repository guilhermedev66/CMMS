import { Plus, RotateCw, TriangleAlert } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { getLocationPath, listAssets, listLocations, type Asset, type Location } from '../api/assets'
import { ApiError } from '../api/client'
import {
  cancelRequest,
  convertRequestToWorkOrder,
  listRequests,
  rejectRequest,
  type MaintenanceRequest,
} from '../api/requests'
import type { Priority } from '../api/shared'
import { useAuth } from '../auth/useAuth'
import { PriorityBadge } from '../components/PriorityBadge'
import { ReasonDialog } from '../components/ReasonDialog'
import { RequestStatusBadge } from '../components/RequestStatusBadge'
import { useAsync } from '../hooks/useAsync'
import { NewRequestDialog } from './NewRequestDialog'

export function RequestsListPage() {
  const { user } = useAuth()
  const { status, data, error, reload } = useAsync(
    () =>
      Promise.all([listRequests(), listAssets(), listLocations()]).then(
        ([requests, assets, locations]) => ({ requests, assets, locations }),
      ),
    [],
  )

  const [showNewRequest, setShowNewRequest] = useState(false)
  const [rejecting, setRejecting] = useState<MaintenanceRequest | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [rowError, setRowError] = useState<string | null>(null)

  const siteNameById = useMemo(
    () => new Map((user?.siteMemberships ?? []).map((m) => [m.siteId, m.siteName])),
    [user],
  )
  const canConvertOrReject = (siteId: string) =>
    user?.isAdmin || user?.siteMemberships.some((m) => m.siteId === siteId && m.role === 'Planner')

  function describeTarget(request: MaintenanceRequest, assets: Asset[], locations: Location[]) {
    if (request.assetId) {
      const asset = assets.find((a) => a.id === request.assetId)
      return asset ? `${asset.tag} — ${asset.name}` : 'Asset'
    }
    if (request.locationId) return getLocationPath(locations, request.locationId)
    return '—'
  }

  async function handleConvert(request: MaintenanceRequest) {
    setBusyId(request.id)
    setRowError(null)
    try {
      await convertRequestToWorkOrder(request.id)
      reload()
    } catch (err) {
      setRowError(err instanceof ApiError ? err.message : 'Could not convert this request.')
    } finally {
      setBusyId(null)
    }
  }

  async function handleReject(reason: string) {
    if (!rejecting) return
    setBusyId(rejecting.id)
    setRowError(null)
    try {
      await rejectRequest(rejecting.id, reason)
      reload()
    } catch (err) {
      setRowError(err instanceof ApiError ? err.message : 'Could not reject this request.')
    } finally {
      setBusyId(null)
      setRejecting(null)
    }
  }

  async function handleCancel(request: MaintenanceRequest) {
    setBusyId(request.id)
    setRowError(null)
    try {
      await cancelRequest(request.id)
      reload()
    } catch (err) {
      setRowError(err instanceof ApiError ? err.message : 'Could not cancel this request.')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="flex h-full flex-col">
      <div className="flex flex-col gap-1 border-b border-border px-6 py-4">
        <div className="flex items-baseline justify-between gap-2">
          <div className="flex items-baseline gap-2">
            <h1 className="text-lg font-semibold text-text-primary">Requests</h1>
            {data && (
              <span className="font-mono text-xs text-text-secondary tabular-nums">{data.requests.length}</span>
            )}
          </div>
          <button
            type="button"
            onClick={() => setShowNewRequest(true)}
            disabled={!user || user.siteMemberships.length === 0}
            className="inline-flex items-center gap-1.5 rounded-sm bg-accent px-3 py-1.5 text-sm font-medium text-accent-contrast disabled:opacity-60"
          >
            <Plus className="h-3.5 w-3.5" strokeWidth={2} />
            New Request
          </button>
        </div>
        <p className="text-sm text-text-secondary">Intake for corrective maintenance — convert to a Work Order once triaged.</p>
      </div>

      {rowError && <p className="border-b border-border bg-status-danger/5 px-6 py-2 text-sm text-status-danger">{rowError}</p>}

      <div className="flex-1 overflow-auto">
        {status === 'loading' && <p className="px-6 py-10 text-center text-sm text-text-secondary">Loading requests…</p>}

        {status === 'error' && (
          <div className="flex flex-col items-center gap-3 px-6 py-16 text-center">
            <TriangleAlert className="h-6 w-6 text-status-danger" strokeWidth={1.5} />
            <p className="text-sm text-text-primary">
              {error instanceof ApiError ? error.message : 'Could not load requests.'}
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

        {status === 'success' && data.requests.length === 0 && (
          <p className="px-6 py-10 text-center text-sm text-text-secondary">No requests yet.</p>
        )}

        {status === 'success' && data.requests.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px] border-collapse text-sm">
              <thead className="sticky top-0 border-b border-border bg-surface-raised text-left text-xs text-text-secondary">
                <tr>
                  <th className="px-6 py-2 font-medium">Title</th>
                  <th className="px-3 py-2 font-medium">Target</th>
                  <th className="px-3 py-2 font-medium">Site</th>
                  <th className="px-3 py-2 font-medium">Priority</th>
                  <th className="px-3 py-2 font-medium">Status</th>
                  <th className="px-3 py-2 font-medium">Created</th>
                  <th className="px-6 py-2 font-medium">Actions</th>
                </tr>
              </thead>
              <tbody>
                {data.requests.map((request) => (
                  <tr key={request.id} className="border-b border-border last:border-b-0 hover:bg-surface">
                    <td className="px-6 py-2 text-text-primary">
                      {request.convertedWorkOrderId ? (
                        <Link to={`/work-orders/${request.convertedWorkOrderId}`} className="text-accent hover:underline">
                          {request.title}
                        </Link>
                      ) : (
                        request.title
                      )}
                    </td>
                    <td className="px-3 py-2 text-text-secondary">{describeTarget(request, data.assets, data.locations)}</td>
                    <td className="px-3 py-2 text-text-secondary">{siteNameById.get(request.siteId) ?? '—'}</td>
                    <td className="px-3 py-2">
                      <PriorityBadge priority={request.priority as Priority} />
                    </td>
                    <td className="px-3 py-2">
                      <RequestStatusBadge status={request.status} />
                    </td>
                    <td className="px-3 py-2 font-mono text-xs text-text-secondary tabular-nums">
                      {new Date(request.createdAtUtc).toLocaleDateString()}
                    </td>
                    <td className="px-6 py-2">
                      {request.status === 'New' && (
                        <div className="flex flex-wrap items-center gap-2">
                          {canConvertOrReject(request.siteId) && (
                            <>
                              <button
                                type="button"
                                disabled={busyId === request.id}
                                onClick={() => void handleConvert(request)}
                                className="rounded-sm border border-border px-2 py-1 text-xs text-text-primary hover:border-border-strong disabled:opacity-60"
                              >
                                Convert
                              </button>
                              <button
                                type="button"
                                disabled={busyId === request.id}
                                onClick={() => setRejecting(request)}
                                className="rounded-sm border border-border px-2 py-1 text-xs text-text-primary hover:border-border-strong disabled:opacity-60"
                              >
                                Reject
                              </button>
                            </>
                          )}
                          {request.createdByUserId === user?.id && (
                            <button
                              type="button"
                              disabled={busyId === request.id}
                              onClick={() => void handleCancel(request)}
                              className="rounded-sm border border-border px-2 py-1 text-xs text-text-primary hover:border-border-strong disabled:opacity-60"
                            >
                              Cancel
                            </button>
                          )}
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {showNewRequest && (
        <NewRequestDialog
          onClose={() => setShowNewRequest(false)}
          onCreated={() => {
            setShowNewRequest(false)
            reload()
          }}
        />
      )}

      {rejecting && (
        <ReasonDialog commandLabel="Reject request" onCancel={() => setRejecting(null)} onConfirm={(reason) => void handleReject(reason)} />
      )}
    </div>
  )
}
