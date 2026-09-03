import { apiClient } from './client'
import type { Priority } from './shared'

/**
 * Matches WorkOrderStatus in src/Modules/WorkManagement/Domain/WorkOrderStatus.cs. Narrower than
 * the full docs/01-domain-and-workflows.md lifecycle: this M2 slice has no OnHold and no
 * Planner-driven Assign/Reassign/Unassign — only self-claim moves Open -> Scheduled. See
 * src/Cmms.Api/WorkOrdersEndpoints.cs's doc comment for the "SCOPE CUT" rationale.
 */
export type WorkOrderStatus = 'Draft' | 'Open' | 'Scheduled' | 'InProgress' | 'Completed' | 'Closed' | 'Cancelled'

/** Matches WorkOrderResponse in src/Cmms.Api/WorkOrdersEndpoints.cs. */
export interface WorkOrder {
  id: string
  siteId: string
  title: string
  description: string | null
  assetId: string | null
  locationId: string | null
  status: WorkOrderStatus
  priority: Priority
  assigneeId: string | null
  assignedAtUtc: string | null
  createdByUserId: string
  createdAtUtc: string
  wrenchStartAtUtc: string | null
  completedAtUtc: string | null
  completedByUserId: string | null
  closedAtUtc: string | null
  closedByUserId: string | null
  cancelledAtUtc: string | null
  cancelReason: string | null
  reopenReason: string | null
  executionCycle: number
  sourceRequestId: string | null
  rowVersion: number
}

/**
 * The transitions this slice's backend actually exposes, in the same shape as
 * src/Modules/WorkManagement/Domain/WorkOrder.cs's public methods + the self-claim endpoint. Used
 * to drive which action(s) the detail page offers for a given status — an operation not listed
 * here has no endpoint at all.
 */
export interface WorkOrderAction {
  command: string
  endpoint: string
  requiresReason?: boolean
}

const ACTIONS_BY_STATUS: Record<WorkOrderStatus, WorkOrderAction[]> = {
  Draft: [{ command: 'Publish', endpoint: 'publish' }],
  Open: [
    { command: 'Self-claim', endpoint: 'self-claim' },
    { command: 'Cancel', endpoint: 'cancel', requiresReason: true },
  ],
  Scheduled: [
    { command: 'Start Work', endpoint: 'start' },
    { command: 'Cancel', endpoint: 'cancel', requiresReason: true },
  ],
  InProgress: [
    { command: 'Mark Completed', endpoint: 'complete' },
    { command: 'Cancel', endpoint: 'cancel', requiresReason: true },
  ],
  Completed: [
    { command: 'Close', endpoint: 'close' },
    { command: 'Reopen', endpoint: 'reopen', requiresReason: true },
  ],
  Closed: [{ command: 'Reopen', endpoint: 'reopen', requiresReason: true }],
  Cancelled: [],
}

export function getAvailableActions(status: WorkOrderStatus): WorkOrderAction[] {
  return ACTIONS_BY_STATUS[status]
}

export function listWorkOrders(): Promise<WorkOrder[]> {
  return apiClient.get<WorkOrder[]>('/work-orders')
}

export function getWorkOrder(id: string): Promise<WorkOrder> {
  return apiClient.get<WorkOrder>(`/work-orders/${id}`)
}

export interface CreateWorkOrderInput {
  siteId: string
  title: string
  description?: string | null
  assetId?: string | null
  locationId?: string | null
  priority?: Priority
}

export function createWorkOrder(input: CreateWorkOrderInput): Promise<WorkOrder> {
  return apiClient.post<WorkOrder>('/work-orders', {
    siteId: input.siteId,
    title: input.title,
    description: input.description ?? null,
    assetId: input.assetId ?? null,
    locationId: input.locationId ?? null,
    priority: input.priority ?? 'P3',
  })
}

/** Runs one of the actions from getAvailableActions against the real endpoint. */
export function runWorkOrderAction(id: string, action: WorkOrderAction, reason?: string): Promise<WorkOrder> {
  const body = action.requiresReason ? { reason } : undefined
  return apiClient.post<WorkOrder>(`/work-orders/${id}/${action.endpoint}`, body)
}
