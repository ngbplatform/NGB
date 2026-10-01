import { httpDelete, httpGet, httpPost, httpPut } from '../api/http'
import type { Attachment, BusinessObjectRef, ContentPage, ContentSummary, Note, UploadTarget } from './types'

const query = (target: BusinessObjectRef) => ({ kind: target.kind, typeCode: target.typeCode, objectId: target.id })
export const contentApi = {
  summary: (target: BusinessObjectRef, signal: AbortSignal) =>
    httpGet<ContentSummary>('/api/business-objects/content-summary', query(target), { signal }),
  attachments: (target: BusinessObjectRef, cursor: string | null, signal: AbortSignal) =>
    httpGet<ContentPage<Attachment>>('/api/attachments', { ...query(target), limit: 50, cursor }, { signal }),
  notes: (target: BusinessObjectRef, cursor: string | null, signal: AbortSignal) =>
    httpGet<ContentPage<Note>>('/api/notes', { ...query(target), limit: 50, cursor }, { signal }),
  requestUpload: (target: BusinessObjectRef, file: File, signal: AbortSignal) =>
    httpPost<UploadTarget>('/api/attachments/uploads', {
      target, fileName: file.name, contentType: file.type || 'application/octet-stream', sizeBytes: file.size,
    }, { signal }),
  complete: (id: string, signal: AbortSignal) => httpPost<Attachment>(`/api/attachments/${id}/complete`, undefined, { signal }),
  deleteAttachment: (id: string, signal: AbortSignal) => httpDelete<void>(`/api/attachments/${id}`, undefined, { signal }),
  download: (id: string, signal: AbortSignal) =>
    httpPost<{ url: string; expiresAtUtc: string }>(`/api/attachments/${id}/download`, undefined, { signal }),
  createNote: (target: BusinessObjectRef, text: string, signal: AbortSignal) => httpPost<Note>('/api/notes', { target, text }, { signal }),
  updateNote: (note: Note, text: string, signal: AbortSignal) => httpPut<Note>(`/api/notes/${note.id}`, { text, version: note.version }, { signal }),
  deleteNote: (note: Note, signal: AbortSignal) => httpDelete<void>(`/api/notes/${note.id}?version=${note.version}`, undefined, { signal }),
}

/** Never use the authenticated API transport for storage URLs. */
export async function uploadBytes(target: UploadTarget, file: File, signal: AbortSignal): Promise<void> {
  const response = await fetch(storageUrl(target.url), {
    method: 'PUT', body: file, headers: target.headers, signal, credentials: 'omit', referrerPolicy: 'no-referrer',
  })
  if (!response.ok) throw new Error('File upload failed. The upload link may have expired. Choose the file again to retry.')
}
export function storageUrl(value: string): string {
  const url = new URL(value)
  if (!['http:', 'https:'].includes(url.protocol)) throw new Error('Invalid storage URL.')
  return url.toString()
}
export function startAttachmentDownload(url: string): void {
  const link = document.createElement('a')
  link.href = storageUrl(url)
  link.rel = 'noopener noreferrer'
  link.target = '_blank'
  link.click()
}
