import { RotateCw, Search, TriangleAlert } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { CriticalityBadge } from '../components/CriticalityBadge'
import { StatusBadge } from '../components/StatusBadge'
import { useAsync } from '../hooks/useAsync'
import { ApiError } from '../api/client'
import { getLocationPath, listAssets, listLocations, type AssetCriticality, type AssetStatus } from '../api/assets'

const statusOptions: AssetStatus[] = ['InService', 'OutOfService', 'Retired']
const criticalityOptions: AssetCriticality[] = ['A', 'B', 'C']

export function AssetsListPage() {
  const [query, setQuery] = useState('')
  const [category, setCategory] = useState('all')
  const [status, setStatus] = useState<'all' | AssetStatus>('all')
  const [criticality, setCriticality] = useState<'all' | AssetCriticality>('all')

  const { status: loadStatus, data, error, reload } = useAsync(
    () => Promise.all([listAssets(), listLocations()]).then(([assets, locations]) => ({ assets, locations })),
    [],
  )

  const assetCategories = useMemo(
    () => Array.from(new Set((data?.assets ?? []).map((asset) => asset.category))).sort(),
    [data],
  )

  const filtered = useMemo(() => {
    if (!data) return []
    const q = query.trim().toLowerCase()
    return data.assets.filter((asset) => {
      if (category !== 'all' && asset.category !== category) return false
      if (status !== 'all' && asset.status !== status) return false
      if (criticality !== 'all' && asset.criticality !== criticality) return false
      if (q && !asset.tag.toLowerCase().includes(q) && !asset.name.toLowerCase().includes(q)) return false
      return true
    })
  }, [data, query, category, status, criticality])

  return (
    <div className="flex h-full flex-col">
      <div className="flex flex-col gap-1 border-b border-border px-6 py-4">
        <div className="flex items-baseline gap-2">
          <h1 className="text-lg font-semibold text-text-primary">Assets</h1>
          {data && (
            <span className="font-mono text-xs text-text-secondary tabular-nums">
              {filtered.length} of {data.assets.length}
            </span>
          )}
        </div>
        <p className="text-sm text-text-secondary">Registry, location, and criticality across every site.</p>
      </div>

      <div className="flex flex-wrap items-center gap-2 border-b border-border px-6 py-3">
        <div className="relative">
          <Search
            className="pointer-events-none absolute top-1/2 left-2.5 h-4 w-4 -translate-y-1/2 text-text-secondary"
            strokeWidth={1.75}
          />
          <input
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Filter by tag or name…"
            disabled={loadStatus !== 'success'}
            className="w-56 rounded-sm border border-border bg-surface-raised py-1.5 pr-3 pl-8 text-sm text-text-primary placeholder:text-text-secondary focus:border-border-strong focus:ring-1 focus:ring-accent focus:outline-none disabled:opacity-60"
          />
        </div>

        <select
          value={category}
          onChange={(event) => setCategory(event.target.value)}
          disabled={loadStatus !== 'success'}
          className="rounded-sm border border-border bg-surface-raised px-2.5 py-1.5 text-sm text-text-primary focus:border-border-strong focus:ring-1 focus:ring-accent focus:outline-none disabled:opacity-60"
        >
          <option value="all">All categories</option>
          {assetCategories.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>

        <select
          value={status}
          onChange={(event) => setStatus(event.target.value as 'all' | AssetStatus)}
          disabled={loadStatus !== 'success'}
          className="rounded-sm border border-border bg-surface-raised px-2.5 py-1.5 text-sm text-text-primary focus:border-border-strong focus:ring-1 focus:ring-accent focus:outline-none disabled:opacity-60"
        >
          <option value="all">All statuses</option>
          {statusOptions.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>

        <select
          value={criticality}
          onChange={(event) => setCriticality(event.target.value as 'all' | AssetCriticality)}
          disabled={loadStatus !== 'success'}
          className="rounded-sm border border-border bg-surface-raised px-2.5 py-1.5 text-sm text-text-primary focus:border-border-strong focus:ring-1 focus:ring-accent focus:outline-none disabled:opacity-60"
        >
          <option value="all">All criticality</option>
          {criticalityOptions.map((c) => (
            <option key={c} value={c}>
              Criticality {c}
            </option>
          ))}
        </select>
      </div>

      <div className="flex-1 overflow-auto">
        {loadStatus === 'loading' && (
          <p className="px-6 py-10 text-center text-sm text-text-secondary">Loading assets…</p>
        )}

        {loadStatus === 'error' && (
          <div className="flex flex-col items-center gap-3 px-6 py-16 text-center">
            <TriangleAlert className="h-6 w-6 text-status-danger" strokeWidth={1.5} />
            <p className="text-sm text-text-primary">
              {error instanceof ApiError ? error.message : 'Could not load assets.'}
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

        {loadStatus === 'success' && data.assets.length === 0 && (
          <p className="px-6 py-10 text-center text-sm text-text-secondary">
            No assets yet. Assets created via the API will show up here.
          </p>
        )}

        {loadStatus === 'success' && data.assets.length > 0 && filtered.length === 0 && (
          <p className="px-6 py-10 text-center text-sm text-text-secondary">No assets match your filters.</p>
        )}

        {loadStatus === 'success' && filtered.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px] border-collapse text-sm">
              <thead className="sticky top-0 border-b border-border bg-surface-raised text-left text-xs text-text-secondary">
                <tr>
                  <th className="px-6 py-2 font-medium">Tag</th>
                  <th className="px-3 py-2 font-medium">Name</th>
                  <th className="px-3 py-2 font-medium">Category</th>
                  <th className="px-3 py-2 font-medium">Location</th>
                  <th className="px-3 py-2 font-medium">Criticality</th>
                  <th className="px-3 py-2 font-medium">Status</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((asset) => (
                  <tr key={asset.id} className="border-b border-border last:border-b-0 hover:bg-surface">
                    <td className="px-6 py-2">
                      <Link
                        to={`/assets/${asset.id}`}
                        className="font-mono text-sm tabular-nums text-accent hover:underline"
                      >
                        {asset.tag}
                      </Link>
                    </td>
                    <td className="px-3 py-2 text-text-primary">{asset.name}</td>
                    <td className="px-3 py-2 text-text-secondary">{asset.category}</td>
                    <td className="px-3 py-2 text-text-secondary">
                      {getLocationPath(data.locations, asset.currentLocationId)}
                    </td>
                    <td className="px-3 py-2">
                      <CriticalityBadge criticality={asset.criticality} />
                    </td>
                    <td className="px-3 py-2">
                      <StatusBadge status={asset.status} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
