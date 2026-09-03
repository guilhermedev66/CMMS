import { Camera, CheckCircle2, Circle, Plus, TriangleAlert, X } from 'lucide-react'
import { useRef, useState, type ChangeEvent, type ReactNode } from 'react'
import { ApiError } from '../api/client'
import type { RoleCode } from '../api/auth'
import type { WorkOrder } from '../api/workOrders'
import {
  attachmentDownloadUrl,
  closeDowntimeInterval,
  createChecklistItem,
  downtimeCauseCategories,
  listAttachments,
  listChecklistItems,
  listDowntimeIntervals,
  listPartUsages,
  openDowntimeInterval,
  postPartUsage,
  resolveChecklistItem,
  unlinkAttachment,
  uploadEvidencePhoto,
  type Attachment,
  type ChecklistItem,
  type ChecklistItemType,
  type DowntimeCauseCategory,
  type DowntimeClassification,
  type DowntimeInterval,
  type PartUsage,
} from '../api/workOrderExecution'
import { useAsync } from '../hooks/useAsync'

interface WorkOrderExecutionPanelProps {
  workOrder: WorkOrder
  currentUserId: string | undefined
  effectiveRole: RoleCode | undefined
}

const inputClass =
  'w-full rounded-sm border border-border bg-surface px-2.5 py-1.5 text-sm text-text-primary placeholder:text-text-secondary focus:border-border-strong focus:ring-1 focus:ring-accent focus:outline-none'

/**
 * The M4 technician workflow: checklist, downtime, parts, evidence photos for one Work Order's
 * current execution cycle. Every write here is gated the same way the server gates it (assignee-
 * or-Planner/Admin, InProgress-only for most of them) — a UX narrowing, not the real boundary; the
 * server re-checks everything (src/Cmms.Api/WorkOrderExecutionEndpoints.cs,
 * src/Cmms.Api/AttachmentsEndpoints.cs).
 */
