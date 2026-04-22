import { httpGet, httpPost } from './http'
import type { ByIdsRequestDto, LookupItemDto } from './contracts'
import type { QueryParams } from './types'

export async function lookupCatalog(catalogType: string, q: string | null, limit = 20): Promise<LookupItemDto[]> {
  const query: QueryParams = { limit }
  if (q && q.trim().length > 0) query.q = q
  return await httpGet<LookupItemDto[]>(`/api/catalogs/${encodeURIComponent(catalogType)}/lookup`, query)
}

export async function getCatalogLookupByIds(catalogType: string, ids: string[]): Promise<LookupItemDto[]> {
  const body: ByIdsRequestDto = { ids }
  return await httpPost<LookupItemDto[]>(`/api/catalogs/${encodeURIComponent(catalogType)}/by-ids`, body)
}
