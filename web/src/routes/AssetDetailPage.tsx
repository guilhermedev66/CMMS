import { ArrowLeft, CalendarDays, ClipboardList, FileText, QrCode, RotateCw, TriangleAlert } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { getAsset, getLocationPath, listAssets, listLocations } from '../api/assets'
import { ApiError } from '../api/client'
import { CriticalityBadge } from '../components/CriticalityBadge'
import { EmptyState } from '../components/EmptyState'
import { StatusBadge } from '../components/StatusBadge'
import { useAsync } from '../hooks/useAsync'

const tabs = ['Overview', 'Work Order History', 'Maintenance Schedule', 'Documents', 'QR Info'] as const
type Tab = (typeof tabs)[number]

export function AssetDetailPage() {
  const { assetId } = useParams<{ assetId: string }>()
  const [activeTab, setActiveTab] = useState<Tab>('Overview')

  const { status, data, error, reload } = useAsync(
    () =>
      Promise.all([getAsset(assetId!), listLocations(), listAssets()]).then(
        ([asset, locations, allAssets]) => ({ asset, locations, allAssets }),
      ),
    [assetId],
  )

  if (status === 'loading') {
    return <p className="px-6 py-16 text-center text-sm text-text-secondary">Loading asset…</p>
  }

  if (status === 'error') {
    const notFound = error instanceof ApiError && error.status === 404

    if (notFound) {
      return (
        <div className="flex h-full flex-col items-center justify-center gap-3 px-6 py-16 text-center">
          <h1 className="text-base font-semibold text-text-primary">Asset not found</h1>
          <p className="max-w-sm text-sm text-text-secondary">
            No asset matches “{assetId}”, or you don't have access to it.
          </p>
          <Link to="/assets" className="text-sm text-accent hover:underline">
            Back to Assets
          </Link>
        </div>
      )
    }

    return (
      <div className="flex h-full flex-col items-center justify-center gap-3 px-6 py-16 text-center">
        <TriangleAlert className="h-6 w-6 text-status-danger" strokeWidth={1.5} />
        <p className="text-sm text-text-primary">
          {error instanceof ApiError ? error.message : 'Could not load this asset.'}
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
    )
  }

  const { asset, locations, allAssets } = data
  const parentAsset = asset.parentAssetId ? allAssets.find((a) => a.id === asset.parentAssetId) : undefined

  return (
    <div className="flex h-full flex-col">
      <div className="border-b border-border px-6 py-4">
        <Link
          to="/assets"
          className="mb-3 inline-flex items-center gap-1 text-sm text-text-secondary hover:text-text-primary"
        >
          <ArrowLeft className="h-3.5 w-3.5" strokeWidth={1.75} />
          Assets
        </Link>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="font-mono text-lg font-semibold tabular-nums text-text-primary">{asset.tag}</h1>
              <span className="text-lg text-text-secondary">{asset.name}</span>
            </div>
            <p className="mt-1 text-sm text-text-secondary">{getLocationPath(locations, asset.currentLocationId)}</p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            <CriticalityBadge criticality={asset.criticality} />
            <StatusBadge status={asset.status} />
          </div>
        </div>
      </div>

      <div className="overflow-x-auto border-b border-border px-6">
        <div className="flex min-w-max gap-4" role="tablist" aria-label="Asset detail sections">
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

      <div className="flex-1 overflow-y-auto">
        {activeTab === 'Overview' && (
          <dl className="grid grid-cols-1 gap-x-8 gap-y-4 px-6 py-6 sm:grid-cols-2">
            <Field label="Tag" value={<span className="font-mono tabular-nums">{asset.tag}</span>} />
            <Field label="Name" value={asset.name} />
            <Field label="Category" value={asset.category} />
            <Field label="Location" value={getLocationPath(locations, asset.currentLocationId)} />
            <Field label="Manufacturer" value={asset.manufacturer ?? '—'} />
            <Field label="Model" value={asset.model ?? '—'} />
            <Field
              label="Serial Number"
              value={<span className="font-mono tabular-nums">{asset.serialNumber ?? '—'}</span>}
            />
            <Field
              label="Parent Asset"
              value={
                parentAsset ? (
                  <Link to={`/assets/${parentAsset.id}`} className="font-mono text-accent tabular-nums hover:underline">
                    {parentAsset.tag}
                  </Link>
                ) : (
                  '—'
                )
              }
            />
            <Field label="Status" value={<StatusBadge status={asset.status} />} />
            <Field label="Criticality" value={<CriticalityBadge criticality={asset.criticality} />} />
            <Field
              label="Created"
              value={new Date(asset.createdAtUtc).toLocaleDateString(undefined, {
                year: 'numeric',
                month: 'short',
                day: 'numeric',
              })}
            />
          </dl>
        )}

        {activeTab === 'Work Order History' && (
          <EmptyState
            compact
            headingLevel="h2"
            icon={ClipboardList}
            label="Work Order History"
            description="Every Work Order raised against this asset, newest first."
            milestone="Wired up in M2 — Requests & Work Orders."
          />
        )}

        {activeTab === 'Maintenance Schedule' && (
          <EmptyState
            compact
            headingLevel="h2"
            icon={CalendarDays}
            label="Maintenance Schedule"
            description="Linked preventive maintenance plans and their next due dates."
            milestone="Wired up in M3 — Preventive Maintenance."
          />
        )}

        {activeTab === 'Documents' && (
          <EmptyState
            compact
            headingLevel="h2"
            icon={FileText}
            label="Documents"
            description="Manuals, certificates, and other reference files for this asset."
            milestone="Attachments (evidence photos) land in M4; manuals/PDFs are a later add per docs/03 ADR-11."
          />
        )}

        {activeTab === 'QR Info' && (
          <div className="flex flex-col items-center gap-3 py-10 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-md border border-border bg-surface-raised text-text-secondary">
              <QrCode className="h-6 w-6" strokeWidth={1.5} />
            </div>
            <h2 className="text-base font-semibold text-text-primary">QR Info</h2>
            <p className="max-w-sm text-sm text-text-secondary">
              A physical tag encodes this locator as <span className="font-mono">/scan/{'{locator}'}</span> — scanning
              it never grants more access than the technician's own role/site membership already allows
              (docs/02-security-and-invariants.md § "QR strategy").
            </p>
            <p className="font-mono text-xs text-text-secondary tabular-nums">Locator: {asset.qrLocator}</p>
            <Link to={`/scan/${asset.qrLocator}`} className="text-sm text-accent hover:underline">
              Simulate a scan →
            </Link>
          </div>
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