export function WorkOrderExecutionPanel({ workOrder, currentUserId, effectiveRole }: WorkOrderExecutionPanelProps) {
  const isPlannerOrAdmin = effectiveRole === 'Planner' || effectiveRole === 'Admin'
  const isAssignee = !!currentUserId && currentUserId === workOrder.assigneeId
  const canExecute = (isAssignee || isPlannerOrAdmin) && workOrder.status === 'InProgress'

  const { status, data, error, reload } = useAsync(
    () =>
      Promise.all([
        listChecklistItems(workOrder.id),
        listDowntimeIntervals(workOrder.id),
        listPartUsages(workOrder.id),
        listAttachments(workOrder.id),
      ]).then(([checklistItems, downtimeIntervals, partUsages, attachments]) => ({
        checklistItems,
        downtimeIntervals,
        partUsages,
        attachments,
      })),
    [workOrder.id, workOrder.executionCycle],
  )

  if (status === 'loading') {
    return <p className="py-8 text-center text-sm text-text-secondary">Loading execution data…</p>
  }

  if (status === 'error') {
    return (
      <div className="flex flex-col items-center gap-2 py-8 text-center">
        <TriangleAlert className="h-5 w-5 text-status-danger" strokeWidth={1.5} />
        <p className="text-sm text-text-primary">
          {error instanceof ApiError ? error.message : 'Could not load checklist/downtime/parts data.'}
        </p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-8 py-6">
      <ChecklistSection
        workOrder={workOrder}
        items={data.checklistItems}
        attachments={data.attachments}
        canExecute={canExecute}
        canDefine={isPlannerOrAdmin}
        onChanged={reload}
      />
      <DowntimeSection workOrder={workOrder} intervals={data.downtimeIntervals} canExecute={canExecute} onChanged={reload} />
      <PartsSection workOrder={workOrder} usages={data.partUsages} canExecute={canExecute} onChanged={reload} />
      <EvidenceSection workOrder={workOrder} attachments={data.attachments} canExecute={canExecute} onChanged={reload} />
    </div>
  )
}

function SectionHeading({ children }: { children: ReactNode }) {
  return <h3 className="mb-3 text-sm font-semibold text-text-primary">{children}</h3>
}

function ErrorLine({ message }: { message: string | null }) {
  if (!message) return null
  return <p className="mt-2 text-xs text-status-danger">{message}</p>
}

// ---------- Checklist ----------

function ChecklistSection({
  workOrder,
  items,
  attachments,
  canExecute,
  canDefine,
  onChanged,
}: {
  workOrder: WorkOrder
  items: ChecklistItem[]
  attachments: Attachment[]
  canExecute: boolean
  canDefine: boolean
  onChanged: () => void
}) {
  const [showAddForm, setShowAddForm] = useState(false)
  const sorted = [...items].sort((a, b) => a.sortOrder - b.sortOrder)

  return (
    <section>
      <div className="mb-3 flex items-center justify-between">
        <SectionHeading>Checklist</SectionHeading>
        {canDefine && (
          <button
            type="button"
            onClick={() => setShowAddForm((v) => !v)}
            className="inline-flex items-center gap-1 rounded-sm border border-border px-2 py-1 text-xs text-text-primary hover:border-border-strong"
          >
            <Plus className="h-3.5 w-3.5" strokeWidth={1.75} />
            Add item
          </button>
        )}
      </div>

      {showAddForm && (
        <AddChecklistItemForm workOrderId={workOrder.id} onAdded={() => { setShowAddForm(false); onChanged() }} onCancel={() => setShowAddForm(false)} />
      )}

      {sorted.length === 0 ? (
        <p className="text-sm text-text-secondary">No checklist items for this execution cycle.</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {sorted.map((item) => (
            <ChecklistItemRow key={item.id} workOrderId={workOrder.id} item={item} attachments={attachments} canExecute={canExecute} onChanged={onChanged} />
          ))}
        </ul>
      )}
    </section>
  )
}

function AddChecklistItemForm({ workOrderId, onAdded, onCancel }: { workOrderId: string; onAdded: () => void; onCancel: () => void }) {
  const [itemType, setItemType] = useState<ChecklistItemType>('Boolean')
  const [label, setLabel] = useState('')
  const [isRequired, setIsRequired] = useState(true)
  const [safetyCritical, setSafetyCritical] = useState(false)
  const [numericMinValue, setNumericMinValue] = useState('')
  const [numericMaxValue, setNumericMaxValue] = useState('')
  const [numericUnit, setNumericUnit] = useState('')
  const [options, setOptions] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit() {
    if (!label.trim()) return
    setSubmitting(true)
    setError(null)
    try {
      await createChecklistItem(workOrderId, {
        itemType,
        label: label.trim(),
        isRequired,
        safetyCritical: itemType === 'Boolean' ? safetyCritical : false,
        numericMinValue: numericMinValue ? Number(numericMinValue) : null,
        numericMaxValue: numericMaxValue ? Number(numericMaxValue) : null,
        numericUnit: numericUnit.trim() || null,
        singleSelectOptionsCsv: options.trim() || null,
      })
      onAdded()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not add this checklist item.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="mb-3 flex flex-col gap-2 rounded-sm border border-border bg-surface-raised p-3">
      <div className="flex flex-col gap-2 sm:flex-row">
        <select value={itemType} onChange={(e) => setItemType(e.target.value as ChecklistItemType)} className={`${inputClass} sm:w-40`}>
          <option value="Boolean">Boolean (pass/fail)</option>
          <option value="Numeric">Numeric</option>
          <option value="SingleSelect">Single select</option>
          <option value="PhotoRequired">Photo required</option>
          <option value="Note">Note</option>
        </select>
        <input type="text" value={label} onChange={(e) => setLabel(e.target.value)} placeholder="Item label…" className={inputClass} />
      </div>

      {itemType === 'Numeric' && (
        <div className="flex flex-col gap-2 sm:flex-row">
          <input type="number" value={numericMinValue} onChange={(e) => setNumericMinValue(e.target.value)} placeholder="Min" className={inputClass} />
          <input type="number" value={numericMaxValue} onChange={(e) => setNumericMaxValue(e.target.value)} placeholder="Max" className={inputClass} />
          <input type="text" value={numericUnit} onChange={(e) => setNumericUnit(e.target.value)} placeholder="Unit" className={inputClass} />
        </div>
      )}

      {itemType === 'SingleSelect' && (
        <input type="text" value={options} onChange={(e) => setOptions(e.target.value)} placeholder="Options, comma-separated" className={inputClass} />
      )}

      <div className="flex flex-wrap items-center gap-4">
        <label className="flex items-center gap-1.5 text-xs text-text-secondary">
          <input type="checkbox" checked={isRequired} onChange={(e) => setIsRequired(e.target.checked)} />
          Required
        </label>
        {itemType === 'Boolean' && (
          <label className="flex items-center gap-1.5 text-xs text-text-secondary">
            <input type="checkbox" checked={safetyCritical} onChange={(e) => setSafetyCritical(e.target.checked)} />
            Safety critical
          </label>
        )}
      </div>

      <ErrorLine message={error} />

      <div className="mt-1 flex justify-end gap-2">
        <button type="button" onClick={onCancel} className="rounded-sm border border-border px-2.5 py-1 text-xs text-text-primary hover:border-border-strong">
          Cancel
        </button>
        <button
          type="button"
          disabled={submitting || !label.trim()}
          onClick={() => void handleSubmit()}
          className="rounded-sm bg-accent px-2.5 py-1 text-xs font-medium text-accent-contrast disabled:opacity-60"
        >
          Add
        </button>
      </div>
    </div>
  )
}

function ChecklistItemRow({
  workOrderId,
  item,
  attachments,
  canExecute,
  onChanged,
}: {
  workOrderId: string
  item: ChecklistItem
  attachments: Attachment[]
  canExecute: boolean
  onChanged: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const [numericValue, setNumericValue] = useState('')
  const [selectedOption, setSelectedOption] = useState('')
  const [noteText, setNoteText] = useState('')

  async function resolve(input: Parameters<typeof resolveChecklistItem>[2]) {
    setBusy(true)
    setError(null)
    try {
      await resolveChecklistItem(workOrderId, item.id, input)
      onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save this item.')
    } finally {
      setBusy(false)
    }
  }

  async function handlePhotoSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      const attachment = await uploadEvidencePhoto(workOrderId, file)
      await resolveChecklistItem(workOrderId, item.id, { attachmentId: attachment.id })
      onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not upload this photo.')
    } finally {
      setBusy(false)
    }
  }

  const linkedAttachment = item.attachmentId ? attachments.find((a) => a.id === item.attachmentId) : undefined

  return (
    <li className="rounded-sm border border-border bg-surface-raised p-3">
      <div className="flex items-start gap-2">
        {item.isResolved ? (
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-status-success" strokeWidth={2} />
        ) : (
          <Circle className="mt-0.5 h-4 w-4 shrink-0 text-text-secondary" strokeWidth={1.75} />
        )}
        <div className="min-w-0 flex-1">
          <p className="text-sm text-text-primary">
            {item.label}
            {item.isRequired && <span className="ml-1 text-status-danger">*</span>}
            {item.safetyCritical && (
              <span className="ml-2 rounded-sm border border-status-warning/30 bg-status-warning/10 px-1.5 py-0.5 text-[10px] font-medium text-status-warning">
                Safety critical
              </span>
            )}
          </p>

          {item.isResolved ? (
            <ResolvedAnswer item={item} attachment={linkedAttachment} />
          ) : canExecute ? (
            <div className="mt-2 flex flex-wrap items-center gap-2">
              {item.itemType === 'Boolean' && (
                <>
                  <button type="button" disabled={busy} onClick={() => void resolve({ booleanValue: true })} className="rounded-sm border border-status-success/30 bg-status-success/10 px-2.5 py-1 text-xs text-status-success disabled:opacity-60">
                    Pass
                  </button>
                  <button type="button" disabled={busy} onClick={() => void resolve({ booleanValue: false })} className="rounded-sm border border-status-danger/30 bg-status-danger/10 px-2.5 py-1 text-xs text-status-danger disabled:opacity-60">
                    Fail
                  </button>
                </>
              )}

              {item.itemType === 'Numeric' && (
                <>
                  <input
                    type="number"
                    value={numericValue}
                    onChange={(e) => setNumericValue(e.target.value)}
                    placeholder={item.numericUnit ? `Value (${item.numericUnit})` : 'Value'}
                    className={`${inputClass} w-32`}
                  />
                  {(item.numericMinValue !== null || item.numericMaxValue !== null) && (
                    <span className="text-xs text-text-secondary">
                      Tolerance: {item.numericMinValue ?? '–∞'} to {item.numericMaxValue ?? '∞'}
                    </span>
                  )}
                  <button
                    type="button"
                    disabled={busy || numericValue === ''}
                    onClick={() => void resolve({ numericValue: Number(numericValue) })}
                    className="rounded-sm bg-accent px-2.5 py-1 text-xs font-medium text-accent-contrast disabled:opacity-60"
                  >
                    Save
                  </button>
                </>
              )}

              {item.itemType === 'SingleSelect' && (
                <>
                  <select value={selectedOption} onChange={(e) => setSelectedOption(e.target.value)} className={`${inputClass} w-40`}>
                    <option value="">Select…</option>
                    {(item.singleSelectOptionsCsv ?? '').split(',').map((o) => o.trim()).filter(Boolean).map((option) => (
                      <option key={option} value={option}>
                        {option}
                      </option>
                    ))}
                  </select>
                  <button
                    type="button"
                    disabled={busy || !selectedOption}
                    onClick={() => void resolve({ selectedOption })}
                    className="rounded-sm bg-accent px-2.5 py-1 text-xs font-medium text-accent-contrast disabled:opacity-60"
                  >
                    Save
                  </button>
                </>
              )}

              {item.itemType === 'PhotoRequired' && (
                <>
                  <input ref={fileInputRef} type="file" accept="image/jpeg,image/png,image/webp" capture="environment" className="hidden" onChange={(e) => void handlePhotoSelected(e)} />
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => fileInputRef.current?.click()}
                    className="inline-flex items-center gap-1.5 rounded-sm bg-accent px-2.5 py-1 text-xs font-medium text-accent-contrast disabled:opacity-60"
                  >
                    <Camera className="h-3.5 w-3.5" strokeWidth={1.75} />
                    {busy ? 'Uploading…' : 'Take / upload photo'}
                  </button>
                </>
              )}

              {item.itemType === 'Note' && (
                <>
                  <textarea value={noteText} onChange={(e) => setNoteText(e.target.value)} rows={2} placeholder="Note…" className={`${inputClass} resize-none`} />
                  <button
                    type="button"
                    disabled={busy || !noteText.trim()}
                    onClick={() => void resolve({ noteText: noteText.trim() })}
                    className="rounded-sm bg-accent px-2.5 py-1 text-xs font-medium text-accent-contrast disabled:opacity-60"
                  >
                    Save
                  </button>
                </>
              )}
            </div>
          ) : (
            <p className="mt-1 text-xs text-text-secondary">Not resolved yet.</p>
          )}

          <ErrorLine message={error} />
        </div>
      </div>
    </li>
  )
}

