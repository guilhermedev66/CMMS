import { apiClient } from './client'
import type { Priority } from './shared'
import { getWorkOrder, type WorkOrder } from './workOrders'

/** Matches MaintenanceRequestStatus in src/Modules/MaintenanceRequests/Domain/MaintenanceRequestStatus.cs. */
export type RequestStatus = 'New' | 'Converted' | 'Rejected' | 'Cancelled'

/** Matches RequestResponse in src/Cmms.Api/MaintenanceRequestsEndpoints.cs. */
export interface MaintenanceRequest {
  id: string
  siteId: string
  createdByUserId: string
  title: string
  description: string | null
  assetId: string | null
  locationId: string | null
  priority: Priority
  status: RequestStatus
  convertedWorkOrderId: string | null
  rejectedReason: string | null
  createdAtUtc: string
  resolvedAtUtc: string | null
  rowVersion: number
}

export function listRequests(): Promise<MaintenanceRequest[]> {
  return apiClient.get<MaintenanceRequest[]>('/requests')
}

export function getRequest(id: string): Promise<MaintenanceRequest> {
  return apiClient.get<MaintenanceRequest>(`/requests/${id}`)
}

export interface CreateRequestInput {
  siteId: string
  title: string
  description?: string | null
  assetId?: string | null
  locationId?: string | null
  priority?: Priority
}

export function createRequest(input: CreateRequestInput): Promise<MaintenanceRequest> {
  return apiClient.post<MaintenanceRequest>('/requests', {
    siteId: input.siteId,
    title: input.title,
    description: input.description ?? null,
    assetId: input.assetId ?? null,
    locationId: input.locationId ?? null,
    priority: input.priority ?? 'P3',
  })
}

/** Converts a New request into a Work Order; returns the created Work Order's id. */
export async function convertRequestToWorkOrder(requestId: string, title?: string): Promise<WorkOrder> {
  const result = await apiClient.post<{ workOrderId: string }>(`/requests/${requestId}/convert`, {
    title: title ?? null,
  })
  return getWorkOrder(result.workOrderId)
}

export function rejectRequest(requestId: string, reason: string): Promise<{ id: string; status: RequestStatus }> {
  return apiClient.post(`/requests/${requestId}/reject`, { reason })
}

export function cancelRequest(requestId: string): Promise<{ id: string; status: RequestStatus }> {
  return apiClient.post(`/requests/${requestId}/cancel`)
}
