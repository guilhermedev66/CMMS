import { useMemo, useState, type ReactNode } from 'react'
import { listAssets, listLocations, type Asset, type Location } from '../api/assets'
import { ApiError } from '../api/client'
import { priorityLabels, type Priority } from '../api/shared'
import { createWorkOrder } from '../api/workOrders'
import { useAuth } from '../auth/useAuth'
import { useAsync } from '../hooks/useAsync'

interface NewWorkOrderDialogProps {
  onClose: () => void
  onCreated: () => void
}

const priorityOptions: Priority[] = ['P1', 'P2', 'P3', 'P4']

/** Direct Work Order creation (workorders.create) — Planner/Admin only, per docs/02's permission table. */
export function NewWorkOrderDialog({ onClose, onCreated }: NewWorkOrderDialogProps) {
  const { user } = useAuth()
  const sites = (user?.siteMemberships ?? []).filter((m) => m.role === 'Planner')
  const [siteId, setSiteId] = useState(sites[0]?.siteId ?? '')

  const { data } = useAsync(() => Promise.all([listAssets(), listLocations()]).then(([assets, locations]) => ({ assets, locations })), [])
  const assetsAtSite = useMemo(() => (data?.assets ?? []).filter((a: Asset) => a.siteId === siteId), [data, siteId])
  const locationsAtSite = useMemo(() => (data?.locations ?? []).filter((l: Location) => l.siteId === siteId), [data, siteId])

  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [assetId, setAssetId] = useState('')
  const [locationId, setLocationId] = useState('')
  const [priority, setPriority] = useState<Priority>('P3')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canSubmit = title.trim().length > 0 && siteId && !submitting

  async function handleSubmit() {
    if (!canSubmit) return
    setSubmitting(true)
    setError(null)
    try {
      await createWorkOrder({
        siteId,
        title: title.trim(),
        description: description.trim() || null,
        assetId: assetId || null,
        locationId: locationId || null,
        priority,
      })
      onCreated()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create this Work Order.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4">
      <div role="dialog" aria-modal="true" aria-label="New Work Order" className="w-full max-w-md rounded-md border border-border bg-surface-raised p-4">
        <h2 className="mb-3 text-sm font-semibold text-text-primary">New Work Order</h2>

        {sites.length === 0 ? (
          <p className="text-sm text-text-secondary">
            You need Planner (or Admin) authority at a site to create a Work Order directly — otherwise, convert a Request instead.
          </p>
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

            <Field label="Title">
              <input type="text" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Brief summary…" className={inputClass} />
            </Field>

            <Field label="Description">
              <textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                rows={3}
                placeholder="Scope of work…"
                className={`${inputClass} resize-none`}
              />
            </Field>

            <Field label="Asset (optional)">
              <select value={assetId} onChange={(e) => setAssetId(e.target.value)} className={inputClass}>
                <option value="">None</option>
                {assetsAtSite.map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.tag} — {a.name}
                  </option>
                ))}
              </select>
            </Field>

            <Field label="Location (optional)">
              <select value={locationId} onChange={(e) => setLocationId(e.target.value)} className={inputClass}>
                <option value="">None</option>
                {locationsAtSite.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.name}
                  </option>
                ))}
              </select>
            </Field>

            <Field label="Priority">
              <select value={priority} onChange={(e) => setPriority(e.target.value as Priority)} className={inputClass}>
                {priorityOptions.map((p) => (
                  <option key={p} value={p}>
                    {priorityLabels[p]}
                  </option>
                ))}
              </select>
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
