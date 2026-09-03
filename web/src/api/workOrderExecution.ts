import { apiClient, attachmentDownloadUrl, uploadAttachmentBytes } from './client'

/** Matches ChecklistItemType in src/Modules/WorkManagement/Domain/ChecklistItem.cs. */
export type ChecklistItemType = 'Boolean' | 'Numeric' | 'SingleSelect' | 'PhotoRequired' | 'Note'

/** Matches ChecklistItemResponse in src/Cmms.Api/WorkOrderExecutionEndpoints.cs. */
export interface ChecklistItem {
  id: string
  workOrderId: string
  executionCycle: number
  sortOrder: number
  itemType: ChecklistItemType
  label: string
  isRequired: boolean
  safetyCritical: boolean
  numericMinValue: number | null
  numericMaxValue: number | null
  numericUnit: string | null
  singleSelectOptionsCsv: string | null
  isResolved: boolean
  booleanValue: boolean | null
  numericValue: number | null
  selectedOption: string | null
  noteText: string | null
  attachmentId: string | null
  numericOutOfTolerance: boolean | null
  resolvedAtUtc: string | null
  resolvedByUserId: string | null
}

export function listChecklistItems(workOrderId: string): Promise<ChecklistItem[]> {
  return apiClient.get<ChecklistItem[]>(`/work-orders/${workOrderId}/checklist-items`)
}

export interface CreateChecklistItemInput {
  itemType: ChecklistItemType
  label: string
  isRequired: boolean
  safetyCritical?: boolean
  numericMinValue?: number | null
  numericMaxValue?: number | null
  numericUnit?: string | null
  singleSelectOptionsCsv?: string | null
}

export function createChecklistItem(workOrderId: string, input: CreateChecklistItemInput): Promise<ChecklistItem> {
  return apiClient.post<ChecklistItem>(`/work-orders/${workOrderId}/checklist-items`, {
    itemType: input.itemType,
    label: input.label,
    isRequired: input.isRequired,
    safetyCritical: input.safetyCritical ?? false,
    numericMinValue: input.numericMinValue ?? null,
    numericMaxValue: input.numericMaxValue ?? null,
    numericUnit: input.numericUnit ?? null,
    singleSelectOptionsCsv: input.singleSelectOptionsCsv ?? null,
  })
}

export interface ResolveChecklistItemInput {
  booleanValue?: boolean | null
  numericValue?: number | null
  selectedOption?: string | null
  noteText?: string | null
  attachmentId?: string | null
}

export function resolveChecklistItem(
  workOrderId: string,
  itemId: string,
  input: ResolveChecklistItemInput,
): Promise<ChecklistItem> {
  return apiClient.post<ChecklistItem>(`/work-orders/${workOrderId}/checklist-items/${itemId}/resolve`, {
    booleanValue: input.booleanValue ?? null,
    numericValue: input.numericValue ?? null,
    selectedOption: input.selectedOption ?? null,
    noteText: input.noteText ?? null,
    attachmentId: input.attachmentId ?? null,
  })
}

/** Matches DowntimeClassification/DowntimeCauseCategory in .../Domain/DowntimeInterval.cs. */
export type DowntimeClassification = 'FullStop' | 'PartialDerating'
export type DowntimeCauseCategory = 'Mechanical' | 'Electrical' | 'Hydraulic' | 'Pneumatic' | 'Instrumentation' | 'Operational'

export const downtimeCauseCategories: DowntimeCauseCategory[] = [
  'Mechanical',
  'Electrical',
  'Hydraulic',
  'Pneumatic',
  'Instrumentation',
  'Operational',
]

/** Matches DowntimeIntervalResponse in src/Cmms.Api/WorkOrderExecutionEndpoints.cs. */
export interface DowntimeInterval {
  id: string
  workOrderId: string
  assetId: string
  executionCycle: number
  classification: DowntimeClassification
  startedAtUtc: string
  endedAtUtc: string | null
  causeCategory: DowntimeCauseCategory | null
  causeMechanism: string | null
  recordedByUserId: string
}

