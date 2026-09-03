/**
 * Local mock data for the Asset List / Detail pages (M1). Field shapes
 * mirror the real backend entities exactly (see
 * src/Modules/Assets/Domain/Asset.cs and Location.cs) so this is a
 * drop-in swap for the real API once the Assets endpoints land — only the
 * fetching (this file's exports) changes, not the components that consume it.
 */

export type AssetCriticality = 'A' | 'B' | 'C'
export type AssetStatus = 'InService' | 'OutOfService' | 'Retired'

export interface MockLocation {
  id: string
  code: string
  name: string
  parentLocationId: string | null
}

export interface MockAsset {
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
}

const SITE_ID = 'site-plant-network'

export const mockLocations: MockLocation[] = [
  { id: 'loc-plant-a', code: 'PLANT-A', name: 'Plant A', parentLocationId: null },
  { id: 'loc-plant-a-b1', code: 'PLANT-A-B1', name: 'Building 1', parentLocationId: 'loc-plant-a' },
  { id: 'loc-plant-a-b1-l1', code: 'PLANT-A-B1-L1', name: 'Line 1', parentLocationId: 'loc-plant-a-b1' },
  { id: 'loc-plant-a-b1-l2', code: 'PLANT-A-B1-L2', name: 'Line 2', parentLocationId: 'loc-plant-a-b1' },
  { id: 'loc-plant-a-b2', code: 'PLANT-A-B2', name: 'Building 2', parentLocationId: 'loc-plant-a' },
  { id: 'loc-plant-a-b2-wh', code: 'PLANT-A-B2-WH', name: 'Warehouse', parentLocationId: 'loc-plant-a-b2' },
  { id: 'loc-plant-b', code: 'PLANT-B', name: 'Plant B', parentLocationId: null },
  { id: 'loc-plant-b-util', code: 'PLANT-B-UTIL', name: 'Utilities', parentLocationId: 'loc-plant-b' },
]

