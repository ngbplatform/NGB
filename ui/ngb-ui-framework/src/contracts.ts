/**
 * Runtime-safe public contracts for Node-side consumers such as Playwright
 * fixtures, data generators, and build tooling. This entrypoint must remain
 * free of Vue components and browser-only initialization.
 */
export type {
  CatalogItemDto,
  CatalogTypeMetadataDto,
  DocumentActionDto,
  DocumentDto,
  DocumentTypeMetadataDto,
  PageResponseDto,
  RelationshipGraphDto,
} from './ngb/api/contracts'
export type {
  ChartOfAccountsAccountDto,
  ChartOfAccountsMetadataDto,
  ChartOfAccountsPageDto,
  ChartOfAccountsUpsertRequestDto,
} from './ngb/accounting/types'
export type {
  FiscalYearCloseStatusDto,
  PeriodClosingCalendarDto,
  PeriodCloseStatusDto,
  RetainedEarningsAccountOptionDto,
} from './ngb/accounting/periodClosingTypes'
export type {
  GeneralJournalEntryDetailsDto,
  GeneralJournalEntryDocumentDto,
  GeneralJournalEntryHeaderDto,
  GeneralJournalEntryPageDto,
} from './ngb/accounting/generalJournalEntryTypes'
export type { ReferenceValue } from './ngb/metadata/types'
export {
  ReportAggregationKind,
  ReportExecutionMode,
  ReportFieldKind,
  ReportRowKind,
  type ReportCellDto,
  type ReportDefinitionDto,
  type ReportExecutionRequestDto,
  type ReportExecutionResponseDto,
  type ReportSheetRowDto,
  type ReportVariantDto,
} from './ngb/reporting/types'
export type {
  NotificationPreference,
  WorkCenterItem,
} from './ngb/work-center/types'
