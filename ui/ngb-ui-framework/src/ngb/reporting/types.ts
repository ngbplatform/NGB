import type { LookupItem, LookupSource } from '../metadata/types'

export enum ReportAggregationKind {
  Sum = 1,
  Min = 2,
  Max = 3,
  Average = 4,
  Count = 5,
  CountDistinct = 6,
  First = 7,
  Last = 8,
}

export enum ReportExecutionMode {
  Canonical = 1,
  Composable = 2,
}

export enum ReportFieldKind {
  Attribute = 1,
  Dimension = 2,
  Time = 3,
  Detail = 4,
  System = 5,
}

export enum ReportTimeGrain {
  Day = 1,
  Week = 2,
  Month = 3,
  Quarter = 4,
  Year = 5,
}

export enum ReportSortDirection {
  Asc = 1,
  Desc = 2,
}

export enum ReportRowKind {
  Header = 1,
  Group = 2,
  Detail = 3,
  Subtotal = 4,
  Total = 5,
}

export type ReportCapabilitiesDto = {
  allowsFilters?: boolean
  allowsRowGroups?: boolean
  allowsColumnGroups?: boolean
  allowsMeasures?: boolean
  allowsDetailFields?: boolean
  allowsSorting?: boolean
  allowsShowDetails?: boolean
  allowsSubtotals?: boolean
  allowsSeparateRowSubtotals?: boolean
  allowsGrandTotals?: boolean
  allowsVariants?: boolean
  allowsXlsxExport?: boolean
  maxRowGroupDepth?: number | null
  maxColumnGroupDepth?: number | null
  maxVisibleColumns?: number | null
  maxVisibleRows?: number | null
  maxRenderedCells?: number | null
}

export type ReportPresentationDto = {
  initialPageSize?: number | null
  rowNoun?: string | null
  emptyStateMessage?: string | null
}

export type ReportFieldDto = {
  code: string
  label: string
  dataType: string
  kind: ReportFieldKind
  isFilterable?: boolean
  isGroupable?: boolean
  isSortable?: boolean
  isSelectable?: boolean
  supportsIncludeDescendants?: boolean
  defaultIncludeDescendants?: boolean
  supportedTimeGrains?: ReportTimeGrain[] | null
  lookup?: LookupSource | null
  description?: string | null
  format?: string | null
}

export type ReportMeasureDto = {
  code: string
  label: string
  dataType: string
  supportedAggregations?: ReportAggregationKind[] | null
  format?: string | null
  description?: string | null
}

export type ReportDatasetDto = {
  datasetCode: string
  fields?: ReportFieldDto[] | null
  measures?: ReportMeasureDto[] | null
}

export type ReportParameterMetadataDto = {
  code: string
  dataType: string
  isRequired: boolean
  description?: string | null
  defaultValue?: string | null
  label?: string | null
}

export type ReportFilterOptionDto = {
  value: string
  label: string
}

export type ReportFilterFieldDto = {
  fieldCode: string
  label: string
  dataType: string
  isRequired?: boolean
  isMulti?: boolean
  supportsIncludeDescendants?: boolean
  defaultIncludeDescendants?: boolean
  lookup?: LookupSource | null
  options?: ReportFilterOptionDto[] | null
  description?: string | null
}

export type ReportGroupingDto = {
  fieldCode: string
  timeGrain?: ReportTimeGrain | null
  includeDetails?: boolean
  includeEmpty?: boolean
  includeDescendants?: boolean
  labelOverride?: string | null
  groupKey?: string | null
}

export type ReportMeasureSelectionDto = {
  measureCode: string
  aggregation?: ReportAggregationKind
  labelOverride?: string | null
  formatOverride?: string | null
}

export type ReportSortDto = {
  fieldCode: string
  direction?: ReportSortDirection
  timeGrain?: ReportTimeGrain | null
  appliesToColumnAxis?: boolean
  groupKey?: string | null
}

export type ReportLayoutDto = {
  rowGroups?: ReportGroupingDto[] | null
  columnGroups?: ReportGroupingDto[] | null
  measures?: ReportMeasureSelectionDto[] | null
  detailFields?: string[] | null
  sorts?: ReportSortDto[] | null
  showDetails?: boolean
  showSubtotals?: boolean
  showSubtotalsOnSeparateRows?: boolean
  showGrandTotals?: boolean
}

