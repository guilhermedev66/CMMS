import { apiClient } from './client'

/** Matches RecurrenceType in src/Modules/PreventiveMaintenance/Domain/MaintenancePlanEnums.cs. */
export type RecurrenceType = 'Fixed' | 'Floating'

export type MaintenancePlanStatus = 'Active' | 'Paused'

/** Matches MaintenancePlanResponse in src/Cmms.Api/MaintenancePlansEndpoints.cs. */
export interface MaintenancePlan {
  id: string
  siteId: string
  assetId: string
  title: string
  description: string | null
  recurrenceType: RecurrenceType
  intervalDays: number
  generationLeadTimeDays: number
  status: MaintenancePlanStatus
  nextDueAtUtc: string
  activeOccurrenceId: string | null
  createdAtUtc: string
  rowVersion: number
}

export function listMaintenancePlans(): Promise<MaintenancePlan[]> {
  return apiClient.get<MaintenancePlan[]>('/maintenance-plans')
}

export interface CreateMaintenancePlanInput {
  siteId: string
  assetId: string
  title: string
  description?: string | null
  recurrenceType: RecurrenceType
  intervalDays: number
  generationLeadTimeDays?: number
  firstDueAtUtc: string
}

export function createMaintenancePlan(input: CreateMaintenancePlanInput): Promise<MaintenancePlan> {
  return apiClient.post<MaintenancePlan>('/maintenance-plans', {
    siteId: input.siteId,
    assetId: input.assetId,
    title: input.title,
    description: input.description ?? null,
    recurrenceType: input.recurrenceType,
    intervalDays: input.intervalDays,
    generationLeadTimeDays: input.generationLeadTimeDays ?? 0,
    firstDueAtUtc: input.firstDueAtUtc,
  })
}

export function pauseMaintenancePlan(id: string): Promise<MaintenancePlan> {
  return apiClient.post<MaintenancePlan>(`/maintenance-plans/${id}/pause`)
}

export function resumeMaintenancePlan(id: string): Promise<MaintenancePlan> {
  return apiClient.post<MaintenancePlan>(`/maintenance-plans/${id}/resume`)
}
