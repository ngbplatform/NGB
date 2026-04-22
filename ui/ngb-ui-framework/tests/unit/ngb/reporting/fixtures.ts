import {
  ReportExecutionMode,
  ReportFieldKind,
  ReportSortDirection,
  ReportTimeGrain,
  type ReportDefinitionDto,
} from '../../../../src/ngb/reporting/types'

export function createReportDefinition(): ReportDefinitionDto {
  return {
    reportCode: 'pm.test_report',
    name: 'Test Report',
    group: 'Testing',
    description: 'A report definition used by unit tests.',
    mode: ReportExecutionMode.Composable,
    dataset: {
      datasetCode: 'pm.test_dataset',
      fields: [
        {
          code: 'property',
          label: 'Property',
          dataType: 'string',
          kind: ReportFieldKind.Attribute,
          isGroupable: true,
          isSortable: true,
          isSelectable: true,
        },
        {
          code: 'period',
          label: 'Period',
          dataType: 'date',
          kind: ReportFieldKind.Time,
          isGroupable: true,
          isSortable: true,
          isSelectable: true,
          supportedTimeGrains: [ReportTimeGrain.Month, ReportTimeGrain.Quarter],
        },
        {
          code: 'tenant',
          label: 'Tenant',
          dataType: 'string',
          kind: ReportFieldKind.Detail,
          isGroupable: true,
          isSortable: true,
          isSelectable: true,
        },
      ],
      measures: [
        {
          code: 'amount',
          label: 'Amount',
          dataType: 'decimal',
          supportedAggregations: [1, 4],
        },
        {
          code: 'units',
          label: 'Units',
          dataType: 'int',
          supportedAggregations: [5],
        },
      ],
    },
    capabilities: {
      allowsShowDetails: true,
      allowsSubtotals: true,
      allowsSeparateRowSubtotals: true,
      allowsGrandTotals: true,
      allowsVariants: true,
      allowsXlsxExport: true,
    },
    defaultLayout: {
      rowGroups: [
        {
          fieldCode: 'property',
        },
      ],
      measures: [],
      detailFields: ['tenant'],
      sorts: [
        {
          fieldCode: 'property',
          direction: ReportSortDirection.Asc,
        },
      ],
      showDetails: true,
      showSubtotals: true,
      showSubtotalsOnSeparateRows: false,
      showGrandTotals: true,
    },
    parameters: [
      { code: 'from_utc', dataType: 'date', isRequired: false },
      { code: 'to_utc', dataType: 'date', isRequired: false },
      { code: 'as_of_utc', dataType: 'date', isRequired: false },
      { code: 'period', dataType: 'date', isRequired: false },
      { code: 'custom', dataType: 'string', isRequired: false, defaultValue: '  custom default  ' },
    ],
    filters: [
      {
        fieldCode: 'property_id',
        label: 'Property',
        dataType: 'guid',
        isMulti: true,
        defaultIncludeDescendants: true,
      },
      {
        fieldCode: 'status',
        label: 'Status',
        dataType: 'string',
        isMulti: false,
      },
    ],
  }
}
