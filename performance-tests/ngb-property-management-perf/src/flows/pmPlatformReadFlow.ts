import { jsonHas, operationSucceeded } from '../../../ngb-performance-tests-framework/src/core/checks.ts';
import { randomInt } from '../../../ngb-performance-tests-framework/src/core/random.ts';
import { thinkTime } from '../../../ngb-performance-tests-framework/src/core/sleep.ts';
import { catalogBrowseFlow } from '../../../ngb-performance-tests-framework/src/flows/catalogBrowseFlow.ts';
import { documentOpenFlow } from '../../../ngb-performance-tests-framework/src/flows/documentOpenFlow.ts';
import type { DiagnosticBreakdownSelector } from '../../../ngb-performance-tests-framework/src/profiles/thresholds.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_CATALOG_TYPES } from '../clients/pmCatalogTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';
import { pageItemIds, resolveAccountId, resolvePeriodProfile } from './pmFlowSupport.ts';

const READ_CATALOG_TYPES = [
  PM_CATALOG_TYPES.property,
  PM_CATALOG_TYPES.party,
  PM_CATALOG_TYPES.bankAccount,
  PM_CATALOG_TYPES.maintenanceCategory,
  PM_CATALOG_TYPES.receivableChargeType,
  PM_CATALOG_TYPES.payableChargeType,
] as const;

const READ_DOCUMENT_TYPES = [
  PM_DOCUMENT_TYPES.lease,
  PM_DOCUMENT_TYPES.rentCharge,
  PM_DOCUMENT_TYPES.receivableCharge,
  PM_DOCUMENT_TYPES.receivablePayment,
  PM_DOCUMENT_TYPES.maintenanceRequest,
  PM_DOCUMENT_TYPES.workOrder,
] as const;

const FAILURE_STATUS_CODES = [
  '0',
  '400',
  '401',
  '403',
  '404',
  '408',
  '409',
  '422',
  '429',
  '500',
  '502',
  '503',
  '504',
] as const;

const PLATFORM_READ_OPERATION_BREAKDOWNS: readonly DiagnosticBreakdownSelector[] = [
  ...READ_CATALOG_TYPES.flatMap((catalogType) => [
    { area: 'catalogs', operation: 'platform.catalogs.list', catalogType },
    { area: 'catalogs', operation: 'platform.catalogs.open', catalogType },
  ]),
  ...READ_DOCUMENT_TYPES.flatMap((documentType) => [
    { area: 'documents', operation: 'platform.documents.list', documentType },
    { area: 'documents', operation: 'platform.documents.open', documentType },
  ]),
  { area: 'documents', operation: 'platform.documents.lookup' },
  { area: 'documents', operation: 'platform.documents.lookup_by_ids' },
];

const PLATFORM_READ_FAILURE_STATUS_BREAKDOWNS: readonly DiagnosticBreakdownSelector[] = withFailureStatuses([
  { area: 'documents', operation: 'platform.documents.lookup' },
  { area: 'documents', operation: 'platform.documents.lookup_by_ids' },
  { area: 'documents', operation: 'platform.documents.list', documentType: PM_DOCUMENT_TYPES.rentCharge },
  { area: 'documents', operation: 'platform.documents.list', documentType: PM_DOCUMENT_TYPES.receivablePayment },
  { area: 'documents', operation: 'platform.documents.list', documentType: PM_DOCUMENT_TYPES.receivableCharge },
  { area: 'documents', operation: 'platform.documents.list', documentType: PM_DOCUMENT_TYPES.lease },
  { area: 'catalogs', operation: 'platform.catalogs.list', catalogType: PM_CATALOG_TYPES.party },
  { area: 'catalogs', operation: 'platform.catalogs.list', catalogType: PM_CATALOG_TYPES.property },
]);

export const PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS: readonly DiagnosticBreakdownSelector[] = [
  ...PLATFORM_READ_OPERATION_BREAKDOWNS,
  ...PLATFORM_READ_FAILURE_STATUS_BREAKDOWNS,
];

export interface PmPlatformReadFlowOptions {
  readonly includeMetadata?: boolean;
  readonly includeLookup?: boolean;
  readonly includeDeepPages?: boolean;
}

