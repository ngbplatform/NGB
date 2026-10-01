export type BusinessObjectKind = 'CatalogItem' | 'Document' | 'GeneralJournalEntry'
export type BusinessObjectRef = { kind: BusinessObjectKind; typeCode: string; id: string }
export type ContentSummary = { attachments: number | null; notes: number | null }
export type ContentPage<T> = { items: T[]; nextCursor: string | null }
export type Attachment = {
  id: string; fileName: string; contentType: string; sizeBytes: number
  createdAtUtc: string; createdByUserId: string; createdByDisplayName: string | null
}
export type Note = {
  id: string; text: string; version: number; createdAtUtc: string; createdByUserId: string
  createdByDisplayName: string | null; updatedAtUtc: string | null; updatedByUserId: string | null
}
export type UploadTarget = { attachmentId: string; url: string; expiresAtUtc: string; headers: Record<string, string> }
