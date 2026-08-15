import type { DocumentStatusValue } from '../metadata/types'

export function normalizeDocumentStatusValue(value: unknown): DocumentStatusValue {
  if (typeof value === 'number' && Number.isFinite(value) && (value === 1 || value === 2 || value === 3)) {
    return value as DocumentStatusValue
  }

  const normalized = String(value ?? '').trim().toLowerCase()
  if (normalized === '1' || normalized === 'draft') return 1
  if (normalized === '2' || normalized === 'posted') return 2
  if (
    normalized === '3'
    || normalized === 'deleted'
    || normalized === 'markedfordeletion'
    || normalized === 'marked_for_deletion'
    || normalized === 'marked-for-deletion'
    || normalized === 'marked for deletion'
  ) return 3

  return 1
}
