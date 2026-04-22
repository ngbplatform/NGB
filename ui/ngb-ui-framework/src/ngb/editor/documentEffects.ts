import { isEmptyGuid, isNonEmptyGuid, shortGuid } from '../utils/guid';
import type { DocumentEffects, EffectAccount } from './types';

type CoaLabelResolver = (id: unknown) => string;

export function resolveEffectAccountLabel(
  account: EffectAccount | null | undefined,
  accountId: string | null | undefined,
  resolveCoaLabel?: CoaLabelResolver | null,
): string {
  const code = String(account?.code ?? '').trim();
  const name = String(account?.name ?? '').trim();
  if (code && name) return `${code} — ${name}`;
  if (code) return code;
  if (name) return name;
  if (resolveCoaLabel && isNonEmptyGuid(accountId) && !isEmptyGuid(accountId)) return resolveCoaLabel(accountId);
  return '—';
}

export function finalizeEffectDimensionSummary(
  items: string[],
  fallbackDimensionSetId?: string | null,
): string | string[] {
  const visibleItems = items.filter((item) => !!item && item !== '—');
  if (visibleItems.length > 0) return visibleItems;
  if (isNonEmptyGuid(fallbackDimensionSetId) && !isEmptyGuid(fallbackDimensionSetId)) return shortGuid(fallbackDimensionSetId);
  return '—';
}

export function collectAccountingEntryAccountIds(snapshot: DocumentEffects | null | undefined): string[] {
  const ids = new Set<string>();

  for (const entry of snapshot?.accountingEntries ?? []) {
    if (!entry.debitAccount && isNonEmptyGuid(entry.debitAccountId) && !isEmptyGuid(entry.debitAccountId)) {
      ids.add(entry.debitAccountId);
    }
    if (!entry.creditAccount && isNonEmptyGuid(entry.creditAccountId) && !isEmptyGuid(entry.creditAccountId)) {
      ids.add(entry.creditAccountId);
    }
  }

  return Array.from(ids);
}
