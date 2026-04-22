import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  buildExportRequest,
  buildExecutionRequest,
  createComposerDraft,
  normalizeComposerDraft,
} from '../../../../src/ngb/reporting/composer'
import {
  ReportAggregationKind,
  ReportSortDirection,
  ReportTimeGrain,
  type ReportComposerDraft,
} from '../../../../src/ngb/reporting/types'
import { createReportDefinition } from './fixtures'

function createEmptyDraft(): ReportComposerDraft {
  return {
    parameters: {},
    filters: {
      property_id: { raw: '', items: [], includeDescendants: true },
      status: { raw: '', items: [], includeDescendants: false },
    },
    rowGroups: [],
    columnGroups: [],
    measures: [],
    detailFields: [],
    sorts: [],
    showDetails: false,
    showSubtotals: false,
    showSubtotalsOnSeparateRows: false,
    showGrandTotals: false,
  }
}

describe('report composer helpers', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('creates a draft with default parameters, filter flags, and a fallback measure', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-08T12:00:00Z'))

    const definition = createReportDefinition()
    const draft = createComposerDraft(definition)

    expect(draft.parameters).toEqual({
      from_utc: '2026-04-01',
      to_utc: '2026-04-08',
      as_of_utc: '2026-04-08',
      period: '2026-04-01',
      custom: 'custom default',
    })
    expect(draft.filters.property_id.includeDescendants).toBe(true)
    expect(draft.filters.status.includeDescendants).toBe(false)
    expect(draft.measures).toEqual([
      {
        measureCode: 'amount',
        aggregation: ReportAggregationKind.Sum,
        labelOverride: null,
      },
    ])
    expect(draft.rowGroups).toEqual([
      {
        fieldCode: 'property',
        groupKey: null,
        timeGrain: null,
      },
    ])
  })

  it('normalizes drafts by stripping rogue parameters, deduping details, coercing sorts, and enforcing capabilities', () => {
    const baseDefinition = createReportDefinition()
    const definition = {
      ...baseDefinition,
      capabilities: {
        ...baseDefinition.capabilities,
        allowsShowDetails: false,
        allowsSubtotals: false,
        allowsGrandTotals: false,
      },
    }

    const draft: ReportComposerDraft = {
      ...createEmptyDraft(),
      parameters: {
        from_utc: '2026-04-01',
        rogue: 'drop me',
      },
      rowGroups: [
        {
          fieldCode: 'period',
          groupKey: null,
          timeGrain: ReportTimeGrain.Month,
        },
      ],
      measures: [
        {
          measureCode: 'amount',
          aggregation: ReportAggregationKind.Sum,
          labelOverride: null,
        },
      ],
      detailFields: ['tenant', 'tenant'],
      sorts: [
        {
          fieldCode: 'period',
          groupKey: null,
          appliesToColumnAxis: true,
          direction: ReportSortDirection.Desc,
          timeGrain: ReportTimeGrain.Year,
        },
        {
          fieldCode: 'tenant',
          groupKey: null,
          appliesToColumnAxis: false,
          direction: 99 as ReportSortDirection,
          timeGrain: null,
        },
        {
          fieldCode: 'unknown',
          groupKey: null,
          appliesToColumnAxis: false,
          direction: ReportSortDirection.Asc,
          timeGrain: null,
        },
      ],
      showDetails: true,
      showSubtotals: true,
      showSubtotalsOnSeparateRows: true,
      showGrandTotals: true,
    }

    const normalized = normalizeComposerDraft(definition, draft)

    expect(normalized.parameters).toEqual({
      from_utc: '2026-04-01',
    })
    expect(normalized.detailFields).toEqual(['tenant'])
    expect(normalized.sorts).toEqual([
      {
        fieldCode: 'period',
        groupKey: null,
        appliesToColumnAxis: false,
        direction: ReportSortDirection.Desc,
        timeGrain: ReportTimeGrain.Month,
      },
      {
        fieldCode: 'tenant',
        groupKey: null,
        appliesToColumnAxis: false,
        direction: ReportSortDirection.Asc,
        timeGrain: null,
      },
    ])
    expect(normalized.showDetails).toBe(false)
    expect(normalized.showSubtotals).toBe(false)
    expect(normalized.showSubtotalsOnSeparateRows).toBe(false)
    expect(normalized.showGrandTotals).toBe(false)
  })

  it('builds execution requests with trimmed parameters, normalized filters, and normalized layout metadata', () => {
    const definition = createReportDefinition()
    const draft: ReportComposerDraft = {
      ...createEmptyDraft(),
      parameters: {
        from_utc: ' 2026-04-01 ',
        to_utc: '   ',
      },
      filters: {
        property_id: {
          raw: 'property-1, property-2, property-1',
          items: [],
          includeDescendants: true,
        },
        status: {
          raw: 'open',
          items: [{ id: 'posted', label: 'Posted' }],
          includeDescendants: false,
        },
      },
      rowGroups: [
        {
          fieldCode: 'period',
          groupKey: null,
          timeGrain: ReportTimeGrain.Quarter,
        },
      ],
      measures: [
        {
          measureCode: 'amount',
          aggregation: ReportAggregationKind.Average,
          labelOverride: 'Amount (Average)',
        },
      ],
      detailFields: ['tenant', 'tenant'],
      sorts: [
        {
          fieldCode: 'period',
          groupKey: null,
          appliesToColumnAxis: true,
          direction: ReportSortDirection.Desc,
          timeGrain: ReportTimeGrain.Year,
        },
        {
          fieldCode: 'tenant',
          groupKey: null,
          appliesToColumnAxis: false,
          direction: ReportSortDirection.Desc,
          timeGrain: null,
        },
      ],
      showDetails: true,
      showSubtotals: true,
      showSubtotalsOnSeparateRows: false,
      showGrandTotals: true,
    }

    const request = buildExecutionRequest(definition, draft)

    expect(request.parameters).toEqual({
      from_utc: '2026-04-01',
    })
    expect(request.filters).toEqual({
      property_id: {
        value: ['property-1', 'property-2'],
        includeDescendants: true,
      },
      status: {
        value: 'posted',
        includeDescendants: false,
      },
    })
    expect(request.layout?.rowGroups).toEqual([
      {
        fieldCode: 'period',
        groupKey: undefined,
        timeGrain: ReportTimeGrain.Quarter,
      },
    ])
    expect(request.layout?.sorts).toEqual([
      {
        fieldCode: 'period',
        direction: ReportSortDirection.Desc,
        appliesToColumnAxis: undefined,
        groupKey: undefined,
        timeGrain: ReportTimeGrain.Quarter,
      },
      {
        fieldCode: 'tenant',
        direction: ReportSortDirection.Desc,
        appliesToColumnAxis: undefined,
        groupKey: undefined,
        timeGrain: undefined,
      },
    ])
    expect(request.layout?.measures?.[0]).toMatchObject({
      measureCode: 'amount',
      aggregation: ReportAggregationKind.Average,
    })
    expect(request.layout?.measures?.[0]?.labelOverride).toBe('Amount (Average)')
    expect(request.offset).toBe(0)
    expect(request.limit).toBe(500)
  })

  it('builds export requests without paging fields', () => {
    const definition = createReportDefinition()
    const draft: ReportComposerDraft = {
      ...createEmptyDraft(),
      parameters: {
        from_utc: '2026-04-01',
      },
      filters: {
        property_id: {
          raw: '',
          items: [{ id: 'property-1', label: 'Property 1' }],
          includeDescendants: true,
        },
        status: {
          raw: 'open',
          items: [],
          includeDescendants: false,
        },
      },
    }

    const request = buildExportRequest(definition, draft)

    expect(request.parameters).toEqual({
      from_utc: '2026-04-01',
    })
    expect(request.filters).toEqual({
      property_id: {
        value: ['property-1'],
        includeDescendants: true,
      },
      status: {
        value: 'open',
        includeDescendants: false,
      },
    })
    expect(request).not.toHaveProperty('offset')
    expect(request).not.toHaveProperty('limit')
    expect(request).not.toHaveProperty('cursor')
  })
})
