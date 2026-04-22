import { describe, expect, it } from 'vitest'

import {
  ReportAggregationKind,
  ReportExecutionMode,
  ReportFieldKind,
  ReportRowKind,
  ReportSortDirection,
  ReportTimeGrain,
} from '../../../../src/ngb/reporting/types'
import type { ReportDefinitionDto, ReportExecutionResponseDto } from '../../../../src/ngb/reporting/types'

describe('reporting types', () => {
  it('publishes stable reporting enums and supports definition and execution contracts', () => {
    const definition: ReportDefinitionDto = {
      reportCode: 'pm.occupancy.summary',
      name: 'Occupancy Summary',
      mode: ReportExecutionMode.Composable,
      dataset: {
        datasetCode: 'occupancy',
        fields: [
          {
            code: 'property',
            label: 'Property',
            dataType: 'reference',
            kind: ReportFieldKind.Dimension,
          },
        ],
        measures: [
          {
            code: 'occupied_units',
            label: 'Occupied units',
            dataType: 'number',
            supportedAggregations: [ReportAggregationKind.Sum],
          },
        ],
      },
      defaultLayout: {
        rowGroups: [
          {
            fieldCode: 'property',
            timeGrain: null,
          },
        ],
        measures: [
          {
            measureCode: 'occupied_units',
            aggregation: ReportAggregationKind.Sum,
          },
        ],
        sorts: [
          {
            fieldCode: 'property',
            direction: ReportSortDirection.Asc,
          },
        ],
      },
    }
    const execution: ReportExecutionResponseDto = {
      sheet: {
        columns: [
          {
            code: 'property',
            title: 'Property',
            dataType: 'string',
          },
        ],
        rows: [
          {
            rowKind: ReportRowKind.Detail,
            cells: [
              {
                display: 'Riverfront Tower',
              },
            ],
          },
        ],
      },
      offset: 0,
      limit: 100,
      total: 1,
      hasMore: false,
    }

    expect(ReportAggregationKind.CountDistinct).toBe(6)
    expect(ReportExecutionMode.Composable).toBe(2)
    expect(ReportTimeGrain.Month).toBe(3)
    expect(definition.dataset?.measures?.[0]?.supportedAggregations).toEqual([ReportAggregationKind.Sum])
    expect(execution.sheet.rows[0]?.rowKind).toBe(ReportRowKind.Detail)
  })
})
