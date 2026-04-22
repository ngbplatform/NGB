import { normalizeDocumentStatusValue } from '../editor/documentStatus'
import { httpDelete, httpGet, httpPost, httpPut } from './http'
import type {
  DocumentDerivationActionDto,
  DocumentDto,
  DocumentEffectsDto,
  DocumentLookupAcrossTypesRequestDto,
  DocumentLookupByIdsRequestDto,
  DocumentLookupDto,
  DocumentTypeMetadataDto,
  PageRequest,
  PageResponseDto,
  RecordPayload,
  RelationshipGraphDto,
} from './contracts'

function toPageQuery(req: PageRequest | null | undefined) {
  if (!req) return undefined
  return {
    offset: req.offset,
    limit: req.limit,
    search: req.search,
    ...(req.filters ?? {}),
  }
}

function normalizeDocumentDto(document: DocumentDto): DocumentDto {
  const status = normalizeDocumentStatusValue(document.status)
  if (document.status === status) return document
  return { ...document, status }
}

function normalizeDocumentLookup(document: DocumentLookupDto): DocumentLookupDto {
  const status = normalizeDocumentStatusValue(document.status)
  if (document.status === status) return document
  return { ...document, status }
}

function normalizeDocumentPage(page: PageResponseDto<DocumentDto>): PageResponseDto<DocumentDto> {
  return {
    ...page,
    items: (page.items ?? []).map(normalizeDocumentDto),
  }
}

export async function getDocumentTypeMetadata(documentType: string): Promise<DocumentTypeMetadataDto> {
  return await httpGet<DocumentTypeMetadataDto>(`/api/documents/${encodeURIComponent(documentType)}/metadata`)
}

export async function getDocumentPage(
  documentType: string,
  req: PageRequest,
): Promise<PageResponseDto<DocumentDto>> {
  const page = await httpGet<PageResponseDto<DocumentDto>>(
    `/api/documents/${encodeURIComponent(documentType)}`,
    toPageQuery(req),
  )
  return normalizeDocumentPage(page)
}

export async function getDocumentById(documentType: string, id: string): Promise<DocumentDto> {
  const document = await httpGet<DocumentDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}`,
  )
  return normalizeDocumentDto(document)
}

export async function getDocumentDerivationActions(
  documentType: string,
  id: string,
): Promise<DocumentDerivationActionDto[]> {
  return await httpGet<DocumentDerivationActionDto[]>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}/derive-actions`,
  )
}

export async function lookupDocumentsAcrossTypes(
  request: DocumentLookupAcrossTypesRequestDto,
): Promise<DocumentLookupDto[]> {
  return (await httpPost<DocumentLookupDto[]>('/api/documents/lookup', request)).map(normalizeDocumentLookup)
}

export async function getDocumentLookupByIds(
  request: DocumentLookupByIdsRequestDto,
): Promise<DocumentLookupDto[]> {
  return (await httpPost<DocumentLookupDto[]>('/api/documents/lookup/by-ids', request)).map(normalizeDocumentLookup)
}

export async function createDraft(documentType: string, payload: RecordPayload): Promise<DocumentDto> {
  const document = await httpPost<DocumentDto>(`/api/documents/${encodeURIComponent(documentType)}`, payload)
  return normalizeDocumentDto(document)
}

export async function deriveDocument(
  targetDocumentType: string,
  request: {
    sourceDocumentId: string
    relationshipType: string
    initialPayload?: RecordPayload | null
  },
): Promise<DocumentDto> {
  const document = await httpPost<DocumentDto>(
    `/api/documents/${encodeURIComponent(targetDocumentType)}/derive`,
    {
      sourceDocumentId: request.sourceDocumentId,
      relationshipType: request.relationshipType,
      initialPayload: request.initialPayload ?? null,
    },
  )
  return normalizeDocumentDto(document)
}

export async function updateDraft(documentType: string, id: string, payload: RecordPayload): Promise<DocumentDto> {
  const document = await httpPut<DocumentDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}`,
    payload,
  )
  return normalizeDocumentDto(document)
}

export async function deleteDraft(documentType: string, id: string): Promise<void> {
  await httpDelete<void>(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}`)
}

export async function postDocument(documentType: string, id: string): Promise<DocumentDto> {
  const document = await httpPost<DocumentDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}/post`,
  )
  return normalizeDocumentDto(document)
}

export async function unpostDocument(documentType: string, id: string): Promise<DocumentDto> {
  const document = await httpPost<DocumentDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}/unpost`,
  )
  return normalizeDocumentDto(document)
}

export async function markDocumentForDeletion(documentType: string, id: string): Promise<DocumentDto> {
  const document = await httpPost<DocumentDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}/mark-for-deletion`,
  )
  return normalizeDocumentDto(document)
}

export async function unmarkDocumentForDeletion(documentType: string, id: string): Promise<DocumentDto> {
  const document = await httpPost<DocumentDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}/unmark-for-deletion`,
  )
  return normalizeDocumentDto(document)
}

export async function getDocumentEffects(documentType: string, id: string, limit = 500): Promise<DocumentEffectsDto> {
  return await httpGet<DocumentEffectsDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}/effects`,
    { limit },
  )
}

export async function getDocumentGraph(
  documentType: string,
  id: string,
  depth = 5,
  maxNodes = 100,
): Promise<RelationshipGraphDto> {
  return await httpGet<RelationshipGraphDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}/graph`,
    { depth, maxNodes },
  )
}