export const mockAssets: MockAsset[] = [
  {
    id: 'asset-001',
    siteId: SITE_ID,
    tag: 'PMP-014',
    name: 'Feed Pump 14',
    category: 'Pump',
    manufacturer: 'Grundfos',
    model: 'CR64-2',
    serialNumber: 'SN-88213-A',
    criticality: 'A',
    status: 'InService',
    currentLocationId: 'loc-plant-a-b1-l1',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e01',
    createdAtUtc: '2025-02-11T09:30:00Z',
  },
  {
    id: 'asset-002',
    siteId: SITE_ID,
    tag: 'CNV-002',
    name: 'Main Conveyor 2',
    category: 'Conveyor',
    manufacturer: 'Siemens',
    model: 'SIMOTICS-CV',
    serialNumber: 'SN-77102-B',
    criticality: 'A',
    status: 'InService',
    currentLocationId: 'loc-plant-a-b1-l1',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e02',
    createdAtUtc: '2025-02-14T11:05:00Z',
  },
  {
    id: 'asset-003',
    siteId: SITE_ID,
    tag: 'HVAC-101',
    name: 'Rooftop HVAC 101',
    category: 'HVAC Unit',
    manufacturer: 'Carrier',
    model: '48TC-A08',
    serialNumber: 'SN-55210-C',
    criticality: 'C',
    status: 'InService',
    currentLocationId: 'loc-plant-a-b2',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e03',
    createdAtUtc: '2025-01-30T08:00:00Z',
  },
  {
    id: 'asset-004',
    siteId: SITE_ID,
    tag: 'GEN-003',
    name: 'Backup Generator 3',
    category: 'Generator',
    manufacturer: 'Caterpillar',
    model: 'C15-GenSet',
    serialNumber: 'SN-99321-D',
    criticality: 'A',
    status: 'OutOfService',
    currentLocationId: 'loc-plant-b-util',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e04',
    createdAtUtc: '2024-11-02T14:20:00Z',
  },
  {
    id: 'asset-005',
    siteId: SITE_ID,
    tag: 'CMP-005',
    name: 'Air Compressor 5',
    category: 'Compressor',
    manufacturer: 'Atlas Copco',
    model: 'GA30VSD',
    serialNumber: 'SN-44120-E',
    criticality: 'B',
    status: 'InService',
    currentLocationId: 'loc-plant-a-b1-l2',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e05',
    createdAtUtc: '2025-03-19T10:45:00Z',
  },
  {
    id: 'asset-006',
    siteId: SITE_ID,
    tag: 'CNC-007',
    name: 'CNC Mill 7',
    category: 'CNC Machine',
    manufacturer: 'Haas',
    model: 'VF-2SS',
    serialNumber: 'SN-33140-F',
    criticality: 'B',
    status: 'InService',
    currentLocationId: 'loc-plant-a-b1-l2',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e06',
    createdAtUtc: '2025-04-02T13:15:00Z',
  },
  {
    id: 'asset-007',
    siteId: SITE_ID,
    tag: 'FRK-012',
    name: 'Forklift 12',
    category: 'Forklift',
    manufacturer: 'Toyota',
    model: '8FBE20U',
    serialNumber: 'SN-22190-G',
    criticality: 'C',
    status: 'InService',
    currentLocationId: 'loc-plant-a-b2-wh',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e07',
    createdAtUtc: '2025-05-07T07:50:00Z',
  },
  {
    id: 'asset-008',
    siteId: SITE_ID,
    tag: 'RBT-003',
    name: 'Welding Robot 3',
    category: 'Robot Arm',
    manufacturer: 'Fanuc',
    model: 'ARC Mate 100iD',
    serialNumber: 'SN-11250-H',
    criticality: 'A',
    status: 'InService',
    currentLocationId: 'loc-plant-a-b1-l1',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e08',
    createdAtUtc: '2025-01-22T16:00:00Z',
  },
  {
    id: 'asset-009',
    siteId: SITE_ID,
    tag: 'MTR-021',
    name: 'Drive Motor 21',
    category: 'Motor',
    manufacturer: 'ABB',
    model: 'M3BP 200',
    serialNumber: 'SN-66230-I',
    criticality: 'B',
    status: 'Retired',
    currentLocationId: 'loc-plant-a-b1-l2',
    parentAssetId: 'asset-005',
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e09',
    createdAtUtc: '2024-08-15T09:10:00Z',
  },
  {
    id: 'asset-010',
    siteId: SITE_ID,
    tag: 'PRS-009',
    name: 'Hydraulic Press 9',
    category: 'Press',
    manufacturer: 'Schuler',
    model: 'MSP-400',
    serialNumber: 'SN-77340-J',
    criticality: 'A',
    status: 'InService',
    currentLocationId: 'loc-plant-a-b1-l2',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e0a',
    createdAtUtc: '2024-12-01T12:30:00Z',
  },
  {
    id: 'asset-011',
    siteId: SITE_ID,
    tag: 'CHL-006',
    name: 'Process Chiller 6',
    category: 'Chiller',
    manufacturer: 'Trane',
    model: 'CVHF-500',
    serialNumber: 'SN-90233-K',
    criticality: 'B',
    status: 'InService',
    currentLocationId: 'loc-plant-a-b2',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e0b',
    createdAtUtc: '2025-03-28T15:40:00Z',
  },
  {
    id: 'asset-012',
    siteId: SITE_ID,
    tag: 'FRK-013',
    name: 'Forklift 13',
    category: 'Forklift',
    manufacturer: 'Toyota',
    model: '8FBE20U',
    serialNumber: 'SN-22191-L',
    criticality: 'C',
    status: 'OutOfService',
    currentLocationId: 'loc-plant-a-b2-wh',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e0c',
    createdAtUtc: '2025-05-07T07:55:00Z',
  },
  {
    id: 'asset-013',
    siteId: SITE_ID,
    tag: 'BLR-001',
    name: 'Steam Boiler 1',
    category: 'Boiler',
    manufacturer: 'Cleaver-Brooks',
    model: 'CB-700',
    serialNumber: 'SN-10450-M',
    criticality: 'A',
    status: 'InService',
    currentLocationId: 'loc-plant-b-util',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e0d',
    createdAtUtc: '2024-09-09T06:25:00Z',
  },
  {
    id: 'asset-014',
    siteId: SITE_ID,
    tag: 'CNV-003',
    name: 'Packing Conveyor 3',
    category: 'Conveyor',
    manufacturer: 'Siemens',
    model: 'SIMOTICS-CV',
    serialNumber: null,
    criticality: 'C',
    status: 'InService',
    currentLocationId: 'loc-plant-a-b2-wh',
    parentAssetId: null,
    qrLocator: '01991e2a-7c31-7f3a-8b21-1a2b3c4d5e0e',
    createdAtUtc: '2025-06-11T10:00:00Z',
  },
]

const locationsById = new Map(mockLocations.map((location) => [location.id, location]))

/** Full breadcrumb path for a location, root-first (e.g. "Plant A / Building 1 / Line 1"). */
export function getLocationPath(locationId: string | null): string {
  if (!locationId) return '—'
  const parts: string[] = []
  let current = locationsById.get(locationId)
  while (current) {
    parts.unshift(current.name)
    current = current.parentLocationId ? locationsById.get(current.parentLocationId) : undefined
  }
  return parts.length > 0 ? parts.join(' / ') : '—'
}

export function getAssetById(id: string): MockAsset | undefined {
  return mockAssets.find((asset) => asset.id === id)
}

export const assetCategories = Array.from(new Set(mockAssets.map((asset) => asset.category))).sort()
