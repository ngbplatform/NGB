import { normalizeDateOnlyValue, toDateOnlyValue } from '../utils/dateValues';
import type { GeneralJournalEntryEditorLineModel } from './generalJournalEntryTypes';

function randomToken(): string {
  return Math.random().toString(36).slice(2, 8);
}

export function createGeneralJournalEntryLineKey(prefix = 'line'): string {
  return `${prefix}-${Date.now().toString(36)}-${randomToken()}`;
}

export function createGeneralJournalEntryLine(
  clientKey = createGeneralJournalEntryLineKey(),
): GeneralJournalEntryEditorLineModel {
  return {
    clientKey,
    side: 1,
    account: null,
    amount: '',
    memo: '',
    dimensions: {},
  };
}

export function parseGeneralJournalEntryAmount(value: string): number {
  const normalized = String(value ?? '').trim().replace(/,/g, '');
  const amount = Number(normalized);
  return Number.isFinite(amount) ? amount : Number.NaN;
}

export function formatGeneralJournalEntryMoney(value: number): string {
  const amount = Number.isFinite(value) ? value : 0;
  return amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

export function todayDateOnly(now = new Date()): string {
  return toDateOnlyValue(now);
}

export function normalizeDateOnly(value: string | null | undefined): string | null {
  const normalized = normalizeDateOnlyValue(value);
  if (normalized) return normalized;

  const raw = String(value ?? '').trim();
  if (!raw) return null;

  const date = new Date(raw);
  if (Number.isNaN(date.getTime())) return null;
  return date.toISOString().slice(0, 10);
}

export function toUtcMidday(dateOnly: string | null | undefined): string {
  const normalized = normalizeDateOnly(dateOnly);
  if (!normalized) throw new Error('Date is required.');
  return `${normalized}T12:00:00Z`;
}

export function normalizeGeneralJournalEntryApprovalState(value: unknown): number {
  if (typeof value === 'number' && Number.isFinite(value)) return value;

  const raw = String(value ?? '').trim().toLowerCase();
  switch (raw) {
    case '1':
    case 'draft':
      return 1;
    case '2':
    case 'submitted':
      return 2;
    case '3':
    case 'approved':
      return 3;
    case '4':
    case 'rejected':
      return 4;
    default:
      return 1;
  }
}

export function normalizeGeneralJournalEntrySource(value: unknown): number {
  if (typeof value === 'number' && Number.isFinite(value)) return value;

  const raw = String(value ?? '').trim().toLowerCase();
  switch (raw) {
    case '2':
    case 'system':
      return 2;
    case '1':
    case 'manual':
    default:
      return 1;
  }
}

export function generalJournalEntryApprovalStateLabel(value: unknown): string {
  switch (normalizeGeneralJournalEntryApprovalState(value)) {
    case 2:
      return 'Submitted';
    case 3:
      return 'Approved';
    case 4:
      return 'Rejected';
    default:
      return 'Draft';
  }
}

export function generalJournalEntrySourceLabel(value: unknown): string {
  return normalizeGeneralJournalEntrySource(value) === 2 ? 'System' : 'Manual';
}

export function generalJournalEntryJournalTypeLabel(value: unknown): string {
  switch (Number(value)) {
    case 1:
      return 'Standard';
    case 2:
      return 'Reversing';
    case 3:
      return 'Adjusting';
    case 4:
      return 'Opening';
    case 5:
      return 'Closing';
    default:
      return '—';
  }
}
