import {
  ReportAggregationKind,
  ReportExecutionMode,
  ReportFieldKind,
  ReportRowKind,
  type ReportDefinitionDto,
  type ReportExecutionResponseDto,
  type ReportVariantDto,
} from '../../../ngb-ui-framework/src/ngb/reporting/types'

export const occupancySummaryReportDefinitionFixture: ReportDefinitionDto = {
  reportCode: 'pm.occupancy.summary',
  name: 'Occupancy Summary',
  description: 'Portfolio occupancy by property and manager.',
  mode: ReportExecutionMode.Composable,
  capabilities: {
    allowsFilters: true,
    allowsRowGroups: true,
    allowsColumnGroups: true,
    allowsMeasures: true,
    allowsDetailFields: true,
    allowsSorting: true,
    allowsShowDetails: true,
    allowsSubtotals: true,
    allowsGrandTotals: true,
    allowsVariants: true,
    allowsXlsxExport: true,
  },
  dataset: {
    datasetCode: 'pm.occupancy.summary',
    fields: [
      {
        code: 'property',
        label: 'Property',
        dataType: 'String',
        kind: ReportFieldKind.Dimension,
        isGroupable: true,
        isSelectable: true,
        isSortable: true,
      },
      {
        code: 'manager',
        label: 'Manager',
        dataType: 'String',
        kind: ReportFieldKind.Attribute,
        isGroupable: true,
        isSelectable: true,
        isSortable: true,
      },
      {
        code: 'occupancy_band',
        label: 'Occupancy Band',
        dataType: 'String',
        kind: ReportFieldKind.Attribute,
        isGroupable: true,
        isSelectable: true,
        isSortable: true,
      },
    ],
    measures: [
      {
        code: 'total_units',
        label: 'Units',
        dataType: 'Decimal',
        supportedAggregations: [ReportAggregationKind.Sum],
      },
      {
        code: 'occupied_units',
        label: 'Occupied',
        dataType: 'Decimal',
        supportedAggregations: [ReportAggregationKind.Sum],
      },
      {
        code: 'vacant_units',
        label: 'Vacant',
        dataType: 'Decimal',
        supportedAggregations: [ReportAggregationKind.Sum],
      },
      {
        code: 'occupancy_rate',
        label: 'Occupancy %',
        dataType: 'Decimal',
        supportedAggregations: [ReportAggregationKind.Average],
      },
    ],
  },
  defaultLayout: {
    detailFields: ['property', 'manager', 'occupancy_band'],
    measures: [
      { measureCode: 'total_units', aggregation: ReportAggregationKind.Sum },
      { measureCode: 'occupied_units', aggregation: ReportAggregationKind.Sum },
      { measureCode: 'vacant_units', aggregation: ReportAggregationKind.Sum },
      { measureCode: 'occupancy_rate', aggregation: ReportAggregationKind.Average },
    ],
    showDetails: true,
    showSubtotals: true,
    showGrandTotals: true,
  },
  filters: [],
  parameters: [],
  presentation: {
    initialPageSize: 100,
    rowNoun: 'property',
    emptyStateMessage: 'Run the report to review current portfolio occupancy.',
  },
}

export const occupancySummaryReportVariantsFixture: ReportVariantDto[] = []

