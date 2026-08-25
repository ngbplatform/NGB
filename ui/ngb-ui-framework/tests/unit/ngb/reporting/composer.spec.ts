import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  applyExecutionRequestToDraft,
  applyVariantToDraft,
  aggregationLabel,
  buildAutoMeasureLabel,
  buildExportRequest,
  buildExecutionRequest,
  buildVariantDto,
  cloneComposerDraft,
  coerceReportAggregationKind,
  coerceReportComposerLookupItem,
  coerceReportComposerLookupItems,
  coerceReportSortDirection,
  coerceReportTimeGrain,
  createComposerDraft,
  getAggregationOptions,
  getGroupableFields,
  getMeasureOptions,
  getReportComposerFilterState,
  getSelectableFields,
  getSelectedReportComposerFilterItem,
  getSortableFields,
  getTimeGrainOptions,
  normalizeComposerDraft,
  resolveDefaultAggregation,
  resolveMeasureLabel,
  slugifyVariantCode,
  sortDirectionOptions,
  timeGrainLabel,
} from '../../../../src/ngb/reporting/composer'
import {
  ReportAggregationKind,
  ReportFieldKind,
  ReportSortDirection,
  ReportTimeGrain,
  type ReportComposerDraft,
  type ReportDefinitionDto,
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

  it('coerces lookup items and supplies safe filter state defaults', () => {
    const draft = createEmptyDraft()
    draft.filters.status.items = [{ id: 'open', label: 'Open', meta: 'Active' }]

    expect(getReportComposerFilterState(draft, {
      fieldCode: 'status',
      defaultIncludeDescendants: true,
    })).toBe(draft.filters.status)
    expect(getReportComposerFilterState(null, {
      fieldCode: 'missing',
      defaultIncludeDescendants: true,
    })).toEqual({ raw: '', items: [], includeDescendants: true })
    expect(getSelectedReportComposerFilterItem(draft, {
      fieldCode: 'status',
      defaultIncludeDescendants: false,
    })).toEqual({ id: 'open', label: 'Open', meta: 'Active' })
    expect(getSelectedReportComposerFilterItem(undefined, {
      fieldCode: 'missing',
      defaultIncludeDescendants: false,
    })).toBeNull()

    expect(coerceReportComposerLookupItem(null)).toBeNull()
    expect(coerceReportComposerLookupItem('item')).toBeNull()
    expect(coerceReportComposerLookupItem({})).toBeNull()
    expect(coerceReportComposerLookupItem({ id: '  account-1  ' })).toEqual({
      id: 'account-1',
      label: 'account-1',
      meta: undefined,
    })
    expect(coerceReportComposerLookupItem({
      id: 42,
      label: '  Answer  ',
      meta: '  Meaning  ',
    })).toEqual({ id: '42', label: 'Answer', meta: 'Meaning' })
    expect(coerceReportComposerLookupItems('not-an-array')).toEqual([])
    expect(coerceReportComposerLookupItems([
      null,
      { id: '' },
      { id: 'one', label: 'One' },
    ])).toEqual([{ id: 'one', label: 'One', meta: undefined }])
  })

  it('coerces every time grain, sort direction, and aggregation representation', () => {
    for (const grain of [
      ReportTimeGrain.Day,
      ReportTimeGrain.Week,
      ReportTimeGrain.Month,
      ReportTimeGrain.Quarter,
      ReportTimeGrain.Year,
    ]) {
      expect(coerceReportTimeGrain(grain)).toBe(grain)
      expect(coerceReportTimeGrain(String(grain))).toBe(grain)
    }
    expect(coerceReportTimeGrain('day')).toBe(ReportTimeGrain.Day)
    expect(coerceReportTimeGrain('week')).toBe(ReportTimeGrain.Week)
    expect(coerceReportTimeGrain('month')).toBe(ReportTimeGrain.Month)
    expect(coerceReportTimeGrain('quarter')).toBe(ReportTimeGrain.Quarter)
    expect(coerceReportTimeGrain('year')).toBe(ReportTimeGrain.Year)
    expect(coerceReportTimeGrain(99)).toBeNull()
    expect(coerceReportTimeGrain(null)).toBeNull()

    expect(coerceReportSortDirection(ReportSortDirection.Asc)).toBe(ReportSortDirection.Asc)
    expect(coerceReportSortDirection(ReportSortDirection.Desc)).toBe(ReportSortDirection.Desc)
    expect(coerceReportSortDirection('1')).toBe(ReportSortDirection.Asc)
    expect(coerceReportSortDirection('asc')).toBe(ReportSortDirection.Asc)
    expect(coerceReportSortDirection('ascending')).toBe(ReportSortDirection.Asc)
    expect(coerceReportSortDirection('2')).toBe(ReportSortDirection.Desc)
    expect(coerceReportSortDirection('desc')).toBe(ReportSortDirection.Desc)
    expect(coerceReportSortDirection('descending')).toBe(ReportSortDirection.Desc)
    expect(coerceReportSortDirection(99)).toBeNull()
    expect(coerceReportSortDirection(null)).toBeNull()

    const aggregationCases: Array<[unknown, ReportAggregationKind]> = [
      [1, ReportAggregationKind.Sum],
      [2, ReportAggregationKind.Min],
      [3, ReportAggregationKind.Max],
      [4, ReportAggregationKind.Average],
      [5, ReportAggregationKind.Count],
      [6, ReportAggregationKind.CountDistinct],
      [7, ReportAggregationKind.First],
      [8, ReportAggregationKind.Last],
      ['sum', ReportAggregationKind.Sum],
      ['min', ReportAggregationKind.Min],
      ['max', ReportAggregationKind.Max],
      ['average', ReportAggregationKind.Average],
      ['avg', ReportAggregationKind.Average],
      ['count', ReportAggregationKind.Count],
      ['countdistinct', ReportAggregationKind.CountDistinct],
      ['count_distinct', ReportAggregationKind.CountDistinct],
      ['count distinct', ReportAggregationKind.CountDistinct],
      ['first', ReportAggregationKind.First],
      ['last', ReportAggregationKind.Last],
    ]
    for (const [value, expected] of aggregationCases) {
      expect(coerceReportAggregationKind(value)).toBe(expected)
      expect(coerceReportAggregationKind(String(expected))).toBe(expected)
    }
    expect(coerceReportAggregationKind(99)).toBeNull()
    expect(coerceReportAggregationKind(null)).toBeNull()
  })

  it('builds option lists and labels for every supported enum value', () => {
    const definition = createReportDefinition()
    const allGrains = [
      ReportTimeGrain.Day,
      ReportTimeGrain.Week,
      ReportTimeGrain.Month,
      ReportTimeGrain.Quarter,
      ReportTimeGrain.Year,
    ]
    const field = {
      ...definition.dataset!.fields![1]!,
      supportedTimeGrains: [...allGrains, ReportTimeGrain.Month, 99 as ReportTimeGrain],
    }
    expect(getTimeGrainOptions(field).map(option => option.value)).toEqual(allGrains)
    expect(getTimeGrainOptions(null)).toEqual([])
    expect(allGrains.map(timeGrainLabel)).toEqual(['Day', 'Week', 'Month', 'Quarter', 'Year'])
    expect(timeGrainLabel(null)).toBe('None')

    const allAggregations = [
      ReportAggregationKind.Sum,
      ReportAggregationKind.Min,
      ReportAggregationKind.Max,
      ReportAggregationKind.Average,
      ReportAggregationKind.Count,
      ReportAggregationKind.CountDistinct,
      ReportAggregationKind.First,
      ReportAggregationKind.Last,
    ]
    expect(allAggregations.map(aggregationLabel)).toEqual([
      'Sum', 'Min', 'Max', 'Average', 'Count', 'Count Distinct', 'First', 'Last',
    ])
    expect(aggregationLabel(99 as ReportAggregationKind)).toBe('99')

    const customDefinition: ReportDefinitionDto = {
      ...definition,
      dataset: {
        ...definition.dataset!,
        measures: [{
          code: 'all',
          label: 'All',
          dataType: 'number',
          supportedAggregations: [...allAggregations, ReportAggregationKind.Sum, 99 as ReportAggregationKind],
        }],
      },
    }
    expect(getAggregationOptions(customDefinition, 'all').map(option => option.value)).toEqual(allAggregations)
    expect(getAggregationOptions(customDefinition, 'missing')).toEqual([{ value: ReportAggregationKind.Sum, label: 'Sum' }])
    expect(sortDirectionOptions()).toEqual([
      { value: ReportSortDirection.Asc, label: 'Ascending' },
      { value: ReportSortDirection.Desc, label: 'Descending' },
    ])

    expect(getGroupableFields(definition).map(field => field.code)).toEqual(['property', 'period', 'tenant'])
    expect(getSortableFields(definition).map(field => field.code)).toEqual(['property', 'period', 'tenant'])
    expect(getSelectableFields(definition).map(field => field.code)).toEqual(['property', 'period', 'tenant'])
    expect(getMeasureOptions(definition).map(measure => measure.code)).toEqual(['amount', 'units'])
    const emptyDefinition: ReportDefinitionDto = { reportCode: 'empty', name: 'Empty' }
    expect(getGroupableFields(emptyDefinition)).toEqual([])
    expect(getSortableFields(emptyDefinition)).toEqual([])
    expect(getSelectableFields(emptyDefinition)).toEqual([])
    expect(getMeasureOptions(emptyDefinition)).toEqual([])
  })

  it('resolves default aggregations and automatic or explicit measure labels', () => {
    const definition = createReportDefinition()
    expect(resolveDefaultAggregation(definition, 'units')).toBe(ReportAggregationKind.Count)
    expect(resolveDefaultAggregation(definition, 'amount')).toBe(ReportAggregationKind.Sum)
    expect(resolveDefaultAggregation(definition, 'missing')).toBe(ReportAggregationKind.Sum)

    const withoutSum: ReportDefinitionDto = {
      ...definition,
      dataset: {
        ...definition.dataset!,
        measures: [{
          code: 'range',
          label: 'Range',
          dataType: 'number',
          supportedAggregations: [ReportAggregationKind.Min, ReportAggregationKind.Max],
        }],
      },
    }
    expect(resolveDefaultAggregation(withoutSum, 'range')).toBe(ReportAggregationKind.Min)
    expect(buildAutoMeasureLabel(definition, 'amount', ReportAggregationKind.Sum)).toBe('Amount')
    expect(buildAutoMeasureLabel(definition, 'amount', ReportAggregationKind.Average)).toBe('Amount (Average)')
    expect(buildAutoMeasureLabel(definition, 'missing', null)).toBe('missing')
    expect(resolveMeasureLabel(definition, {
      measureCode: 'amount',
      aggregation: ReportAggregationKind.Sum,
      labelOverride: '  Net Amount  ',
    })).toBe('Net Amount')
    expect(resolveMeasureLabel(definition, {
      measureCode: 'amount',
      aggregation: ReportAggregationKind.Average,
      labelOverride: ' ',
    })).toBe('Amount (Average)')
  })

  it('deep-clones composer state and normalizes keyed axis sorts and duplicate details', () => {
    const definition = createReportDefinition()
    const draft: ReportComposerDraft = {
      ...createEmptyDraft(),
      parameters: { custom: 'value' },
      filters: {
        status: {
          raw: 'open',
          includeDescendants: false,
          items: [{ id: 'open', label: 'Open', meta: 'state' }],
        },
      },
      rowGroups: [
        { fieldCode: ' period ', groupKey: ' row-period ', timeGrain: ReportTimeGrain.Month },
        { fieldCode: 'period', groupKey: 'row-quarter', timeGrain: ReportTimeGrain.Quarter },
        { fieldCode: ' ', groupKey: null, timeGrain: null },
      ],
      columnGroups: [
        { fieldCode: 'period', groupKey: 'column-period', timeGrain: ReportTimeGrain.Year },
      ],
      measures: [
        { measureCode: ' amount ', aggregation: 99 as ReportAggregationKind, labelOverride: 'Amount' },
        { measureCode: ' ', aggregation: null, labelOverride: 'Discarded blank measure' },
      ],
      detailFields: [' tenant ', '', 'tenant'],
      sorts: [
        { fieldCode: 'period', groupKey: 'row-period', appliesToColumnAxis: true, direction: ReportSortDirection.Desc, timeGrain: ReportTimeGrain.Year },
        { fieldCode: 'period', groupKey: 'row-period', appliesToColumnAxis: false, direction: ReportSortDirection.Asc, timeGrain: ReportTimeGrain.Month },
        { fieldCode: 'period', groupKey: 'column-period', appliesToColumnAxis: false, direction: ReportSortDirection.Asc, timeGrain: null },
        { fieldCode: 'tenant', groupKey: null, appliesToColumnAxis: false, direction: ReportSortDirection.Desc, timeGrain: null },
      ],
      showDetails: true,
      showSubtotals: true,
      showSubtotalsOnSeparateRows: true,
      showGrandTotals: true,
    }

    const cloned = cloneComposerDraft(draft)
    expect(cloned).toEqual(draft)
    expect(cloned).not.toBe(draft)
    expect(cloned.filters.status).not.toBe(draft.filters.status)
    expect(cloned.filters.status!.items[0]).not.toBe(draft.filters.status!.items[0])

    const normalized = normalizeComposerDraft(definition, draft)
    expect(normalized.rowGroups).toHaveLength(2)
    expect(normalized.measures).toEqual([{
      measureCode: 'amount',
      aggregation: ReportAggregationKind.Sum,
      labelOverride: null,
    }])
    expect(normalized.detailFields).toEqual(['tenant'])
    expect(normalized.sorts).toEqual([
      {
        fieldCode: 'period',
        groupKey: 'row-period',
        appliesToColumnAxis: false,
        direction: ReportSortDirection.Desc,
        timeGrain: ReportTimeGrain.Month,
      },
      {
        fieldCode: 'period',
        groupKey: 'column-period',
        appliesToColumnAxis: true,
        direction: ReportSortDirection.Asc,
        timeGrain: null,
      },
      {
        fieldCode: 'tenant',
        groupKey: null,
        appliesToColumnAxis: false,
        direction: ReportSortDirection.Desc,
        timeGrain: null,
      },
    ])
  })

  it('normalizes unkeyed row/column grouping sort fallbacks and removes duplicate sort keys', () => {
    const definition = createReportDefinition()
    const normalized = normalizeComposerDraft(definition, {
      ...createEmptyDraft(),
      rowGroups: [
        { fieldCode: 'period', groupKey: null, timeGrain: ReportTimeGrain.Month },
        { fieldCode: 'property', groupKey: null, timeGrain: null },
      ],
      columnGroups: [
        { fieldCode: 'period', groupKey: null, timeGrain: ReportTimeGrain.Quarter },
        { fieldCode: 'tenant', groupKey: null, timeGrain: null },
      ],
      detailFields: ['property'],
      sorts: [
        { fieldCode: 'period', groupKey: null, appliesToColumnAxis: true, direction: ReportSortDirection.Asc, timeGrain: ReportTimeGrain.Quarter },
        { fieldCode: 'period', groupKey: null, appliesToColumnAxis: true, direction: ReportSortDirection.Desc, timeGrain: ReportTimeGrain.Quarter },
        { fieldCode: 'period', groupKey: null, appliesToColumnAxis: false, direction: ReportSortDirection.Asc, timeGrain: ReportTimeGrain.Month },
        { fieldCode: 'tenant', groupKey: null, appliesToColumnAxis: false, direction: ReportSortDirection.Desc, timeGrain: null },
        { fieldCode: 'property', groupKey: null, appliesToColumnAxis: true, direction: ReportSortDirection.Asc, timeGrain: ReportTimeGrain.Month },
        { fieldCode: 'property', groupKey: null, appliesToColumnAxis: false, direction: ReportSortDirection.Asc, timeGrain: ReportTimeGrain.Month },
        { fieldCode: 'property', groupKey: null, appliesToColumnAxis: false, direction: ReportSortDirection.Desc, timeGrain: ReportTimeGrain.Month },
        { fieldCode: 'tenant', groupKey: null, appliesToColumnAxis: true, direction: ReportSortDirection.Asc, timeGrain: ReportTimeGrain.Month },
      ],
    })

    expect(normalized.sorts).toEqual([
      expect.objectContaining({ fieldCode: 'period', appliesToColumnAxis: true, timeGrain: ReportTimeGrain.Quarter }),
      expect.objectContaining({ fieldCode: 'period', appliesToColumnAxis: false, timeGrain: ReportTimeGrain.Month }),
      expect.objectContaining({ fieldCode: 'tenant', appliesToColumnAxis: true, timeGrain: null }),
      expect.objectContaining({ fieldCode: 'property', appliesToColumnAxis: false, timeGrain: null }),
    ])
  })

  it('builds nullable layout metadata and auto-labels default aggregation measures', () => {
    const definition = createReportDefinition()
    const draft: ReportComposerDraft = {
      ...createEmptyDraft(),
      rowGroups: [{ fieldCode: 'property', groupKey: null, timeGrain: null }],
      columnGroups: [{ fieldCode: 'property', groupKey: null, timeGrain: null }],
      measures: [{ measureCode: 'amount', aggregation: ReportAggregationKind.Sum, labelOverride: null }],
    }

    expect(buildExecutionRequest(definition, draft).layout).toMatchObject({
      rowGroups: [{ fieldCode: 'property', groupKey: undefined, timeGrain: undefined }],
      columnGroups: [{ fieldCode: 'property', groupKey: undefined, timeGrain: undefined }],
      measures: [{ measureCode: 'amount', aggregation: ReportAggregationKind.Sum, labelOverride: undefined }],
    })

    const noDataset: ReportDefinitionDto = { reportCode: 'none', name: 'None' }
    const normalized = normalizeComposerDraft(noDataset, {
      ...createEmptyDraft(),
      rowGroups: [{ fieldCode: 'property', groupKey: null, timeGrain: ReportTimeGrain.Month }],
      measures: [{ measureCode: 'missing', aggregation: null, labelOverride: null }],
    })
    expect(normalized.rowGroups[0]?.timeGrain).toBeNull()
    expect(buildAutoMeasureLabel(noDataset, 'missing', null)).toBe('missing')
  })

  it('creates drafts from full defaults and applies full, empty, and variant requests', () => {
    const base = createReportDefinition()
    const definition: ReportDefinitionDto = {
      ...base,
      parameters: [
        ...(base.parameters ?? []),
        { code: 'unrecognized_parameter', dataType: 'string', isRequired: false },
      ],
      defaultLayout: {
        rowGroups: [{ fieldCode: 'period', groupKey: 'row', timeGrain: ReportTimeGrain.Month }],
        columnGroups: [{ fieldCode: 'period', groupKey: 'column', timeGrain: ReportTimeGrain.Quarter }],
        measures: [
          { measureCode: 'amount', aggregation: ReportAggregationKind.Average, labelOverride: 'Average amount' },
          { measureCode: 'units', aggregation: ReportAggregationKind.Count },
        ],
        detailFields: [],
        sorts: [{
          fieldCode: 'period',
          groupKey: 'column',
          appliesToColumnAxis: true,
          direction: 99 as ReportSortDirection,
          timeGrain: ReportTimeGrain.Quarter,
        }],
        showDetails: false,
        showSubtotals: false,
        showSubtotalsOnSeparateRows: true,
        showGrandTotals: false,
      },
    }
    const created = createComposerDraft(definition)
    expect(created.columnGroups[0]).toMatchObject({ groupKey: 'column', timeGrain: ReportTimeGrain.Quarter })
    expect(created.measures[0]).toMatchObject({ aggregation: ReportAggregationKind.Average, labelOverride: 'Average amount' })
    expect(created.measures[1]).toMatchObject({ aggregation: ReportAggregationKind.Count, labelOverride: null })
    expect(created.parameters.unrecognized_parameter).toBe('')
    expect(buildExecutionRequest(definition, created).layout?.columnGroups).toEqual([
      { fieldCode: 'period', groupKey: 'column', timeGrain: ReportTimeGrain.Quarter },
    ])

    const applied = applyExecutionRequestToDraft(definition, {
      parameters: { custom: '  request value  ', rogue: 'ignored after normalization' },
      filters: {
        property_id: { value: ['one', null, ' two ', ''], includeDescendants: false },
        status: { value: null, includeDescendants: true },
        unknown: { value: 'ignored' },
      },
      layout: {
        rowGroups: [{ fieldCode: 'period', groupKey: 'request-row', timeGrain: 'month' as unknown as ReportTimeGrain }],
        columnGroups: [{ fieldCode: 'property', groupKey: 'request-column' }],
        measures: [{ measureCode: 'amount', aggregation: 99 as ReportAggregationKind }],
        detailFields: ['tenant'],
        sorts: [{ fieldCode: 'tenant', direction: 99 as ReportSortDirection }],
        showDetails: true,
        showSubtotals: true,
        showSubtotalsOnSeparateRows: true,
        showGrandTotals: true,
      },
    })
    expect(applied.parameters.custom).toBe('request value')
    expect(applied.parameters).not.toHaveProperty('rogue')
    expect(applied.filters.property_id.raw).toBe('one, two')
    expect(applied.filters.status.raw).toBe('')
    expect(applied.measures[0]?.aggregation).toBe(ReportAggregationKind.Sum)
    expect(applied.sorts[0]?.direction).toBe(ReportSortDirection.Asc)

    const emptyDefinition: ReportDefinitionDto = {
      reportCode: 'empty',
      name: 'Empty',
      capabilities: {
        allowsShowDetails: true,
        allowsSubtotals: true,
        allowsSeparateRowSubtotals: false,
        allowsGrandTotals: true,
      },
    }
    expect(createComposerDraft(emptyDefinition).measures).toEqual([])
    expect(applyExecutionRequestToDraft(emptyDefinition, { layout: {} }).measures).toEqual([])
    expect(applyExecutionRequestToDraft(base, {}).measures[0]?.measureCode).toBe('amount')
    expect(applyExecutionRequestToDraft(base, { layout: { measures: [] } }).measures[0]?.measureCode).toBe('amount')

    const variantDraft = applyVariantToDraft(base, {
      variantCode: 'saved',
      reportCode: base.reportCode,
      name: 'Saved',
      layout: null,
      filters: null,
      parameters: null,
    })
    expect(variantDraft.measures[0]?.measureCode).toBe('amount')
  })

  it('slugifies variant codes and builds trimmed variant DTOs with option defaults', () => {
    expect(slugifyVariantCode('  My Fiscal-Year View!  ')).toBe('my-fiscal-year-view')
    expect(slugifyVariantCode('---')).toBe('variant')

    const definition = createReportDefinition()
    const dto = buildVariantDto(definition, createEmptyDraft(), {
      variantCode: 'mine',
      name: '  My Variant  ',
      isDefault: true,
      isShared: false,
    })
    expect(dto).toMatchObject({
      variantCode: 'mine',
      reportCode: definition.reportCode,
      name: 'My Variant',
      isDefault: true,
      isShared: false,
    })
    expect(buildVariantDto(definition, createEmptyDraft(), {
      variantCode: 'default-options',
      name: '',
    })).toMatchObject({ isDefault: false, isShared: true })
  })

  it('builds empty requests and skips filter definitions absent from the draft', () => {
    const definition: ReportDefinitionDto = {
      reportCode: 'minimal',
      name: 'Minimal',
      filters: [
        { fieldCode: 'missing', label: 'Missing', dataType: 'string' },
        { fieldCode: 'empty_multi', label: 'Empty multi', dataType: 'string', isMulti: true },
      ],
    }
    const draft: ReportComposerDraft = {
      ...createEmptyDraft(),
      filters: {
        empty_multi: { raw: ' , ', items: [], includeDescendants: false },
      },
    }

    expect(buildExportRequest(definition, draft)).toMatchObject({
      parameters: null,
      filters: null,
    })
    expect(resolveMeasureLabel(createReportDefinition(), {
      measureCode: 'amount',
      aggregation: ReportAggregationKind.Sum,
      labelOverride: null,
    })).toBe('Amount')
    expect(buildExportRequest({ reportCode: 'no-filters', name: 'No filters' }, draft).filters).toBeNull()
  })
})
