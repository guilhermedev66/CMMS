import { apiClient } from './client'

export type AssetCriticality = 'A' | 'B' | 'C'
export type AssetStatus = 'InService' | 'OutOfService' | 'Retired'

/**
 * Matches AssetResponse in src/Cmms.Api/AssetsEndpoints.cs. Note: a
 * Requester-role caller gets AssetLimitedResponse instead (id/tag/name/
 * currentLocationId only) — these pages aren't exercised by that role yet,
 * so that narrower projection isn't modeled here. Revisit when Requester UI
 * is actually built (M2).
 */
export interface Asset {
  id: string
  siteId: string
  tag: string
  name: string
  category: string
  manufacturer: string | null
  model: string | null
  serialNumber: string | null
  criticality: AssetCriticality
  status: AssetStatus
  currentLocationId: string | null
  parentAssetId: string | null
  qrLocator: string
  createdAtUtc: string
  rowVersion: number
}

/** Matches LocationResponse in src/Cmms.Api/AssetsEndpoints.cs. */
export interface Location {
  id: string
  siteId: string
  code: string
  name: string
  parentLocationId: string | null
  createdAtUtc: string
  rowVersion: number
}

export function listAssets(): Promise<Asset[]> {
  return apiClient.get<Asset[]>('/assets')
}

export function getAsset(id: string): Promise<Asset> {
  return apiClient.get<Asset>(`/assets/${id}`)
}

export function listLocations(): Promise<Location[]> {
  return apiClient.get<Location[]>('/locations')
}

/** Full breadcrumb path for a location, root-first (e.g. "Plant A / Building 1 / Line 1"). */
export function getLocationPath(locations: Location[], locationId: string | null): string {
  if (!locationId) return '—'
  const byId = new Map(locations.map((location) => [location.id, location]))
  const parts: string[] = []
  let current = byId.get(locationId)
  while (current) {
    parts.unshift(current.name)
    current = current.parentLocationId ? byId.get(current.parentLocationId) : undefined
  }
  return parts.length > 0 ? parts.join(' / ') : '—'
}