function ResolvedAnswer({ item, attachment }: { item: ChecklistItem; attachment: Attachment | undefined }) {
  switch (item.itemType) {
    case 'Boolean':
      return <p className={`mt-1 text-xs ${item.booleanValue ? 'text-status-success' : 'text-status-danger'}`}>{item.booleanValue ? 'Pass' : 'Fail'}</p>
    case 'Numeric':
      return (
        <p className={`mt-1 text-xs ${item.numericOutOfTolerance ? 'text-status-danger' : 'text-text-secondary'}`}>
          {item.numericValue} {item.numericUnit ?? ''}
          {item.numericOutOfTolerance && ' — out of tolerance'}
        </p>
      )
    case 'SingleSelect':
      return <p className="mt-1 text-xs text-text-secondary">{item.selectedOption}</p>
    case 'Note':
      return <p className="mt-1 text-xs text-text-secondary">{item.noteText}</p>
    case 'PhotoRequired':
      return attachment ? (
        <img src={attachmentDownloadUrl(attachment.id)} alt={item.label} className="mt-2 h-24 w-24 rounded-sm border border-border object-cover" />
      ) : (
        <p className="mt-1 text-xs text-text-secondary">Photo attached.</p>
      )
    default:
      return null
  }
}

// ---------- Downtime ----------