export type ReportDefinitionDto = {
  reportCode: string
  name: string
  group?: string | null
  description?: string | null
  mode?: ReportExecutionMode
  dataset?: ReportDatasetDto | null
  capabilities?: ReportCapabilitiesDto | null
  defaultLayout?: ReportLayoutDto | null
  parameters?: ReportParameterMetadataDto[] | null
  filters?: ReportFilterFieldDto[] | null
  presentation?: ReportPresentationDto | null
}

export type ReportFilterValueDto = {
  value: unknown
  includeDescendants?: boolean
}

export type ReportExportRequestDto = {
  layout?: ReportLayoutDto | null
  filters?: Record<string, ReportFilterValueDto> | null
  parameters?: Record<string, string> | null
  variantCode?: string | null
}

export type ReportExecutionRequestDto = {
  layout?: ReportLayoutDto | null
  filters?: Record<string, ReportFilterValueDto> | null
  parameters?: Record<string, string> | null
  variantCode?: string | null
  offset?: number
  limit?: number
  cursor?: string | null
}

export type ReportCellReportTargetDto = {
  reportCode: string
  parameters?: Record<string, string> | null
  filters?: Record<string, ReportFilterValueDto> | null
}

export type ReportCellActionDto = {
  kind: string
  documentType?: string | null
  documentId?: string | null
  accountId?: string | null
  catalogType?: string | null
  catalogId?: string | null
  report?: ReportCellReportTargetDto | null
}

export type ReportCellDto = {
  value?: unknown
  display?: string | null
  valueType?: string | null
  colSpan?: number
  rowSpan?: number
  styleKey?: string | null
  semanticRole?: string | null
  action?: ReportCellActionDto | null
}

export type ReportSheetColumnDto = {
  code: string
  title: string
  dataType: string
  width?: number
  isFrozen?: boolean
  semanticRole?: string | null
}

export type ReportSheetRowDto = {
  rowKind: ReportRowKind
  cells: ReportCellDto[]
  outlineLevel?: number
  isExpanded?: boolean
  groupKey?: string | null
  semanticRole?: string | null
}

export type ReportSheetMetaDto = {
  title?: string | null
  subtitle?: string | null
  isPivot?: boolean
  hasRowOutline?: boolean
  hasColumnGroups?: boolean
  diagnostics?: Record<string, string> | null
}

export type ReportSheetDto = {
  columns: ReportSheetColumnDto[]
  rows: ReportSheetRowDto[]
  meta?: ReportSheetMetaDto | null
  headerRows?: ReportSheetRowDto[] | null
}

export type ReportExecutionResponseDto = {
  sheet: ReportSheetDto
  offset: number
  limit: number
  total?: number | null
  hasMore: boolean
  nextCursor?: string | null
  diagnostics?: Record<string, string> | null
}

export type ReportVariantDto = {
  variantCode: string
  reportCode: string
  name: string
  layout?: ReportLayoutDto | null
  filters?: Record<string, ReportFilterValueDto> | null
  parameters?: Record<string, string> | null
  isDefault?: boolean
  isShared?: boolean
}

export type ReportComposerLookupItem = LookupItem

export type ReportComposerFilterState = {
  raw: string
  items: ReportComposerLookupItem[]
  includeDescendants: boolean
}

export type ReportComposerGroupingState = {
  groupKey?: string | null
  fieldCode: string
  timeGrain: ReportTimeGrain | null
}

export type ReportComposerMeasureState = {
  measureCode: string
  aggregation: ReportAggregationKind | null
  labelOverride: string | null
}

export type ReportComposerSortState = {
  fieldCode: string
  groupKey?: string | null
  appliesToColumnAxis?: boolean
  direction: ReportSortDirection
  timeGrain: ReportTimeGrain | null
}

export type ReportComposerDraft = {
  parameters: Record<string, string>
  filters: Record<string, ReportComposerFilterState>
  rowGroups: ReportComposerGroupingState[]
  columnGroups: ReportComposerGroupingState[]
  measures: ReportComposerMeasureState[]
  detailFields: string[]
  sorts: ReportComposerSortState[]
  showDetails: boolean
  showSubtotals: boolean
  showSubtotalsOnSeparateRows: boolean
  showGrandTotals: boolean
}

export type ReportOptionItem<T extends string | number = string | number> = {
  value: T
  label: string
}
