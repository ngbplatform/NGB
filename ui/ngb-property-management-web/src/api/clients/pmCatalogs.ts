import { httpPost } from 'ngb-ui-framework'

export type PmPropertyBulkCreateUnitsRequest = {
  buildingId: string
  fromInclusive: number
  toInclusive: number
  step?: number
  unitNoFormat?: string
  floorSize?: number | null
}

export type PmPropertyBulkCreateUnitsResponse = {
  buildingId: string
  requestedCount: number
  createdCount: number
  duplicateCount: number
  createdIds: string[]
  createdUnitNosSample: string[]
  duplicateUnitNosSample: string[]
  isDryRun?: boolean
  wouldCreateCount?: number
  previewUnitNosSample?: string[]
}

export async function bulkCreatePmPropertyUnits(
  req: PmPropertyBulkCreateUnitsRequest,
  opts?: { dryRun?: boolean },
): Promise<PmPropertyBulkCreateUnitsResponse> {
  const query = opts?.dryRun ? '?dryRun=true' : ''
  return await httpPost<PmPropertyBulkCreateUnitsResponse>(`/api/catalogs/pm.property/bulk-create-units${query}`, req)
}

export async function dryRunPmPropertyUnits(req: PmPropertyBulkCreateUnitsRequest): Promise<PmPropertyBulkCreateUnitsResponse> {
  return await bulkCreatePmPropertyUnits(req, { dryRun: true })
}
