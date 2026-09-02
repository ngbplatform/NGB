import { httpGet, httpPost, type HttpRequestOptions } from './http'
import type { ByIdsRequestDto, LookupItemDto } from './contracts'
import type { QueryParams } from './types'

export async function lookupCatalog(catalogType: string, q: string | null, limit = 20, options?: HttpRequestOptions): Promise<LookupItemDto[]> {
  const query: QueryParams = { limit }
  if (q && q.trim().length > 0) query.q = q
  if (!options) return await httpGet<LookupItemDto[]>(`/api/catalogs/${encodeURIComponent(catalogType)}/lookup`, query)
  return await httpGet<LookupItemDto[]>(`/api/catalogs/${encodeURIComponent(catalogType)}/lookup`, query, options)
}

export async function getCatalogLookupByIds(catalogType: string, ids: string[]): Promise<LookupItemDto[]> {
  const body: ByIdsRequestDto = { ids }
  return await httpPost<LookupItemDto[]>(`/api/catalogs/${encodeURIComponent(catalogType)}/by-ids`, body)
}
