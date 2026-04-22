import {
  ReportAggregationKind,
  ReportSortDirection,
  ReportTimeGrain,
  type ReportComposerDraft,
  type ReportComposerFilterState,
  type ReportComposerLookupItem,
  type ReportComposerGroupingState,
  type ReportComposerMeasureState,
  type ReportComposerSortState,
  type ReportDefinitionDto,
  type ReportFieldDto,
  type ReportExportRequestDto,
  type ReportFilterFieldDto,
  type ReportMeasureDto,
  type ReportOptionItem,
  type ReportExecutionRequestDto,
  type ReportFilterValueDto,
  type ReportVariantDto,
} from './types'

function pad2(value: number): string {
  return String(value).padStart(2, '0')
}

function formatDateOnly(date: Date): string {
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`
}

function currentMonthStart(): string {
  const now = new Date()
  return `${now.getFullYear()}-${pad2(now.getMonth() + 1)}-01`
}

function currentDay(): string {
  return formatDateOnly(new Date())
}

function isRangeFromParameter(normalizedCode: string): boolean {
  return normalizedCode === 'from_utc' || normalizedCode.endsWith('.from_utc') || normalizedCode.endsWith('_from')
}

function isRangeToParameter(normalizedCode: string): boolean {
  return normalizedCode === 'to_utc' || normalizedCode.endsWith('.to_utc') || normalizedCode.endsWith('_to')
}

function isAsOfParameter(normalizedCode: string): boolean {
  return normalizedCode === 'as_of_utc' || normalizedCode === 'asof_utc' || normalizedCode.includes('as_of')
}

function isCurrentPeriodParameter(normalizedCode: string): boolean {
  return normalizedCode === 'period_utc' || normalizedCode === 'period'
}

function defaultParameterValue(code: string): string {
  const normalized = code.trim().toLowerCase()
  if (isRangeFromParameter(normalized)) return currentMonthStart()
  if (isRangeToParameter(normalized)) return currentDay()
  if (isAsOfParameter(normalized)) return currentDay()
  if (isCurrentPeriodParameter(normalized)) return currentMonthStart()
  return ''
}

function createEmptyFilterState(includeDescendants: boolean): ReportComposerFilterState {
  return {
    raw: '',
    items: [],
    includeDescendants,
  }
}

export function getReportComposerFilterState(
  draft: Pick<ReportComposerDraft, 'filters'> | null | undefined,
  field: Pick<ReportFilterFieldDto, 'fieldCode' | 'defaultIncludeDescendants'>,
): ReportComposerFilterState {
  return draft?.filters[field.fieldCode] ?? createEmptyFilterState(!!field.defaultIncludeDescendants)
}

export function getSelectedReportComposerFilterItem(
  draft: Pick<ReportComposerDraft, 'filters'> | null | undefined,
  field: Pick<ReportFilterFieldDto, 'fieldCode' | 'defaultIncludeDescendants'>,
): ReportComposerLookupItem | null {
  return getReportComposerFilterState(draft, field).items[0] ?? null
}

export function coerceReportComposerLookupItem(value: unknown): ReportComposerLookupItem | null {
  if (!value || typeof value !== 'object') return null

  const id = String((value as { id?: unknown }).id ?? '').trim()
  if (id.length === 0) return null

  const label = String((value as { label?: unknown }).label ?? '').trim() || id
  const meta = String((value as { meta?: unknown }).meta ?? '').trim()
  return {
    id,
    label,
    meta: meta.length > 0 ? meta : undefined,
  }
}

export function coerceReportComposerLookupItems(value: unknown): ReportComposerLookupItem[] {
  if (!Array.isArray(value)) return []
  return value
    .map((item) => coerceReportComposerLookupItem(item))
    .filter((item): item is ReportComposerLookupItem => item !== null)
}

function scalarFilterToRaw(value: unknown): string {
  if (Array.isArray(value)) return value.map((entry) => String(entry ?? '').trim()).filter((entry) => entry.length > 0).join(', ')
  return String(value ?? '').trim()
}

function normalizeCode(value: unknown): string {
  return String(value ?? '').trim()
}

function normalizeOptionalText(value: unknown): string | null {
  const normalized = normalizeCode(value)
  return normalized.length > 0 ? normalized : null
}

function datasetField(definition: ReportDefinitionDto, fieldCode: string): ReportFieldDto | undefined {
  const normalized = normalizeCode(fieldCode)
  if (normalized.length === 0) return undefined
  return (definition.dataset?.fields ?? []).find((field) => field.code === normalized)
}

function datasetMeasure(definition: ReportDefinitionDto, measureCode: string): ReportMeasureDto | undefined {
  const normalized = normalizeCode(measureCode)
  if (normalized.length === 0) return undefined
  return (definition.dataset?.measures ?? []).find((measure) => measure.code === normalized)
}

function measureBaseLabel(definition: ReportDefinitionDto, measureCode: string): string {
  return datasetMeasure(definition, measureCode)?.label ?? normalizeCode(measureCode)
}

function normalizeGroupingState(definition: ReportDefinitionDto, groups: ReportComposerGroupingState[]): ReportComposerGroupingState[] {
  return groups
    .map((group) => {
      const fieldCode = normalizeCode(group.fieldCode)
      const field = datasetField(definition, fieldCode)
      const supportedTimeGrains = new Set(getTimeGrainOptions(field).map((option) => option.value))
      const timeGrain = normalizeTimeGrainValue(group.timeGrain)

      return {
        groupKey: normalizeOptionalText(group.groupKey),
        fieldCode,
        timeGrain: timeGrain !== null && supportedTimeGrains.has(timeGrain) ? timeGrain : null,
      }
    })
    .filter((group) => group.fieldCode.length > 0)
}

function normalizeMeasureLabelOverride(
  definition: ReportDefinitionDto,
  measureCode: string,
  aggregation: ReportAggregationKind,
  labelOverride: string | null | undefined,
): string | null {
  const trimmed = String(labelOverride ?? '').trim()
  if (trimmed.length === 0) return null
  return trimmed === buildAutoMeasureLabel(definition, measureCode, aggregation) ? null : trimmed
}

function normalizeMeasureState(definition: ReportDefinitionDto, measures: ReportComposerMeasureState[]): ReportComposerMeasureState[] {
  return measures
    .map((measure) => {
      const measureCode = normalizeCode(measure.measureCode)
      const aggregation = normalizeAggregationValue(measure.aggregation) ?? resolveDefaultAggregation(definition, measure.measureCode)
      return {
        measureCode,
        aggregation,
        labelOverride: normalizeMeasureLabelOverride(definition, measureCode, aggregation, measure.labelOverride),
      }
    })
    .filter((measure) => measure.measureCode.length > 0)
}

function buildMeasureLayoutLabelOverride(definition: ReportDefinitionDto, measure: ReportComposerMeasureState): string | null {
  const aggregation = normalizeAggregationValue(measure.aggregation) ?? resolveDefaultAggregation(definition, measure.measureCode)
  const trimmed = String(measure.labelOverride ?? '').trim()
  if (trimmed.length > 0) return trimmed

  const autoLabel = buildAutoMeasureLabel(definition, measure.measureCode, aggregation)
  return autoLabel === measureBaseLabel(definition, measure.measureCode) ? null : autoLabel
}

function normalizeDetailFieldState(detailFields: string[]): string[] {
  return detailFields
    .map((fieldCode) => normalizeCode(fieldCode))
    .filter((fieldCode, index, items) => fieldCode.length > 0 && items.indexOf(fieldCode) === index)
}

function buildSortStateKey(fieldCode: string, timeGrain: ReportTimeGrain | null): string {
  return `${fieldCode}|${timeGrain ?? ''}`
}

function buildAxisSortStateKey(fieldCode: string, timeGrain: ReportTimeGrain | null, appliesToColumnAxis: boolean, groupKey: string | null): string {
  return `${fieldCode}|${appliesToColumnAxis ? 'column' : 'row'}|${timeGrain ?? ''}|${groupKey ?? ''}`
}

function collectGroupedTimeGrainsByField(groups: ReportComposerGroupingState[]): Map<string, ReportTimeGrain[]> {
  const map = new Map<string, ReportTimeGrain[]>()

  for (const group of groups) {
    if (group.timeGrain === null) continue
    const grains = map.get(group.fieldCode) ?? []
    if (!grains.includes(group.timeGrain)) grains.push(group.timeGrain)
    map.set(group.fieldCode, grains)
  }

  return map
}

function findGroupingByKey(groups: ReportComposerGroupingState[], fieldCode: string, groupKey: string | null): ReportComposerGroupingState | null {
  if (!groupKey) return null
  return groups.find((group) => group.fieldCode === fieldCode && normalizeOptionalText(group.groupKey) === groupKey) ?? null
}

function resolveGroupedSortTimeGrain(
  requestedTimeGrain: ReportTimeGrain | null,
  groupedTimeGrains: ReportTimeGrain[],
  hasGroupingWithoutTimeGrain: boolean,
): ReportTimeGrain | null | undefined {
  if (groupedTimeGrains.length > 0) {
    if (requestedTimeGrain !== null && groupedTimeGrains.includes(requestedTimeGrain)) return requestedTimeGrain
    return groupedTimeGrains[0] ?? null
  }

  if (hasGroupingWithoutTimeGrain) {
    return requestedTimeGrain === null ? null : undefined
  }

  return undefined
}

function normalizeSortState(
  definition: ReportDefinitionDto,
  rowGroups: ReportComposerGroupingState[],
  columnGroups: ReportComposerGroupingState[],
  detailFields: string[],
  sorts: ReportComposerSortState[],
): ReportComposerSortState[] {
  const sortableFieldCodes = new Set(
    (definition.dataset?.fields ?? [])
      .filter((field) => !!field.isSortable)
      .map((field) => field.code),
  )
  const rowGroupTimeGrainsByField = collectGroupedTimeGrainsByField(rowGroups)
  const columnGroupTimeGrainsByField = collectGroupedTimeGrainsByField(columnGroups)

  const detailFieldSet = new Set(detailFields)
  const seen = new Set<string>()
  const result: ReportComposerSortState[] = []

  for (const sort of sorts) {
    const fieldCode = normalizeCode(sort.fieldCode)
    if (fieldCode.length === 0 || !sortableFieldCodes.has(fieldCode)) continue

    const direction = normalizeSortDirectionValue(sort.direction) ?? ReportSortDirection.Asc
    const requestedTimeGrain = normalizeTimeGrainValue(sort.timeGrain)
    const requestedGroupKey = normalizeOptionalText(sort.groupKey)
    const requestedColumnAxis = !!sort.appliesToColumnAxis

    const explicitRowGroup = findGroupingByKey(rowGroups, fieldCode, requestedGroupKey)
    const explicitColumnGroup = findGroupingByKey(columnGroups, fieldCode, requestedGroupKey)

    if (explicitRowGroup || explicitColumnGroup) {
      const appliesToColumnAxis = !!explicitColumnGroup
      const effectiveTimeGrain = explicitColumnGroup?.timeGrain ?? explicitRowGroup?.timeGrain ?? null
      const key = buildAxisSortStateKey(fieldCode, effectiveTimeGrain, appliesToColumnAxis, requestedGroupKey)
      if (seen.has(key)) continue

      result.push({
        fieldCode,
        groupKey: requestedGroupKey,
        appliesToColumnAxis,
        direction,
        timeGrain: effectiveTimeGrain,
      })
      seen.add(key)
      continue
    }

    const rowGroupedTimeGrains = rowGroupTimeGrainsByField.get(fieldCode) ?? []
    const columnGroupedTimeGrains = columnGroupTimeGrainsByField.get(fieldCode) ?? []
    const hasRowGrouping = rowGroups.some((group) => group.fieldCode === fieldCode)
    const hasColumnGrouping = columnGroups.some((group) => group.fieldCode === fieldCode)
    const hasDetailField = detailFieldSet.has(fieldCode)

    let appliesToColumnAxis = requestedColumnAxis
    let effectiveTimeGrain: ReportTimeGrain | null | undefined

    if (requestedColumnAxis) {
      effectiveTimeGrain = resolveGroupedSortTimeGrain(requestedTimeGrain, columnGroupedTimeGrains, hasColumnGrouping)
      if (effectiveTimeGrain === undefined && !hasColumnGrouping && hasRowGrouping) {
        appliesToColumnAxis = false
        effectiveTimeGrain = resolveGroupedSortTimeGrain(requestedTimeGrain, rowGroupedTimeGrains, hasRowGrouping)
      }
    } else {
      effectiveTimeGrain = resolveGroupedSortTimeGrain(requestedTimeGrain, rowGroupedTimeGrains, hasRowGrouping)
      if (effectiveTimeGrain === undefined && !hasRowGrouping && hasColumnGrouping) {
        appliesToColumnAxis = true
        effectiveTimeGrain = resolveGroupedSortTimeGrain(requestedTimeGrain, columnGroupedTimeGrains, hasColumnGrouping)
      }
    }

    if (effectiveTimeGrain !== undefined) {
      const key = buildAxisSortStateKey(fieldCode, effectiveTimeGrain, appliesToColumnAxis, null)
      if (seen.has(key)) continue

      result.push({
        fieldCode,
        groupKey: null,
        appliesToColumnAxis,
        direction,
        timeGrain: effectiveTimeGrain,
      })
      seen.add(key)
      continue
    }

    if (hasDetailField) {
      const key = buildAxisSortStateKey(fieldCode, null, false, null)
      if (seen.has(key)) continue

      result.push({
        fieldCode,
        groupKey: null,
        appliesToColumnAxis: false,
        direction,
        timeGrain: null,
      })
      seen.add(key)
    }
  }

  return result
}

function buildLayoutFromDraft(definition: ReportDefinitionDto, draft: ReportComposerDraft) {
  const rowGroups = normalizeGroupingState(definition, draft.rowGroups)
  const columnGroups = normalizeGroupingState(definition, draft.columnGroups)
  const measures = normalizeMeasureState(definition, draft.measures)
  const detailFields = normalizeDetailFieldState(draft.detailFields)
  const sorts = normalizeSortState(definition, rowGroups, columnGroups, detailFields, draft.sorts)

  return {
    rowGroups: rowGroups.map((group) => ({
      fieldCode: group.fieldCode,
      groupKey: group.groupKey ?? undefined,
      timeGrain: group.timeGrain ?? undefined,
    })),
    columnGroups: columnGroups.map((group) => ({
      fieldCode: group.fieldCode,
      groupKey: group.groupKey ?? undefined,
      timeGrain: group.timeGrain ?? undefined,
    })),
    measures: measures.map((measure) => ({
      measureCode: measure.measureCode,
      aggregation: normalizeAggregationValue(measure.aggregation) ?? resolveDefaultAggregation(definition, measure.measureCode),
      labelOverride: buildMeasureLayoutLabelOverride(definition, measure) ?? undefined,
    })),
    detailFields,
    sorts: sorts.map((sort) => ({
      fieldCode: sort.fieldCode,
      direction: sort.direction,
      appliesToColumnAxis: sort.appliesToColumnAxis || undefined,
      groupKey: sort.groupKey ?? undefined,
      timeGrain: sort.timeGrain ?? undefined,
    })),
    showDetails: !!draft.showDetails,
    showSubtotals: !!draft.showSubtotals,
    showSubtotalsOnSeparateRows: !!draft.showSubtotalsOnSeparateRows,
    showGrandTotals: !!draft.showGrandTotals,
  }
}

export function cloneComposerDraft(draft: ReportComposerDraft): ReportComposerDraft {
  return {
    parameters: { ...draft.parameters },
    filters: Object.fromEntries(
      Object.entries(draft.filters).map(([fieldCode, state]) => [fieldCode, {
        raw: state.raw,
        includeDescendants: state.includeDescendants,
        items: state.items.map((item) => ({ ...item })),
      }]),
    ),
    rowGroups: draft.rowGroups.map((group) => ({ ...group })),
    columnGroups: draft.columnGroups.map((group) => ({ ...group })),
    measures: draft.measures.map((measure) => ({ ...measure })),
    detailFields: [...draft.detailFields],
    sorts: draft.sorts.map((sort) => ({ ...sort })),
    showDetails: !!draft.showDetails,
    showSubtotals: !!draft.showSubtotals,
    showSubtotalsOnSeparateRows: !!draft.showSubtotalsOnSeparateRows,
    showGrandTotals: !!draft.showGrandTotals,
  }
}

export function normalizeComposerDraft(definition: ReportDefinitionDto, draft: ReportComposerDraft): ReportComposerDraft {
  const next = cloneComposerDraft(draft)
  const allowedParameterCodes = new Set((definition.parameters ?? []).map((parameter) => parameter.code))
  next.parameters = Object.fromEntries(
    Object.entries(next.parameters)
      .filter(([code]) => allowedParameterCodes.has(code)),
  )
  next.rowGroups = normalizeGroupingState(definition, next.rowGroups)
  next.columnGroups = normalizeGroupingState(definition, next.columnGroups)
  next.measures = normalizeMeasureState(definition, next.measures)
  next.detailFields = normalizeDetailFieldState(next.detailFields)
  next.sorts = normalizeSortState(definition, next.rowGroups, next.columnGroups, next.detailFields, next.sorts)

  if (!definition.capabilities?.allowsSubtotals) {
    next.showSubtotals = false
    next.showSubtotalsOnSeparateRows = false
  } else if (!definition.capabilities?.allowsSeparateRowSubtotals) {
    next.showSubtotalsOnSeparateRows = false
  }

  if (definition.capabilities?.allowsGrandTotals === false) {
    next.showGrandTotals = false
  }

  if (!definition.capabilities?.allowsShowDetails) {
    next.showDetails = false
  }

  return next
}

export function createComposerDraft(definition: ReportDefinitionDto): ReportComposerDraft {
  const defaultLayout = definition.defaultLayout ?? {}

  const parameters = Object.fromEntries(
    (definition.parameters ?? []).map((parameter) => [parameter.code, parameter.defaultValue?.trim() || defaultParameterValue(parameter.code)]),
  )

  const filters = Object.fromEntries(
    (definition.filters ?? []).map((field) => [field.fieldCode, createEmptyFilterState(!!field.defaultIncludeDescendants)]),
  )

  const rowGroups: ReportComposerGroupingState[] = (defaultLayout.rowGroups ?? [])
    .map((group) => ({
      groupKey: normalizeOptionalText(group.groupKey),
      fieldCode: group.fieldCode,
      timeGrain: normalizeTimeGrainValue(group.timeGrain),
    }))

  const columnGroups: ReportComposerGroupingState[] = (defaultLayout.columnGroups ?? [])
    .map((group) => ({
      groupKey: normalizeOptionalText(group.groupKey),
      fieldCode: group.fieldCode,
      timeGrain: normalizeTimeGrainValue(group.timeGrain),
    }))

  const measures: ReportComposerMeasureState[] = (defaultLayout.measures ?? [])
    .map((measure) => ({
      measureCode: measure.measureCode,
      aggregation: normalizeAggregationValue(measure.aggregation),
      labelOverride: measure.labelOverride ?? null,
    }))

  const detailFields = [...(defaultLayout.detailFields ?? [])]

  const sorts: ReportComposerSortState[] = (defaultLayout.sorts ?? [])
    .map((sort) => ({
      fieldCode: sort.fieldCode,
      groupKey: normalizeOptionalText(sort.groupKey),
      appliesToColumnAxis: !!sort.appliesToColumnAxis,
      direction: normalizeSortDirectionValue(sort.direction) ?? ReportSortDirection.Asc,
      timeGrain: normalizeTimeGrainValue(sort.timeGrain),
    }))

  if (measures.length === 0) {
    const firstMeasure = definition.dataset?.measures?.[0]
    if (firstMeasure) {
      measures.push({
        measureCode: firstMeasure.code,
        aggregation: resolveDefaultAggregation(definition, firstMeasure.code),
        labelOverride: null,
      })
    }
  }

  return normalizeComposerDraft(definition, {
    parameters,
    filters,
    rowGroups,
    columnGroups,
    measures,
    detailFields,
    sorts,
    showDetails: !!defaultLayout.showDetails,
    showSubtotals: defaultLayout.showSubtotals !== false,
    showSubtotalsOnSeparateRows: defaultLayout.showSubtotalsOnSeparateRows === true,
    showGrandTotals: defaultLayout.showGrandTotals !== false,
  })
}

export function applyExecutionRequestToDraft(definition: ReportDefinitionDto, request: ReportExecutionRequestDto): ReportComposerDraft {
  const next = createComposerDraft(definition)
  const layout = request.layout ?? null

  if (request.parameters) {
    for (const [key, value] of Object.entries(request.parameters)) {
      next.parameters[key] = String(value ?? '').trim()
    }
  }

  if (request.filters) {
    for (const [fieldCode, value] of Object.entries(request.filters)) {
      const state = next.filters[fieldCode]
      if (!state) continue
      state.raw = scalarFilterToRaw(value?.value)
      state.items = []
      state.includeDescendants = !!value?.includeDescendants
    }
  }

  if (layout) {
    next.rowGroups = (layout.rowGroups ?? []).map((group) => ({
      groupKey: normalizeOptionalText(group.groupKey),
      fieldCode: group.fieldCode,
      timeGrain: normalizeTimeGrainValue(group.timeGrain),
    }))
    next.columnGroups = (layout.columnGroups ?? []).map((group) => ({
      groupKey: normalizeOptionalText(group.groupKey),
      fieldCode: group.fieldCode,
      timeGrain: normalizeTimeGrainValue(group.timeGrain),
    }))
    next.measures = (layout.measures ?? []).map((measure) => ({
      measureCode: measure.measureCode,
      aggregation: normalizeAggregationValue(measure.aggregation) ?? resolveDefaultAggregation(definition, measure.measureCode),
      labelOverride: measure.labelOverride ?? null,
    }))
    next.detailFields = [...(layout.detailFields ?? [])]
    next.sorts = (layout.sorts ?? [])
      .map((sort) => ({
        fieldCode: sort.fieldCode,
        groupKey: normalizeOptionalText(sort.groupKey),
        appliesToColumnAxis: !!sort.appliesToColumnAxis,
        direction: normalizeSortDirectionValue(sort.direction) ?? ReportSortDirection.Asc,
        timeGrain: normalizeTimeGrainValue(sort.timeGrain),
      }))

    next.showDetails = !!layout.showDetails
    next.showSubtotals = layout.showSubtotals !== false
    next.showSubtotalsOnSeparateRows = layout.showSubtotalsOnSeparateRows === true
    next.showGrandTotals = layout.showGrandTotals !== false
  }

  if (next.measures.length === 0) {
    const firstMeasure = definition.dataset?.measures?.[0]
    if (firstMeasure) {
      next.measures.push({
        measureCode: firstMeasure.code,
        aggregation: resolveDefaultAggregation(definition, firstMeasure.code),
        labelOverride: null,
      })
    }
  }

  return normalizeComposerDraft(definition, next)
}

export function applyVariantToDraft(definition: ReportDefinitionDto, variant: ReportVariantDto): ReportComposerDraft {
  return applyExecutionRequestToDraft(definition, {
    layout: variant.layout ?? null,
    filters: variant.filters ?? null,
    parameters: variant.parameters ?? null,
    variantCode: variant.variantCode,
    offset: 0,
    limit: 500,
  })
}

export function slugifyVariantCode(name: string): string {
  const normalized = String(name ?? '')
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')

  return normalized || 'variant'
}

export function buildVariantDto(
  definition: ReportDefinitionDto,
  draft: ReportComposerDraft,
  options: {
    variantCode: string
    name: string
    isDefault?: boolean
    isShared?: boolean
  },
): ReportVariantDto {
  const request = buildExecutionRequest(definition, draft)
  return {
    variantCode: options.variantCode,
    reportCode: definition.reportCode,
    name: String(options.name ?? '').trim(),
    layout: request.layout ?? null,
    filters: request.filters ?? null,
    parameters: request.parameters ?? null,
    isDefault: !!options.isDefault,
    isShared: options.isShared !== false,
  }
}

function normalizeTimeGrainValue(value: unknown): ReportTimeGrain | null {
  if (typeof value === 'number') {
    switch (value) {
      case ReportTimeGrain.Day:
      case ReportTimeGrain.Week:
      case ReportTimeGrain.Month:
      case ReportTimeGrain.Quarter:
      case ReportTimeGrain.Year:
        return value
      default:
        return null
    }
  }
  const text = String(value ?? '').trim().toLowerCase()
  switch (text) {
    case '1':
    case 'day':
      return ReportTimeGrain.Day
    case '2':
    case 'week':
      return ReportTimeGrain.Week
    case '3':
    case 'month':
      return ReportTimeGrain.Month
    case '4':
    case 'quarter':
      return ReportTimeGrain.Quarter
    case '5':
    case 'year':
      return ReportTimeGrain.Year
    default:
      return null
  }
}

function normalizeSortDirectionValue(value: unknown): ReportSortDirection | null {
  if (typeof value === 'number') {
    switch (value) {
      case ReportSortDirection.Asc:
      case ReportSortDirection.Desc:
        return value
      default:
        return null
    }
  }
  const text = String(value ?? '').trim().toLowerCase()
  switch (text) {
    case '1':
    case 'asc':
    case 'ascending':
      return ReportSortDirection.Asc
    case '2':
    case 'desc':
    case 'descending':
      return ReportSortDirection.Desc
    default:
      return null
  }
}

function normalizeAggregationValue(value: unknown): ReportAggregationKind | null {
  if (typeof value === 'number') {
    switch (value) {
      case ReportAggregationKind.Sum:
      case ReportAggregationKind.Min:
      case ReportAggregationKind.Max:
      case ReportAggregationKind.Average:
      case ReportAggregationKind.Count:
      case ReportAggregationKind.CountDistinct:
      case ReportAggregationKind.First:
      case ReportAggregationKind.Last:
        return value
      default:
        return null
    }
  }
  const text = String(value ?? '').trim().toLowerCase()
  switch (text) {
    case '1':
    case 'sum':
      return ReportAggregationKind.Sum
    case '2':
    case 'min':
      return ReportAggregationKind.Min
    case '3':
    case 'max':
      return ReportAggregationKind.Max
    case '4':
    case 'average':
    case 'avg':
      return ReportAggregationKind.Average
    case '5':
    case 'count':
      return ReportAggregationKind.Count
    case '6':
    case 'countdistinct':
    case 'count_distinct':
    case 'count distinct':
      return ReportAggregationKind.CountDistinct
    case '7':
    case 'first':
      return ReportAggregationKind.First
    case '8':
    case 'last':
      return ReportAggregationKind.Last
    default:
      return null
  }
}

export function coerceReportTimeGrain(value: unknown): ReportTimeGrain | null {
  return normalizeTimeGrainValue(value)
}

export function coerceReportSortDirection(value: unknown): ReportSortDirection | null {
  return normalizeSortDirectionValue(value)
}

export function coerceReportAggregationKind(value: unknown): ReportAggregationKind | null {
  return normalizeAggregationValue(value)
}

export function resolveDefaultAggregation(definition: ReportDefinitionDto, measureCode: string): ReportAggregationKind {
  const measure = definition.dataset?.measures?.find((entry) => entry.code === measureCode)
  const supported = (measure?.supportedAggregations ?? [])
    .map(normalizeAggregationValue)
    .filter((value): value is ReportAggregationKind => value !== null)
  if (supported.length === 1) return supported[0]!
  if (supported.includes(ReportAggregationKind.Sum)) return ReportAggregationKind.Sum
  return supported[0] ?? ReportAggregationKind.Sum
}

export function getGroupableFields(definition: ReportDefinitionDto): ReportFieldDto[] {
  return (definition.dataset?.fields ?? []).filter((field) => !!field.isGroupable)
}

export function getSortableFields(definition: ReportDefinitionDto): ReportFieldDto[] {
  return (definition.dataset?.fields ?? []).filter((field) => !!field.isSortable)
}

export function getMeasureOptions(definition: ReportDefinitionDto): ReportMeasureDto[] {
  return definition.dataset?.measures ?? []
}

export function getSelectableFields(definition: ReportDefinitionDto): ReportFieldDto[] {
  return (definition.dataset?.fields ?? []).filter((field) => !!field.isSelectable)
}

export function getTimeGrainOptions(field: ReportFieldDto | null | undefined): ReportOptionItem<ReportTimeGrain>[] {
  const seen = new Set<ReportTimeGrain>()
  const options: ReportOptionItem<ReportTimeGrain>[] = []

  for (const raw of field?.supportedTimeGrains ?? []) {
    const grain = normalizeTimeGrainValue(raw)
    if (grain === null || seen.has(grain)) continue
    seen.add(grain)
    options.push({
      value: grain,
      label: timeGrainLabel(grain),
    })
  }

  return options
}

export function getAggregationOptions(definition: ReportDefinitionDto, measureCode: string): ReportOptionItem<ReportAggregationKind>[] {
  const measure = definition.dataset?.measures?.find((entry) => entry.code === measureCode)
  const supported = (measure?.supportedAggregations ?? [])
    .map(normalizeAggregationValue)
    .filter((value): value is ReportAggregationKind => value !== null)
  const seen = new Set<ReportAggregationKind>()
  const aggregations = (supported.length > 0 ? supported : [ReportAggregationKind.Sum]).filter((value) => {
    if (seen.has(value)) return false
    seen.add(value)
    return true
  })
  return aggregations.map((aggregation) => ({
    value: aggregation,
    label: aggregationLabel(aggregation),
  }))
}

export function timeGrainLabel(grain: ReportTimeGrain | null | undefined): string {
  switch (grain) {
    case ReportTimeGrain.Day: return 'Day'
    case ReportTimeGrain.Week: return 'Week'
    case ReportTimeGrain.Month: return 'Month'
    case ReportTimeGrain.Quarter: return 'Quarter'
    case ReportTimeGrain.Year: return 'Year'
    default: return 'None'
  }
}

export function aggregationLabel(aggregation: ReportAggregationKind): string {
  switch (aggregation) {
    case ReportAggregationKind.Sum: return 'Sum'
    case ReportAggregationKind.Min: return 'Min'
    case ReportAggregationKind.Max: return 'Max'
    case ReportAggregationKind.Average: return 'Average'
    case ReportAggregationKind.Count: return 'Count'
    case ReportAggregationKind.CountDistinct: return 'Count Distinct'
    case ReportAggregationKind.First: return 'First'
    case ReportAggregationKind.Last: return 'Last'
    default: return String(aggregation)
  }
}


export function buildAutoMeasureLabel(
  definition: ReportDefinitionDto,
  measureCode: string,
  aggregation: ReportAggregationKind | null | undefined,
): string {
  const normalizedMeasureCode = normalizeCode(measureCode)
  const effectiveAggregation = normalizeAggregationValue(aggregation) ?? resolveDefaultAggregation(definition, normalizedMeasureCode)
  const defaultAggregation = resolveDefaultAggregation(definition, normalizedMeasureCode)
  const baseLabel = measureBaseLabel(definition, normalizedMeasureCode)
  return effectiveAggregation === defaultAggregation
    ? baseLabel
    : `${baseLabel} (${aggregationLabel(effectiveAggregation)})`
}

export function resolveMeasureLabel(
  definition: ReportDefinitionDto,
  measure: Pick<ReportComposerMeasureState, 'measureCode' | 'aggregation' | 'labelOverride'>,
): string {
  const trimmed = String(measure.labelOverride ?? '').trim()
  if (trimmed.length > 0) return trimmed
  return buildAutoMeasureLabel(definition, measure.measureCode, measure.aggregation)
}

export function sortDirectionOptions(): ReportOptionItem<ReportSortDirection>[] {
  return [
    { value: ReportSortDirection.Asc, label: 'Ascending' },
    { value: ReportSortDirection.Desc, label: 'Descending' },
  ]
}

export function buildExecutionRequest(definition: ReportDefinitionDto, draft: ReportComposerDraft): ReportExecutionRequestDto {
  const request = buildBaseExecutionRequest(definition, draft)
  return {
    ...request,
    offset: 0,
    limit: 500,
  }
}

export function buildExportRequest(definition: ReportDefinitionDto, draft: ReportComposerDraft): ReportExportRequestDto {
  return buildBaseExecutionRequest(definition, draft)
}

function buildBaseExecutionRequest(definition: ReportDefinitionDto, draft: ReportComposerDraft): ReportExportRequestDto {
  const filterEntries: Array<[string, ReportFilterValueDto]> = []
  for (const field of definition.filters ?? []) {
    const state = draft.filters[field.fieldCode]
    if (!state) continue

    const itemIds = state.items.map((item) => item.id).filter((value) => value.trim().length > 0)
    const raw = state.raw.trim()
    const scalar = field.isMulti
      ? Array.from(new Set(raw.split(',').map((part) => part.trim()).filter((part) => part.length > 0)))
      : raw

    const value = itemIds.length > 0
      ? (field.isMulti ? itemIds : itemIds[0])
      : field.isMulti
        ? scalar
        : scalar || null

    if (value == null) continue
    if (Array.isArray(value) && value.length === 0) continue
    if (typeof value === 'string' && value.length === 0) continue

    filterEntries.push([field.fieldCode, {
      value,
      includeDescendants: !!state.includeDescendants,
    }])
  }

  const filters = Object.fromEntries(filterEntries)

  const parameters = Object.fromEntries(
    Object.entries(draft.parameters)
      .map(([key, value]) => [key, String(value ?? '').trim()])
      .filter(([, value]) => value.length > 0),
  )

  return {
    parameters: Object.keys(parameters).length > 0 ? parameters : null,
    filters: Object.keys(filters).length > 0 ? filters : null,
    layout: buildLayoutFromDraft(definition, draft),
  }
}
