import { normalizeDocumentStatusValue } from '../documents/documentStatus'
import { httpDelete, httpGet, httpPost, httpPut, type HttpRequestOptions } from './http'
import type {
  DocumentEditorStateDto,
  ExecuteDocumentActionRequestDto,
  ExecuteDocumentActionResultDto,
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

const editorStateRequests = new Map<string, Promise<DocumentEditorStateDto>>()

function documentKey(documentType: string, id: string): string {
  return `${documentType}\u0000${id}`
}

export async function getDocumentTypeMetadata(documentType: string): Promise<DocumentTypeMetadataDto> {
  return await httpGet<DocumentTypeMetadataDto>(`/api/documents/${encodeURIComponent(documentType)}/metadata`)
}

export async function getDocumentPage(
  documentType: string,
  req: PageRequest,
  options?: HttpRequestOptions,
): Promise<PageResponseDto<DocumentDto>> {
  const url = `/api/documents/${encodeURIComponent(documentType)}`
  const query = toPageQuery(req)
  const page = options
    ? await httpGet<PageResponseDto<DocumentDto>>(url, query, options)
    : await httpGet<PageResponseDto<DocumentDto>>(url, query)
  return normalizeDocumentPage(page)
}

export async function getDocumentById(documentType: string, id: string): Promise<DocumentDto> {
  const document = await httpGet<DocumentDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}`,
  )
  return normalizeDocumentDto(document)
}

export async function getDocumentEditorState(
  documentType: string,
  id: string,
): Promise<DocumentEditorStateDto> {
  const key = documentKey(documentType, id)
  const pending = editorStateRequests.get(key)
  if (pending) return await pending

  const request = httpGet<DocumentEditorStateDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}/editor-state`,
  ).then((state) => ({
    ...state,
    document: normalizeDocumentDto(state.document),
  }))
  editorStateRequests.set(key, request)

  try {
    return await request
  } finally {
    editorStateRequests.delete(key)
  }
}

export async function executeDocumentAction(
  documentType: string,
  id: string,
  actionCode: string,
  request: ExecuteDocumentActionRequestDto,
  idempotencyKey: string = globalThis.crypto.randomUUID(),
): Promise<ExecuteDocumentActionResultDto> {
  const result = await httpPost<ExecuteDocumentActionResultDto>(
    `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(id)}/actions/${encodeURIComponent(actionCode)}`,
    request,
    { headers: { 'Idempotency-Key': idempotencyKey } },
  )

  return {
    ...result,
    document: normalizeDocumentDto(result.document),
    createdDocument: result.createdDocument ? normalizeDocumentDto(result.createdDocument) : result.createdDocument,
  }
}

export async function lookupDocumentsAcrossTypes(
  request: DocumentLookupAcrossTypesRequestDto,
  options?: HttpRequestOptions,
): Promise<DocumentLookupDto[]> {
  const result = options
    ? await httpPost<DocumentLookupDto[]>('/api/documents/lookup', request, options)
    : await httpPost<DocumentLookupDto[]>('/api/documents/lookup', request)
  return result.map(normalizeDocumentLookup)
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
