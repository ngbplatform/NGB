import type { DocumentStatusValue } from '../metadata/types';

export type DocumentStatusTone = 'neutral' | 'success' | 'warn';
export type DocumentStatusVisual = 'saved' | 'posted' | 'marked';

export function normalizeDocumentStatusValue(value: unknown): DocumentStatusValue {
  if (typeof value === 'number' && Number.isFinite(value) && (value === 1 || value === 2 || value === 3)) {
    return value as DocumentStatusValue;
  }

  const normalized = String(value ?? '').trim().toLowerCase();
  if (normalized === '1' || normalized === 'draft') return 1;
  if (normalized === '2' || normalized === 'posted') return 2;
  if (
    normalized === '3'
    || normalized === 'deleted'
    || normalized === 'markedfordeletion'
    || normalized === 'marked_for_deletion'
    || normalized === 'marked-for-deletion'
    || normalized === 'marked for deletion'
  ) return 3;

  return 1;
}

export function documentStatusLabel(value: unknown): string {
  const status = normalizeDocumentStatusValue(value);
  if (status === 2) return 'Posted';
  if (status === 3) return 'Deleted';
  return 'Draft';
}

export function documentStatusTone(value: unknown): DocumentStatusTone {
  const status = normalizeDocumentStatusValue(value);
  if (status === 2) return 'success';
  if (status === 3) return 'warn';
  return 'neutral';
}

export function documentStatusVisual(value: unknown): DocumentStatusVisual {
  const status = normalizeDocumentStatusValue(value);
  if (status === 2) return 'posted';
  if (status === 3) return 'marked';
  return 'saved';
}