export const occupancySummaryReportExecutionFixture: ReportExecutionResponseDto = {
  sheet: {
    columns: [
      { code: 'property', title: 'Property', dataType: 'string' },
      { code: 'manager', title: 'Portfolio Manager', dataType: 'string' },
      { code: 'total_units', title: 'Units', dataType: 'number' },
      { code: 'occupied_units', title: 'Occupied', dataType: 'number' },
      { code: 'vacant_units', title: 'Vacant', dataType: 'number' },
      { code: 'occupancy_rate', title: 'Total', dataType: 'number', semanticRole: 'pivot-total' },
    ],
    headerRows: [
      {
        rowKind: ReportRowKind.Header,
        cells: [
          { display: 'Property', value: 'Property', rowSpan: 2 },
          { display: 'Portfolio Manager', value: 'Portfolio Manager', rowSpan: 2 },
          { display: 'Occupancy Snapshot', value: 'Occupancy Snapshot', colSpan: 4 },
        ],
      },
      {
        rowKind: ReportRowKind.Header,
        cells: [
          { display: 'Units', value: 'Units' },
          { display: 'Occupied', value: 'Occupied' },
          { display: 'Vacant', value: 'Vacant' },
          { display: 'Total', value: 'Total' },
        ],
      },
    ],
    rows: [
      {
        rowKind: ReportRowKind.Detail,
        cells: [
          { display: 'Riverfront Tower', value: 'Riverfront Tower', valueType: 'string' },
          { display: 'Alex Carter', value: 'Alex Carter', valueType: 'string' },
          { display: '24', value: 24, valueType: 'decimal' },
          { display: '19', value: 19, valueType: 'decimal' },
          { display: '5', value: 5, valueType: 'decimal' },
          { display: '79.17', value: 79.17, valueType: 'decimal' },
        ],
      },
      {
        rowKind: ReportRowKind.Detail,
        cells: [
          { display: 'Harbor View Plaza', value: 'Harbor View Plaza', valueType: 'string' },
          { display: 'Jordan Reed', value: 'Jordan Reed', valueType: 'string' },
          { display: '18', value: 18, valueType: 'decimal' },
          { display: '16', value: 16, valueType: 'decimal' },
          { display: '2', value: 2, valueType: 'decimal' },
          { display: '88.89', value: 88.89, valueType: 'decimal' },
        ],
      },
      {
        rowKind: ReportRowKind.Total,
        cells: [
          { display: 'Portfolio Total', value: 'Portfolio Total', valueType: 'string' },
          { display: '2 managers', value: '2 managers', valueType: 'string' },
          { display: '42', value: 42, valueType: 'decimal' },
          { display: '35', value: 35, valueType: 'decimal' },
          { display: '7', value: 7, valueType: 'decimal' },
          { display: '83.33', value: 83.33, valueType: 'decimal' },
        ],
      },
    ],
    meta: {
      title: 'Occupancy Summary',
      subtitle: 'Portfolio view',
      isPivot: true,
      hasRowOutline: false,
      hasColumnGroups: true,
    },
  },
  offset: 0,
  limit: 100,
  total: 3,
  hasMore: false,
}

export const occupancySummaryReportEmptyExecutionFixture: ReportExecutionResponseDto = {
  sheet: {
    columns: occupancySummaryReportExecutionFixture.sheet.columns,
    headerRows: occupancySummaryReportExecutionFixture.sheet.headerRows,
    rows: [],
    meta: occupancySummaryReportExecutionFixture.sheet.meta,
  },
  offset: 0,
  limit: 100,
  total: 0,
  hasMore: false,
}

export const occupancySummaryReportPagedFirstExecutionFixture: ReportExecutionResponseDto = {
  sheet: {
    columns: occupancySummaryReportExecutionFixture.sheet.columns,
    headerRows: occupancySummaryReportExecutionFixture.sheet.headerRows,
    rows: [occupancySummaryReportExecutionFixture.sheet.rows[0]!],
    meta: occupancySummaryReportExecutionFixture.sheet.meta,
  },
  offset: 0,
  limit: 1,
  total: 2,
  hasMore: true,
  nextCursor: 'cursor:page:2',
}

export const occupancySummaryReportPagedSecondExecutionFixture: ReportExecutionResponseDto = {
  sheet: {
    columns: occupancySummaryReportExecutionFixture.sheet.columns,
    headerRows: occupancySummaryReportExecutionFixture.sheet.headerRows,
    rows: [occupancySummaryReportExecutionFixture.sheet.rows[1]!],
    meta: occupancySummaryReportExecutionFixture.sheet.meta,
  },
  offset: 1,
  limit: 1,
  total: 2,
  hasMore: false,
  nextCursor: null,
}
