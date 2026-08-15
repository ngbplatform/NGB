import { normalizeDocumentStatusValue } from '../documents/documentStatus';

export { normalizeDocumentStatusValue } from '../documents/documentStatus';

export type DocumentStatusTone = 'neutral' | 'success' | 'warn';
export type DocumentStatusVisual = 'saved' | 'posted' | 'marked';

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