export function listDowntimeIntervals(workOrderId: string): Promise<DowntimeInterval[]> {
  return apiClient.get<DowntimeInterval[]>(`/work-orders/${workOrderId}/downtime-intervals`)
}

export function openDowntimeInterval(workOrderId: string, classification: DowntimeClassification): Promise<DowntimeInterval> {
  return apiClient.post<DowntimeInterval>(`/work-orders/${workOrderId}/downtime-intervals`, { classification })
}

export function closeDowntimeInterval(
  workOrderId: string,
  intervalId: string,
  causeCategory: DowntimeCauseCategory,
  causeMechanism: string,
): Promise<DowntimeInterval> {
  return apiClient.post<DowntimeInterval>(`/work-orders/${workOrderId}/downtime-intervals/${intervalId}/close`, {
    causeCategory,
    causeMechanism,
  })
}

/** Matches PartUsageResponse — unitCost/currency come back null for a caller without costs.view (Technician). */
export interface PartUsage {
  id: string
  workOrderId: string
  executionCycle: number
  partName: string
  partCode: string | null
  quantity: number
  unitCost: number | null
  currency: string | null
  createdAtUtc: string
}

export function listPartUsages(workOrderId: string): Promise<PartUsage[]> {
  return apiClient.get<PartUsage[]>(`/work-orders/${workOrderId}/part-usages`)
}

export interface PostPartUsageInput {
  partName: string
  partCode?: string | null
  quantity: number
  unitCost: number
  currency: string
}

/** Generates and sends a fresh idempotency key per call — a retried tap on a flaky mobile
 * connection dedupes server-side (src/Modules/WorkManagement/Domain/PartUsage.cs) instead of
 * posting the same part twice. */
export function postPartUsage(workOrderId: string, input: PostPartUsageInput): Promise<PartUsage> {
  return apiClient.post<PartUsage>(`/work-orders/${workOrderId}/part-usages`, {
    partName: input.partName,
    partCode: input.partCode ?? null,
    quantity: input.quantity,
    unitCost: input.unitCost,
    currency: input.currency,
    idempotencyKey: crypto.randomUUID(),
  })
}

/** Raster evidence photos only — matches AttachmentsEndpoints.AllowedContentTypes. */
export type AttachmentContentType = 'image/jpeg' | 'image/png' | 'image/webp'

export interface UploadIntent {
  id: string
  status: 'Pending' | 'Uploaded' | 'Active' | 'Expired' | 'Rejected'
  expiresAtUtc: string
}

/** Matches AttachmentResponse in src/Cmms.Api/AttachmentsEndpoints.cs. */
export interface Attachment {
  id: string
  parentResourceId: string
  contentType: string
  byteSize: number
  pixelWidth: number
  pixelHeight: number
  uploadedByUserId: string
  createdAtUtc: string
}

export function listAttachments(workOrderId: string): Promise<Attachment[]> {
  return apiClient.get<Attachment[]>(`/work-orders/${workOrderId}/attachments`)
}

export function unlinkAttachment(attachmentId: string): Promise<void> {
  return apiClient.post<void>(`/attachments/${attachmentId}/unlink`)
}

export { attachmentDownloadUrl }

/**
 * The full upload pipeline (docs/02 § "Attachment strategy"): create an intent scoped to this
 * Work Order and content type, PUT the raw bytes, then finalize (server re-authorizes, decodes +
 * re-encodes, and only then the Attachment exists). Returns the finalized Attachment — callers
 * pass its `id` straight into resolveChecklistItem's `attachmentId` for a PhotoRequired item, or
 * just leave it as general evidence.
 */
export async function uploadEvidencePhoto(workOrderId: string, file: File): Promise<Attachment> {
  const contentType = (file.type || 'image/jpeg') as AttachmentContentType
  const intent = await apiClient.post<UploadIntent>(`/work-orders/${workOrderId}/attachments/upload-intents`, {
    declaredContentType: contentType,
    originalFileName: file.name || null,
  })

  await uploadAttachmentBytes(`/attachments/upload-intents/${intent.id}/bytes`, file, contentType)

  return apiClient.post<Attachment>(`/attachments/upload-intents/${intent.id}/finalize`)
}
