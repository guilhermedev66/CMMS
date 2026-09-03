import { useMemo, useState, type ReactNode } from 'react'
import { listAssets, type Asset } from '../api/assets'
import { ApiError } from '../api/client'
import { createMaintenancePlan, type RecurrenceType } from '../api/maintenancePlans'
import { useAuth } from '../auth/useAuth'
import { useAsync } from '../hooks/useAsync'

interface NewMaintenancePlanDialogProps {
  onClose: () => void
  onCreated: () => void
}

/** Plan definition form — docs/01: calendar-based (Fixed + Floating) recurrence, day-interval, optional generation lead time. */
export function NewMaintenancePlanDialog({ onClose, onCreated }: NewMaintenancePlanDialogProps) {
  const { user } = useAuth()
  const sites = (user?.siteMemberships ?? []).filter((m) => m.role === 'Planner')
  const [siteId, setSiteId] = useState(sites[0]?.siteId ?? '')

  const { data } = useAsync(() => listAssets(), [])
  const assetsAtSite = useMemo(() => (data ?? []).filter((a: Asset) => a.siteId === siteId), [data, siteId])

  const [assetId, setAssetId] = useState('')
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [recurrenceType, setRecurrenceType] = useState<RecurrenceType>('Fixed')
  const [intervalDays, setIntervalDays] = useState(30)
  const [leadTimeDays, setLeadTimeDays] = useState(0)
  const [firstDueDate, setFirstDueDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canSubmit = title.trim().length > 0 && siteId && assetId && intervalDays > 0 && !submitting

  async function handleSubmit() {
    if (!canSubmit) return
    setSubmitting(true)
    setError(null)
    try {
      await createMaintenancePlan({
        siteId,
        assetId,
        title: title.trim(),
        description: description.trim() || null,
        recurrenceType,
        intervalDays,
        generationLeadTimeDays: leadTimeDays,
        firstDueAtUtc: new Date(`${firstDueDate}T00:00:00Z`).toISOString(),
      })
      onCreated()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create this plan.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4">
      <div role="dialog" aria-modal="true" aria-label="New Maintenance Plan" className="w-full max-w-md rounded-md border border-border bg-surface-raised p-4">
        <h2 className="mb-3 text-sm font-semibold text-text-primary">New Maintenance Plan</h2>

        {sites.length === 0 ? (
          <p className="text-sm text-text-secondary">You need Planner (or Admin) authority at a site to define a plan.</p>
        ) : (
          <div className="flex flex-col gap-3">
            {sites.length > 1 && (
              <Field label="Site">
                <select value={siteId} onChange={(e) => setSiteId(e.target.value)} className={inputClass}>
                  {sites.map((s) => (
                    <option key={s.siteId} value={s.siteId}>
                      {s.siteName}
                    </option>
                  ))}
                </select>
              </Field>
            )}

            <Field label="Asset">
              <select value={assetId} onChange={(e) => setAssetId(e.target.value)} className={inputClass}>
                <option value="">Select an asset…</option>
                {assetsAtSite.map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.tag} — {a.name}
                  </option>
                ))}
              </select>
            </Field>

            <Field label="Title">
              <input type="text" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Quarterly lubrication" className={inputClass} />
            </Field>

            <Field label="Description">
              <textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                rows={2}
                placeholder="Scope of the recurring work…"
                className={`${inputClass} resize-none`}
              />
            </Field>

            <div className="flex gap-2">
              <label className="flex items-center gap-1.5 text-sm text-text-primary">
                <input type="radio" checked={recurrenceType === 'Fixed'} onChange={() => setRecurrenceType('Fixed')} />
                Fixed (calendar-anchored)
              </label>
              <label className="flex items-center gap-1.5 text-sm text-text-primary">
                <input type="radio" checked={recurrenceType === 'Floating'} onChange={() => setRecurrenceType('Floating')} />
                Floating (from last completion)
              </label>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <Field label="Interval (days)">
                <input
                  type="number"
                  min={1}
                  value={intervalDays}
                  onChange={(e) => setIntervalDays(Number(e.target.value))}
                  className={inputClass}
                />
              </Field>
              <Field label="Lead time (days)">
                <input
                  type="number"
                  min={0}
                  value={leadTimeDays}
                  onChange={(e) => setLeadTimeDays(Number(e.target.value))}
                  className={inputClass}
                />
              </Field>
            </div>

            <Field label="First due date">
              <input type="date" value={firstDueDate} onChange={(e) => setFirstDueDate(e.target.value)} className={inputClass} />
            </Field>

            {error && <p className="text-xs text-status-danger">{error}</p>}
          </div>
        )}

        <div className="mt-4 flex justify-end gap-2">
          <button type="button" onClick={onClose} className="rounded-sm border border-border px-3 py-1.5 text-sm text-text-primary hover:border-border-strong">
            Cancel
          </button>
          {sites.length > 0 && (
            <button
              type="button"
              disabled={!canSubmit}
              onClick={() => void handleSubmit()}
              className="rounded-sm bg-accent px-3 py-1.5 text-sm font-medium text-accent-contrast disabled:opacity-60"
            >
              Create
            </button>
          )}
        </div>
      </div>
    </div>
  )
}

const inputClass =
  'w-full rounded-sm border border-border bg-surface px-2.5 py-1.5 text-sm text-text-primary placeholder:text-text-secondary focus:border-border-strong focus:ring-1 focus:ring-accent focus:outline-none'

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="flex flex-col gap-1 text-xs text-text-secondary">
      {label}
      {children}
    </label>
  )
}
