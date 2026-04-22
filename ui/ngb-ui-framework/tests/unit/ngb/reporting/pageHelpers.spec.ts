import { describe, expect, it } from 'vitest'

import {
  canAutoRunReport,
  chooseAvailableVariantCode,
  hasFilterStateValue,
  isInlineDateParameterDataType,
  normalizeParameterDataType,
  normalizeReportDateValue,
  parameterLabel,
  tryResolveOptionLabel,
} from '../../../../src/ngb/reporting/pageHelpers'
import { ReportExecutionMode, ReportFieldKind, type ReportComposerDraft, type ReportDefinitionDto } from '../../../../src/ngb/reporting/types'

const definition: ReportDefinitionDto = {
  reportCode: 'pm.occupancy.summary',
  name: 'Occupancy Summary',
  mode: ReportExecutionMode.Composable,
  dataset: {
    datasetCode: 'pm.occupancy.summary',
    fields: [
      {
        code: 'property',
        label: 'Property',
        dataType: 'String',
        kind: ReportFieldKind.Attribute,
      },
    ],
    measures: [
      {
        code: 'occupied_units',
        label: 'Occupied Units',
        dataType: 'Decimal',
        supportedAggregations: [1],
      },
    ],
  },
  defaultLayout: {
    measures: [
      { measureCode: 'occupied_units', aggregation: 1 },
    ],
  },
  parameters: [
    {
      code: 'as_of_utc',
      label: 'As of',
      dataType: 'DateOnly',
      isRequired: true,
    },
  ],
  filters: [
    {
      fieldCode: 'property',
      label: 'Property',
      dataType: 'Guid',
      isRequired: true,
      lookup: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
    },
    {
      fieldCode: 'status',
      label: 'Status',
      dataType: 'String',
      options: [
        { value: 'open', label: 'Open' },
        { value: 'posted', label: 'Posted' },
      ],
    },
  ],
}

function draft(overrides?: Partial<ReportComposerDraft>): ReportComposerDraft {
  return {
    parameters: {
      as_of_utc: '',
      ...(overrides?.parameters ?? {}),
    },
    filters: {
      property: {
        raw: '',
        items: [],
        includeDescendants: false,
      },
      status: {
        raw: '',
        items: [],
        includeDescendants: false,
      },
      ...(overrides?.filters ?? {}),
    },
    rowGroups: [],
    columnGroups: [],
    measures: [
      {
        measureCode: 'occupied_units',
        aggregation: 1,
        labelOverride: null,
      },
    ],
    detailFields: [],
    sorts: [],
    showDetails: true,
    showSubtotals: true,
    showSubtotalsOnSeparateRows: false,
    showGrandTotals: true,
    ...overrides,
  }
}

describe('reporting page helpers', () => {
  it('detects whether a filter state has a usable value', () => {
    expect(hasFilterStateValue(definition.filters![0], draft())).toBe(false)
    expect(hasFilterStateValue(definition.filters![0], draft({
      filters: {
        property: {
          raw: '',
          items: [{ id: 'property-1', label: 'Riverfront Tower' }],
          includeDescendants: false,
        },
      },
    }))).toBe(true)
    expect(hasFilterStateValue(definition.filters![1], draft({
      filters: {
        status: {
          raw: 'posted',
          items: [],
          includeDescendants: false,
        },
      },
    }))).toBe(true)
  })

  it('allows auto-run only when required parameters and filters are present', () => {
    expect(canAutoRunReport(definition, draft())).toBe(false)
    expect(canAutoRunReport(definition, draft({
      parameters: { as_of_utc: '2026-04-08' },
    }))).toBe(false)
    expect(canAutoRunReport(definition, draft({
      parameters: { as_of_utc: '2026-04-08' },
      filters: {
        property: {
          raw: '',
          items: [{ id: 'property-1', label: 'Riverfront Tower' }],
          includeDescendants: false,
        },
      },
    }))).toBe(true)
  })

  it('normalizes date-oriented parameter metadata and values', () => {
    expect(normalizeParameterDataType('Date Time (UTC)')).toBe('date_time_utc_')
    expect(isInlineDateParameterDataType('Date Only')).toBe(true)
    expect(isInlineDateParameterDataType('String')).toBe(false)
    expect(normalizeReportDateValue('2026-04-08')).toBe('2026-04-08')
    expect(parameterLabel({ code: 'as_of_utc', label: 'As of' })).toBe('As of')
    expect(parameterLabel({ code: 'as_of_utc', label: '' })).toBe('as_of_utc')
  })

  it('resolves option labels and generates unique variant codes', () => {
    expect(tryResolveOptionLabel(definition.filters![1], 'open')).toBe('Open')
    expect(tryResolveOptionLabel(definition.filters![1], 'unknown')).toBe('unknown')
    expect(chooseAvailableVariantCode('Desk View', [
      { variantCode: 'desk-view' },
      { variantCode: 'desk-view-2' },
    ] as Array<{ variantCode: string } & Record<string, unknown>>)).toBe('desk-view-3')
    expect(chooseAvailableVariantCode('Desk View', [
      { variantCode: 'desk-view' },
    ] as Array<{ variantCode: string } & Record<string, unknown>>, 'desk-view')).toBe('desk-view')
  })
})