function DowntimeSection({
  workOrder,
  intervals,
  canExecute,
  onChanged,
}: {
  workOrder: WorkOrder
  intervals: DowntimeInterval[]
  canExecute: boolean
  onChanged: () => void
}) {
  const [opening, setOpening] = useState(false);
  const [error, setError] = useState<string | null>(null)
  const openInterval = intervals.find((i) => i.endedAtUtc === null)

  async function handleOpen(classification: DowntimeClassification) {
    setOpening(true)
    setError(null)
    try {
      await openDowntimeInterval(workOrder.id, classification)
      onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not open a downtime interval.')
    } finally {
      setOpening(false)
    }
  }

  return (
    <section>
      <SectionHeading>Downtime</SectionHeading>

      {canExecute && !openInterval && workOrder.assetId && (
        <div className="mb-3 flex gap-2">
          <button type="button" disabled={opening} onClick={() => void handleOpen('FullStop')} className="rounded-sm border border-status-danger/30 bg-status-danger/10 px-2.5 py-1.5 text-xs text-status-danger disabled:opacity-60">
            Open full stop
          </button>
          <button type="button" disabled={opening} onClick={() => void handleOpen('PartialDerating')} className="rounded-sm border border-status-warning/30 bg-status-warning/10 px-2.5 py-1.5 text-xs text-status-warning disabled:opacity-60">
            Open partial derating
          </button>
        </div>
      )}
      {canExecute && !workOrder.assetId && <p className="mb-3 text-xs text-text-secondary">No asset linked — downtime cannot be recorded.</p>}
      <ErrorLine message={error} />

      {intervals.length === 0 ? (
        <p className="text-sm text-text-secondary">No downtime recorded for this execution cycle.</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {intervals.map((interval) => (
            <DowntimeIntervalRow key={interval.id} workOrderId={workOrder.id} interval={interval} canExecute={canExecute} onChanged={onChanged} />
          ))}
        </ul>
      )}
    </section>
  )
}

