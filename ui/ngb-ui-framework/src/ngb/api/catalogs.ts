import { httpDelete, httpGet, httpPost, httpPut } from './http'
import type { CatalogItemDto, CatalogTypeMetadataDto, PageRequest, PageResponseDto, RecordPayload } from './contracts'

function toPageQuery(req: PageRequest | null | undefined) {
  if (!req) return undefined
  return {
    offset: req.offset,
    limit: req.limit,
    search: req.search,
    ...(req.filters ?? {}),
  }
}

export async function getCatalogTypeMetadata(catalogType: string): Promise<CatalogTypeMetadataDto> {
  return await httpGet<CatalogTypeMetadataDto>(`/api/catalogs/${encodeURIComponent(catalogType)}/metadata`)
}

export async function getCatalogPage(
  catalogType: string,
  req: PageRequest,
): Promise<PageResponseDto<CatalogItemDto>> {
  return await httpGet<PageResponseDto<CatalogItemDto>>(
    `/api/catalogs/${encodeURIComponent(catalogType)}`,
    toPageQuery(req),
  )
}

export async function getCatalogById(catalogType: string, id: string): Promise<CatalogItemDto> {
  return await httpGet<CatalogItemDto>(`/api/catalogs/${encodeURIComponent(catalogType)}/${encodeURIComponent(id)}`)
}

export async function createCatalog(catalogType: string, payload: RecordPayload): Promise<CatalogItemDto> {
  return await httpPost<CatalogItemDto>(`/api/catalogs/${encodeURIComponent(catalogType)}`, payload)
}

export async function updateCatalog(catalogType: string, id: string, payload: RecordPayload): Promise<CatalogItemDto> {
  return await httpPut<CatalogItemDto>(`/api/catalogs/${encodeURIComponent(catalogType)}/${encodeURIComponent(id)}`, payload)
}

export async function deleteCatalog(catalogType: string, id: string): Promise<void> {
  await httpDelete<void>(`/api/catalogs/${encodeURIComponent(catalogType)}/${encodeURIComponent(id)}`)
}

export async function markCatalogForDeletion(catalogType: string, id: string): Promise<void> {
  await httpPost<void>(`/api/catalogs/${encodeURIComponent(catalogType)}/${encodeURIComponent(id)}/mark-for-deletion`)
}

export async function unmarkCatalogForDeletion(catalogType: string, id: string): Promise<void> {
  await httpPost<void>(`/api/catalogs/${encodeURIComponent(catalogType)}/${encodeURIComponent(id)}/unmark-for-deletion`)
}
