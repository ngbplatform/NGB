export const DEFAULT_CHART_OF_ACCOUNTS_BASE_PATH = '/admin/chart-of-accounts';
export const DEFAULT_GENERAL_JOURNAL_ENTRIES_BASE_PATH = '/accounting/general-journal-entries';
export const DEFAULT_ACCOUNTING_PERIOD_CLOSING_BASE_PATH = '/admin/accounting/period-closing';

export type ChartOfAccountsRouteOptions = {
  panel?: 'new' | 'edit' | null;
  id?: string | null;
  basePath?: string | null;
};

export type GeneralJournalEntriesRouteOptions = {
  basePath?: string | null;
};

export type AccountingPeriodClosingRouteOptions = {
  year?: string | number | null;
  month?: string | null;
  fy?: string | null;
  basePath?: string | null;
};

const GENERAL_JOURNAL_ENTRY_DOCUMENT_TYPES = new Set([
  'accounting.general_journal_entry',
  'general_journal_entry',
]);

function normalizePathSegment(value: string | null | undefined): string {
  return String(value ?? '').trim();
}

function appendQuery(path: string, query: Record<string, string | null | undefined>): string {
  const params = new URLSearchParams();

  for (const [key, value] of Object.entries(query)) {
    const normalized = normalizePathSegment(value);
    if (!normalized) continue;
    params.set(key, normalized);
  }

  const serialized = params.toString();
  return serialized ? `${path}?${serialized}` : path;
}

export function buildChartOfAccountsPath(options: ChartOfAccountsRouteOptions = {}): string {
  return appendQuery(
    normalizePathSegment(options.basePath) || DEFAULT_CHART_OF_ACCOUNTS_BASE_PATH,
    {
      panel: options.panel ?? undefined,
      id: options.id ?? undefined,
    },
  );
}

export function isGeneralJournalEntryDocumentType(documentType: string | null | undefined): boolean {
  return GENERAL_JOURNAL_ENTRY_DOCUMENT_TYPES.has(normalizePathSegment(documentType));
}

export function buildGeneralJournalEntriesListPath(options: GeneralJournalEntriesRouteOptions = {}): string {
  return normalizePathSegment(options.basePath) || DEFAULT_GENERAL_JOURNAL_ENTRIES_BASE_PATH;
}

export function buildGeneralJournalEntriesPath(
  id?: string | null,
  options: GeneralJournalEntriesRouteOptions = {},
): string {
  const basePath = buildGeneralJournalEntriesListPath(options);
  const normalizedId = normalizePathSegment(id);
  if (!normalizedId) return `${basePath}/new`;
  return `${basePath}/${encodeURIComponent(normalizedId)}`;
}

export function buildAccountingPeriodClosingPath(
  options: AccountingPeriodClosingRouteOptions = {},
): string {
  return appendQuery(
    normalizePathSegment(options.basePath) || DEFAULT_ACCOUNTING_PERIOD_CLOSING_BASE_PATH,
    {
      year: options.year == null ? undefined : String(options.year),
      month: options.month ?? undefined,
      fy: options.fy ?? undefined,
    },
  );
}