function DowntimeIntervalRow({
  workOrderId,
  interval,
  canExecute,
  onChanged,
}: {
  workOrderId: string
  interval: DowntimeInterval
  canExecute: boolean
  onChanged: () => void
}) {
  const [causeCategory, setCauseCategory] = useState<DowntimeCauseCategory>('Mechanical')
  const [causeMechanism, setCauseMechanism] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const isOpen = interval.endedAtUtc === null

  async function handleClose() {
    if (!causeMechanism.trim()) return
    setBusy(true)
    setError(null)
    try {
      await closeDowntimeInterval(workOrderId, interval.id, causeCategory, causeMechanism.trim())
      onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not close this interval.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <li className="rounded-sm border border-border bg-surface-raised p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className={`text-sm ${interval.classification === 'FullStop' ? 'text-status-danger' : 'text-status-warning'}`}>
          {interval.classification === 'FullStop' ? 'Full stop' : 'Partial derating'}
        </span>
        <span className="text-xs text-text-secondary">
          Started {new Date(interval.startedAtUtc).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}
        </span>
      </div>

      {isOpen ? (
        canExecute ? (
          <div className="mt-2 flex flex-col gap-2 sm:flex-row sm:items-end">
            <select value={causeCategory} onChange={(e) => setCauseCategory(e.target.value as DowntimeCauseCategory)} className={`${inputClass} sm:w-40`}>
              {downtimeCauseCategories.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
            <input type="text" value={causeMechanism} onChange={(e) => setCauseMechanism(e.target.value)} placeholder="Cause mechanism…" className={inputClass} />
            <button
              type="button"
              disabled={busy || !causeMechanism.trim()}
              onClick={() => void handleClose()}
              className="shrink-0 rounded-sm bg-accent px-2.5 py-1.5 text-xs font-medium text-accent-contrast disabled:opacity-60"
            >
              Close
            </button>
          </div>
        ) : (
          <p className="mt-2 text-xs text-text-secondary">Still open.</p>
        )
      ) : (
        <p className="mt-2 text-xs text-text-secondary">
          {interval.causeCategory} — {interval.causeMechanism}
        </p>
      )}
      <ErrorLine message={error} />
    </li>
  )
}

// ---------- Parts ----------

function PartsSection({
  workOrder,
  usages,
  canExecute,
  onChanged,
}: {
  workOrder: WorkOrder
  usages: PartUsage[]
  canExecute: boolean
  onChanged: () => void
}) {
  const [partName, setPartName] = useState('')
  const [partCode, setPartCode] = useState('')
  const [quantity, setQuantity] = useState('1')
  const [unitCost, setUnitCost] = useState('')
  const [currency, setCurrency] = useState('USD')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canSubmit = partName.trim().length > 0 && Number(quantity) > 0 && Number(unitCost) >= 0 && currency.trim().length > 0

  async function handleSubmit() {
    if (!canSubmit) return
    setSubmitting(true)
    setError(null)
    try {
      await postPartUsage(workOrder.id, {
        partName: partName.trim(),
        partCode: partCode.trim() || null,
        quantity: Number(quantity),
        unitCost: Number(unitCost),
        currency: currency.trim().toUpperCase(),
      })
      setPartName('')
      setPartCode('')
      setQuantity('1')
      setUnitCost('')
      onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not post this part.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <section>
      <SectionHeading>Parts used</SectionHeading>

      {canExecute && (
        <div className="mb-3 flex flex-col gap-2 rounded-sm border border-border bg-surface-raised p-3 sm:flex-row sm:flex-wrap sm:items-end">
          <input type="text" value={partName} onChange={(e) => setPartName(e.target.value)} placeholder="Part name" className={`${inputClass} sm:w-40`} />
          <input type="text" value={partCode} onChange={(e) => setPartCode(e.target.value)} placeholder="Code (optional)" className={`${inputClass} sm:w-32`} />
          <input type="number" min="0" value={quantity} onChange={(e) => setQuantity(e.target.value)} placeholder="Qty" className={`${inputClass} sm:w-20`} />
          <input type="number" min="0" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} placeholder="Unit cost" className={`${inputClass} sm:w-24`} />
          <input type="text" value={currency} onChange={(e) => setCurrency(e.target.value)} placeholder="Currency" className={`${inputClass} sm:w-16`} />
          <button type="button" disabled={submitting || !canSubmit} onClick={() => void handleSubmit()} className="shrink-0 rounded-sm bg-accent px-2.5 py-1.5 text-xs font-medium text-accent-contrast disabled:opacity-60">
            Add
          </button>
        </div>
      )}
      <ErrorLine message={error} />

      {usages.length === 0 ? (
        <p className="text-sm text-text-secondary">No parts posted for this execution cycle.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-max text-left text-sm">
            <thead>
              <tr className="border-b border-border text-xs text-text-secondary">
                <th className="py-1.5 pr-4 font-medium">Part</th>
                <th className="py-1.5 pr-4 font-medium">Qty</th>
                <th className="py-1.5 pr-4 font-medium">Unit cost</th>
                <th className="py-1.5 font-medium">Total</th>
              </tr>
            </thead>
            <tbody>
              {usages.map((usage) => (
                <tr key={usage.id} className="border-b border-border/60 last:border-0">
                  <td className="py-1.5 pr-4 text-text-primary">
                    {usage.partName}
                    {usage.partCode && <span className="ml-1 text-xs text-text-secondary">({usage.partCode})</span>}
                  </td>
                  <td className="py-1.5 pr-4 tabular-nums text-text-primary">{usage.quantity}</td>
                  <td className="py-1.5 pr-4 tabular-nums text-text-primary">
                    {usage.unitCost !== null ? `${usage.unitCost.toFixed(2)} ${usage.currency}` : '—'}
                  </td>
                  <td className="py-1.5 tabular-nums text-text-primary">
                    {usage.unitCost !== null ? `${(usage.unitCost * usage.quantity).toFixed(2)} ${usage.currency}` : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

// ---------- Evidence photos (general, not tied to a checklist item) ----------

function EvidenceSection({
  workOrder,
  attachments,
  canExecute,
  onChanged,
}: {
  workOrder: WorkOrder
  attachments: Attachment[]
  canExecute: boolean
  onChanged: () => void
}) {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleFileSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      await uploadEvidencePhoto(workOrder.id, file)
      onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not upload this photo.')
    } finally {
      setBusy(false)
    }
  }

  async function handleUnlink(attachmentId: string) {
    setBusy(true)
    setError(null)
    try {
      await unlinkAttachment(attachmentId)
      onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not remove this photo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section>
      <div className="mb-3 flex items-center justify-between">
        <SectionHeading>Evidence photos</SectionHeading>
        {canExecute && (
          <>
            <input ref={fileInputRef} type="file" accept="image/jpeg,image/png,image/webp" capture="environment" className="hidden" onChange={(e) => void handleFileSelected(e)} />
            <button
              type="button"
              disabled={busy}
              onClick={() => fileInputRef.current?.click()}
              className="inline-flex items-center gap-1.5 rounded-sm border border-border px-2 py-1 text-xs text-text-primary hover:border-border-strong disabled:opacity-60"
            >
              <Camera className="h-3.5 w-3.5" strokeWidth={1.75} />
              {busy ? 'Uploading…' : 'Add photo'}
            </button>
          </>
        )}
      </div>
      <ErrorLine message={error} />

      {attachments.length === 0 ? (
        <p className="text-sm text-text-secondary">No evidence photos yet.</p>
      ) : (
        <div className="flex flex-wrap gap-3">
          {attachments.map((attachment) => (
            <div key={attachment.id} className="group relative h-24 w-24 shrink-0 overflow-hidden rounded-sm border border-border">
              <img src={attachmentDownloadUrl(attachment.id)} alt="Evidence" className="h-full w-full object-cover" />
              {canExecute && (
                <button
                  type="button"
                  aria-label="Remove photo"
                  disabled={busy}
                  onClick={() => void handleUnlink(attachment.id)}
                  className="absolute top-1 right-1 rounded-sm bg-black/60 p-1 text-white opacity-0 transition-opacity group-hover:opacity-100 focus:opacity-100 disabled:opacity-60"
                >
                  <X className="h-3 w-3" strokeWidth={2} />
                </button>
              )}
            </div>
          ))}
        </div>
      )}
    </section>
  )
}