export function pmPlatformReadFlow(context: NgbScenarioContext, options: PmPlatformReadFlowOptions = {}): void {
  if (options.includeMetadata ?? true) {
    readPlatformMetadata(context);
  }

  readAdminSurfaces(context);
  readCatalogSurfaces(context);
  const documentIds = readDocumentSurfaces(context, options.includeDeepPages ?? false);

  if (options.includeLookup ?? true) {
    readLookupSurfaces(context, documentIds);
  }
}

function readPlatformMetadata(context: NgbScenarioContext): void {
  operationSucceeded(context.health.check(), [200]);
  context.metadata.loadAll();

  for (const catalogType of READ_CATALOG_TYPES) {
    operationSucceeded(context.catalogs.getCatalogMetadata(catalogType), [200]);
  }

  for (const documentType of READ_DOCUMENT_TYPES) {
    operationSucceeded(context.documents.getDocumentMetadata(documentType), [200]);
  }
}

function readAdminSurfaces(context: NgbScenarioContext): void {
  operationSucceeded(context.admin.getMainMenu(), [200]);
  operationSucceeded(context.admin.getChartOfAccountsMetadata(), [200]);
  const accountPage = context.admin.listChartOfAccounts({ offset: 0, limit: 50, onlyActive: true });
  operationSucceeded(accountPage, [200]);
  jsonHas(accountPage, 'items');

  const accountId = resolveAccountId() ?? firstAccountId(accountPage);
  if (accountId) {
    operationSucceeded(context.admin.getChartOfAccount(accountId), [200]);
    operationSucceeded(context.admin.getChartOfAccountsByIds([accountId]), [200]);
  }
}

function firstAccountId(response: { json(): unknown }): string | null {
  try {
    const json = response.json();
    const items = typeof json === 'object' && json !== null
      ? (json as { items?: Array<{ accountId?: unknown }> }).items
      : undefined;
    const accountId = items?.[0]?.accountId;
    return typeof accountId === 'string' && accountId.length > 0 ? accountId : null;
  } catch {
    return null;
  }
}

function readCatalogSurfaces(context: NgbScenarioContext): void {
  for (const catalogType of READ_CATALOG_TYPES) {
    catalogBrowseFlow(context, catalogType);
  }
}

function readDocumentSurfaces(context: NgbScenarioContext, includeDeepPages: boolean): string[] {
  const period = resolvePeriodProfile('open');
  const ids: string[] = [];

  for (const documentType of READ_DOCUMENT_TYPES) {
    const page = context.documents.listDocuments(documentType, {
      offset: 0,
      limit: 50,
      filters: {
        deleted: 'active',
        periodFrom: period.fromUtc,
        periodTo: period.toUtc,
      },
    });
    operationSucceeded(page, [200]);
    jsonHas(page, 'items');
    const currentIds = pageItemIds(page, 3);
    ids.push(...currentIds);

    const firstId = currentIds[0];
    if (firstId) {
      documentOpenFlow(context, documentType, firstId);
    }

    if (includeDeepPages) {
      const deepOffset = randomInt(1, 10) * 200;
      const deepPage = context.documents.listDocuments(documentType, {
        offset: deepOffset,
        limit: 100,
        filters: {
          deleted: 'active',
          periodFrom: period.fromUtc,
          periodTo: period.toUtc,
        },
      });
      operationSucceeded(deepPage, [200]);
      jsonHas(deepPage, 'items');
    }

    thinkTime(0.1, 0.4);
  }

  return [...new Set(ids)];
}

function readLookupSurfaces(context: NgbScenarioContext, documentIds: readonly string[]): void {
  const lookup = context.documents.lookupAcrossTypes({
    documentTypes: READ_DOCUMENT_TYPES,
    query: '',
    perTypeLimit: 5,
    activeOnly: true,
  });
  operationSucceeded(lookup, [200]);

  if (documentIds.length > 0) {
    operationSucceeded(context.documents.getByIdsAcrossTypes({
      documentTypes: READ_DOCUMENT_TYPES,
      ids: documentIds.slice(0, 10),
    }), [200]);
  }
}

function withFailureStatuses(
  selectors: readonly DiagnosticBreakdownSelector[],
): DiagnosticBreakdownSelector[] {
  return selectors.flatMap((selector) => FAILURE_STATUS_CODES.map((status) => ({ ...selector, status })));
}
