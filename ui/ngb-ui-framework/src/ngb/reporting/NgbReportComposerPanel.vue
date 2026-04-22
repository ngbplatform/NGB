<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import NgbButton from '../primitives/NgbButton.vue'
import NgbDatePicker from '../primitives/NgbDatePicker.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import NgbInput from '../primitives/NgbInput.vue'
import NgbSelect from '../primitives/NgbSelect.vue'
import NgbSwitch from '../primitives/NgbSwitch.vue'
import NgbTabs from '../primitives/NgbTabs.vue'
import NgbFormLayout from '../components/forms/NgbFormLayout.vue'
import NgbFormRow from '../components/forms/NgbFormRow.vue'
import FilterFieldControl from '../metadata/NgbFilterFieldControl.vue'

import {
  ReportSortDirection,
  ReportTimeGrain,
  type ReportComposerDraft,
  type ReportComposerLookupItem,
  type ReportDefinitionDto,
  type ReportFieldDto,
  type ReportFilterFieldDto,
  type ReportOptionItem,
} from './types'
import {
  coerceReportAggregationKind,
  coerceReportComposerLookupItems,
  coerceReportSortDirection,
  coerceReportTimeGrain,
  cloneComposerDraft,
  getAggregationOptions,
  getReportComposerFilterState,
  getSelectedReportComposerFilterItem,
  getGroupableFields,
  getMeasureOptions,
  getSelectableFields,
  getSortableFields,
  getTimeGrainOptions,
  normalizeComposerDraft,
  resolveDefaultAggregation,
  resolveMeasureLabel,
  sortDirectionOptions,
} from './composer'
import { resolveReportLookupTarget } from './config'
import ReportComposerCollectionSection from './NgbReportComposerCollectionSection.vue'

const props = defineProps<{
  definition: ReportDefinitionDto
  modelValue: ReportComposerDraft
  lookupItemsByFilterCode: Record<string, ReportComposerLookupItem[]>
  running?: boolean
  variantOptions?: ReportOptionItem[]
  selectedVariantCode?: string
  variantSummary?: string
  variantDisabled?: boolean
  createVariantDisabled?: boolean
  editVariantDisabled?: boolean
  saveVariantDisabled?: boolean
  deleteVariantDisabled?: boolean
  resetVariantDisabled?: boolean
  loadVariantDisabled?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: ReportComposerDraft): void
  (e: 'run'): void
  (e: 'close'): void
  (e: 'filter-query', payload: { fieldCode: string; query: string }): void
  (e: 'update:selectedVariantCode', value: string): void
  (e: 'create-variant'): void
  (e: 'edit-variant'): void
  (e: 'save-variant'): void
  (e: 'delete-variant'): void
  (e: 'reset-variant'): void
  (e: 'load-variant'): void
}>()

type ComposerTabKey = 'general' | 'grouping' | 'filters' | 'fields' | 'sorting' | 'variant'
type ComposerDragSection = 'rowGroups' | 'columnGroups' | 'measures' | 'detailFields' | 'sorts'
type GroupingEntry = ReportComposerDraft['rowGroups'][number]
type MeasureEntry = ReportComposerDraft['measures'][number]
type SortEntry = ReportComposerDraft['sorts'][number]

const groupableFields = computed(() => getGroupableFields(props.definition))
const selectableFields = computed(() => getSelectableFields(props.definition))
const sortableFields = computed(() => getSortableFields(props.definition))
const measures = computed(() => getMeasureOptions(props.definition))
const route = useRoute()
const router = useRouter()
const sortDirections = sortDirectionOptions()
const groupingSectionColumns = [
  { title: 'Field' },
  { title: 'Time grain', width: '220px' },
]
const measureSectionColumns = [
  { title: 'Measure' },
  { title: 'Aggregation', width: '132px' },
  { title: 'Label', width: '132px' },
]
const detailFieldSectionColumns = [
  { title: 'Field' },
]
const sortingSectionColumns = [
  { title: 'Field', width: '120px' },
  { title: 'Axis', width: '120px' },
  { title: 'Direction', width: '132px' },
  { title: 'Time grain', width: '100px' },
]
const allowsRowGroups = computed(() => !!props.definition.capabilities?.allowsRowGroups)
const allowsColumnGroups = computed(() => !!props.definition.capabilities?.allowsColumnGroups)
const allowsMeasures = computed(() => props.definition.capabilities?.allowsMeasures !== false)
const allowsDetailFields = computed(() => !!props.definition.capabilities?.allowsDetailFields)
const allowsSorting = computed(() => !!props.definition.capabilities?.allowsSorting)
const allowsShowDetails = computed(() => !!props.definition.capabilities?.allowsShowDetails)
const allowsSubtotals = computed(() => !!props.definition.capabilities?.allowsSubtotals)
const allowsSeparateRowSubtotals = computed(() => !!props.definition.capabilities?.allowsSeparateRowSubtotals)
const allowsGrandTotals = computed(() => props.definition.capabilities?.allowsGrandTotals !== false)
const allowsVariants = computed(() => props.definition.capabilities?.allowsVariants !== false)

type SortTarget = {
  fieldCode: string
  timeGrain: ReportTimeGrain | null
  appliesToColumnAxis: boolean
}

const timeGrainDrillOrder = [
  ReportTimeGrain.Year,
  ReportTimeGrain.Quarter,
  ReportTimeGrain.Month,
  ReportTimeGrain.Week,
  ReportTimeGrain.Day,
]

const groupFieldOptions = computed(() => groupableFields.value.map((field) => ({ value: field.code, label: field.label })))
const selectedSortableFields = computed(() => {
  const selected = new Map<string, ReportFieldDto>()

  for (const group of props.modelValue.rowGroups) {
    const field = fieldByCode(group.fieldCode)
    if (field?.isSortable) selected.set(field.code, field)
  }

  for (const group of props.modelValue.columnGroups) {
    const field = fieldByCode(group.fieldCode)
    if (field?.isSortable) selected.set(field.code, field)
  }

  for (const fieldCode of props.modelValue.detailFields) {
    const field = fieldByCode(fieldCode)
    if (field?.isSortable) selected.set(field.code, field)
  }

  return [...selected.values()]
})

const sortFieldOptions = computed(() => selectedSortableFields.value.map((field) => ({ value: field.code, label: field.label })))
const availableSortTargets = computed<SortTarget[]>(() => {
  const targets: SortTarget[] = []
  const seen = new Set<string>()

  for (const group of props.modelValue.rowGroups) {
    const field = fieldByCode(group.fieldCode)
    if (!field?.isSortable) continue

    const key = buildAxisSortTargetKey(group.fieldCode, group.timeGrain ?? null, false)
    if (seen.has(key)) continue
    seen.add(key)
    targets.push({
      fieldCode: group.fieldCode,
      timeGrain: group.timeGrain ?? null,
      appliesToColumnAxis: false,
    })
  }

  for (const group of props.modelValue.columnGroups) {
    const field = fieldByCode(group.fieldCode)
    if (!field?.isSortable) continue

    const key = buildAxisSortTargetKey(group.fieldCode, group.timeGrain ?? null, true)
    if (seen.has(key)) continue
    seen.add(key)
    targets.push({
      fieldCode: group.fieldCode,
      timeGrain: group.timeGrain ?? null,
      appliesToColumnAxis: true,
    })
  }

  for (const fieldCode of props.modelValue.detailFields) {
    const field = fieldByCode(fieldCode)
    if (!field?.isSortable) continue

    const key = buildAxisSortTargetKey(fieldCode, null, false)
    if (seen.has(key)) continue
    seen.add(key)
    targets.push({
      fieldCode,
      timeGrain: null,
      appliesToColumnAxis: false,
    })
  }

  return targets
})
const measureOptions = computed(() => measures.value.map((measure) => ({ value: measure.code, label: measure.label })))
const selectableFieldOptions = computed(() => selectableFields.value.map((field) => ({ value: field.code, label: field.label, meta: field.description ?? undefined })))
const sortingHelpLines = computed(() => {
  if (!allowsSorting.value || sortableFields.value.length === 0) return []

  const lines = ['Sort only by fields already selected in row groups, column groups, or detail fields.']
  const hasTimeGrouping = [...props.modelValue.rowGroups, ...props.modelValue.columnGroups]
    .some((group) => {
      const field = fieldByCode(group.fieldCode)
      return (field?.supportedTimeGrains?.length ?? 0) > 0
    })

  if (hasTimeGrouping) {
    lines.push('For grouped time fields, the time grain must match the selected grouping axis.')
  }

  return lines
})

const hasGeneralTab = computed(() => (props.definition.parameters?.length ?? 0) > 0 || hasFormattingContent.value)
const hasGroupingTab = computed(() => (allowsRowGroups.value || allowsColumnGroups.value) && groupableFields.value.length > 0)
const hasFiltersTab = computed(() => (props.definition.filters?.length ?? 0) > 0)
const hasFieldsTab = computed(() => (allowsMeasures.value && measureOptions.value.length > 0) || (allowsDetailFields.value && selectableFieldOptions.value.length > 0))
const hasSortingTab = computed(() => allowsSorting.value && sortableFields.value.length > 0)
const hasFormattingContent = computed(() => allowsShowDetails.value || allowsSubtotals.value || allowsGrandTotals.value)
const hasVariantTab = computed(() => allowsVariants.value)

const tabs = computed(() => {
  const items: { key: ComposerTabKey; label: string }[] = []
  if (hasGeneralTab.value) items.push({ key: 'general', label: 'General' })
  if (hasGroupingTab.value) items.push({ key: 'grouping', label: 'Grouping' })
  if (hasFiltersTab.value) items.push({ key: 'filters', label: 'Filters' })
  if (hasFieldsTab.value) items.push({ key: 'fields', label: 'Fields' })
  if (hasSortingTab.value) items.push({ key: 'sorting', label: 'Sorting' })
  if (hasVariantTab.value) items.push({ key: 'variant', label: 'Variant' })
  return items
})

const activeTab = ref<ComposerTabKey>('general')
const dragState = ref<{ section: ComposerDragSection; index: number } | null>(null)

watch(
  tabs,
  (nextTabs) => {
    const first = nextTabs[0]?.key ?? 'general'
    if (!nextTabs.some((tab) => tab.key === activeTab.value)) activeTab.value = first
  },
  { immediate: true },
)

function updateDraft(mutator: (draft: ReportComposerDraft) => void) {
  const next = cloneComposerDraft(props.modelValue)
  mutator(next)
  emit('update:modelValue', normalizeComposerDraft(props.definition, next))
}

function rowGroupingKey(group: GroupingEntry, index: number): string {
  return `row:${group.fieldCode}:${index}`
}

function columnGroupingKey(group: GroupingEntry, index: number): string {
  return `column:${group.fieldCode}:${index}`
}

function measureKey(measure: MeasureEntry, index: number): string {
  return `${measure.measureCode}:${index}`
}

function detailFieldKey(fieldCode: string, index: number): string {
  return `${fieldCode}:${index}`
}

function sortKey(sort: SortEntry, index: number): string {
  return `${sort.fieldCode}:${sort.timeGrain ?? 'none'}:${index}`
}

const rowGroupingRowKey = (item: unknown, index: number) => rowGroupingKey(item as GroupingEntry, index)
const columnGroupingRowKey = (item: unknown, index: number) => columnGroupingKey(item as GroupingEntry, index)
const measureRowKey = (item: unknown, index: number) => measureKey(item as MeasureEntry, index)
const detailFieldRowKey = (item: unknown, index: number) => detailFieldKey(String(item ?? ''), index)
const sortRowKey = (item: unknown, index: number) => sortKey(item as SortEntry, index)

function groupingTimeGrainOptions(fieldCode: string): ReportOptionItem[] {
  return [{ value: '', label: 'No time grain' }, ...getTimeGrainOptions(fieldByCode(fieldCode))]
}

function hasGroupingTimeGrainOptions(fieldCode: string): boolean {
  return getTimeGrainOptions(fieldByCode(fieldCode)).length > 0
}

function measureAggregationOptions(measureCode: string): ReportOptionItem[] {
  return getAggregationOptions(props.definition, measureCode)
}

function measureAggregationValue(measure: MeasureEntry) {
  return measure.aggregation ?? resolveDefaultAggregation(props.definition, measure.measureCode)
}

function handleCollectionDragStart(payload: { section: string; index: number; event: DragEvent }) {
  onComposerDragStart(payload.section as ComposerDragSection, payload.index, payload.event)
}

function handleCollectionDrop(payload: { section: string; index: number; event: DragEvent }) {
  onComposerDrop(payload.section as ComposerDragSection, payload.index, payload.event)
}

function filterState(field: ReportFilterFieldDto) {
  return getReportComposerFilterState(props.modelValue, field)
}

function selectedFilterItem(field: ReportFilterFieldDto): ReportComposerLookupItem | null {
  return getSelectedReportComposerFilterItem(props.modelValue, field)
}

function normalizeParameterDataType(dataType: string): string {
  return dataType.trim().toLowerCase().replace(/[^a-z0-9]+/g, '_')
}

function isDateParameter(dataType: string) {
  const normalized = normalizeParameterDataType(dataType)
  return normalized === 'date' || normalized === 'date_only' || normalized === 'date_time_utc'
}

function parameterInputType(dataType: string): string {
  const normalized = dataType.trim().toLowerCase()
  if (normalized.includes('int') || normalized.includes('decimal') || normalized.includes('number')) return 'number'
  return 'text'
}

function normalizeDateParameterValue(value: string | null | undefined): string | null {
  const text = String(value ?? '').trim()
  const match = text.match(/^(\d{4}-\d{2}-\d{2})/)
  return match?.[1] ?? null
}

function fieldByCode(fieldCode: string): ReportFieldDto | undefined {
  return props.definition.dataset?.fields?.find((field) => field.code === fieldCode)
}

function buildSortTargetKey(fieldCode: string, timeGrain: ReportTimeGrain | null): string {
  return `${fieldCode}|${timeGrain ?? ''}`
}

function buildAxisSortTargetKey(fieldCode: string, timeGrain: ReportTimeGrain | null, appliesToColumnAxis: boolean): string {
  return `${fieldCode}|${appliesToColumnAxis ? 'column' : 'row'}|${timeGrain ?? ''}`
}

function nextFinerTimeGrain(field: ReportFieldDto | undefined, current: ReportTimeGrain | null): ReportTimeGrain | null {
  if (!field || current === null) return null

  const supported = new Set(getTimeGrainOptions(field).map((option) => option.value))
  const currentIndex = timeGrainDrillOrder.indexOf(current)
  if (currentIndex < 0) return null

  for (let index = currentIndex + 1; index < timeGrainDrillOrder.length; index += 1) {
    const grain = timeGrainDrillOrder[index]!
    if (supported.has(grain)) return grain
  }

  return null
}

function nextDrillGrouping(axis: 'rowGroups' | 'columnGroups'): { fieldCode: string; timeGrain: ReportTimeGrain | null } | null {
  const groups = props.modelValue[axis]
  const last = groups[groups.length - 1]
  if (!last) return null

  const field = fieldByCode(last.fieldCode)
  const nextGrain = nextFinerTimeGrain(field, last.timeGrain)
  if (nextGrain === null) return null

  const existing = new Set(groups.map((entry) => buildSortTargetKey(entry.fieldCode, entry.timeGrain ?? null)))
  const key = buildSortTargetKey(last.fieldCode, nextGrain)
  if (existing.has(key)) return null

  return {
    fieldCode: last.fieldCode,
    timeGrain: nextGrain,
  }
}

function sortAxisOptionsForField(fieldCode: string): ReportOptionItem[] {
  const options: ReportOptionItem[] = []

  if (props.modelValue.rowGroups.some((group) => group.fieldCode === fieldCode))
    options.push({ value: 'row', label: 'Rows' })

  if (props.modelValue.columnGroups.some((group) => group.fieldCode === fieldCode))
    options.push({ value: 'column', label: 'Columns' })

  if (props.modelValue.detailFields.includes(fieldCode))
    options.push({ value: 'detail', label: 'Details' })

  return options
}

function resolveSortAxis(sort: { fieldCode: string; appliesToColumnAxis?: boolean }): 'row' | 'column' | 'detail' {
  const available = sortAxisOptionsForField(sort.fieldCode).map((option) => String(option.value))

  if (sort.appliesToColumnAxis && available.includes('column')) return 'column'
  if (!sort.appliesToColumnAxis && available.includes('row')) return 'row'
  if (!sort.appliesToColumnAxis && available.includes('detail')) return 'detail'
  if (available.includes('column')) return 'column'
  if (available.includes('detail')) return 'detail'
  return 'row'
}

function sortTimeGrainOptionsForFieldAndAxis(fieldCode: string, axis: 'row' | 'column' | 'detail'): ReportOptionItem[] {
  const field = fieldByCode(fieldCode)
  if (!field || axis === 'detail') return []

  const groups = axis === 'column' ? props.modelValue.columnGroups : props.modelValue.rowGroups

  const groupedTimeGrains = Array.from(new Set(
    groups
      .filter((group): group is typeof group & { timeGrain: ReportTimeGrain } => group.fieldCode === fieldCode && group.timeGrain !== null)
      .map((group) => group.timeGrain),
  ))

  if (groupedTimeGrains.length === 0) return []

  return groupedTimeGrains.map((grain) => ({
    value: grain,
    label: getTimeGrainOptions(field).find((option) => option.value === grain)?.label ?? 'No time grain',
  }))
}

function addGrouping(axis: 'rowGroups' | 'columnGroups') {
  const fallback = groupableFields.value[0]
  if (!fallback) return

  const drill = nextDrillGrouping(axis)
  if (drill) {
    updateDraft((draft) => {
      draft[axis].push({
        fieldCode: drill.fieldCode,
        timeGrain: drill.timeGrain,
      })
    })
    return
  }

  const existing = new Set(props.modelValue[axis].map((entry) => entry.fieldCode))
  const chosen = groupableFields.value.find((field) => !existing.has(field.code)) ?? fallback

  updateDraft((draft) => {
    draft[axis].push({
      fieldCode: chosen.code,
      timeGrain: null,
    })
  })
}

function moveGrouping(axis: 'rowGroups' | 'columnGroups', index: number, delta: number) {
  updateDraft((draft) => {
    const target = index + delta
    if (target < 0 || target >= draft[axis].length) return
    const [entry] = draft[axis].splice(index, 1)
    draft[axis].splice(target, 0, entry!)
  })
}

function removeGrouping(axis: 'rowGroups' | 'columnGroups', index: number) {
  updateDraft((draft) => {
    draft[axis].splice(index, 1)
  })
}

function setGroupingField(axis: 'rowGroups' | 'columnGroups', index: number, fieldCode: string) {
  updateDraft((draft) => {
    const entry = draft[axis][index]
    if (!entry) return
    entry.fieldCode = fieldCode
    const field = fieldByCode(fieldCode)
    if (!field?.supportedTimeGrains?.length) entry.timeGrain = null
  })
}

function setGroupingTimeGrain(axis: 'rowGroups' | 'columnGroups', index: number, value: unknown) {
  updateDraft((draft) => {
    const entry = draft[axis][index]
    if (!entry) return
    entry.timeGrain = coerceReportTimeGrain(value)
  })
}

function addMeasure() {
  const fallback = measures.value[0]
  if (!fallback) return

  const existing = new Set(props.modelValue.measures.map((entry) => entry.measureCode))
  const chosen = measures.value.find((measure) => !existing.has(measure.code)) ?? fallback

  updateDraft((draft) => {
    draft.measures.push({
      measureCode: chosen.code,
      aggregation: resolveDefaultAggregation(props.definition, chosen.code),
      labelOverride: null,
    })
  })
}

function addSort() {
  const fallback = availableSortTargets.value[0]
  if (!fallback) return

  const existing = new Set(props.modelValue.sorts.map((entry) => buildAxisSortTargetKey(entry.fieldCode, entry.timeGrain ?? null, !!entry.appliesToColumnAxis)))
  const chosen = availableSortTargets.value.find((target) => !existing.has(buildAxisSortTargetKey(target.fieldCode, target.timeGrain, target.appliesToColumnAxis))) ?? fallback

  updateDraft((draft) => {
    draft.sorts.push({
      fieldCode: chosen.fieldCode,
      appliesToColumnAxis: chosen.appliesToColumnAxis,
      groupKey: null,
      direction: ReportSortDirection.Asc,
      timeGrain: chosen.timeGrain,
    })
  })
}

function moveSort(index: number, delta: number) {
  updateDraft((draft) => {
    const target = index + delta
    if (target < 0 || target >= draft.sorts.length) return
    const [entry] = draft.sorts.splice(index, 1)
    draft.sorts.splice(target, 0, entry!)
  })
}

function removeMeasure(index: number) {
  updateDraft((draft) => {
    draft.measures.splice(index, 1)
  })
}

function removeSort(index: number) {
  updateDraft((draft) => {
    draft.sorts.splice(index, 1)
  })
}

function setParameter(code: string, value: string) {
  updateDraft((draft) => {
    draft.parameters[code] = value
  })
}

function setFilterRaw(fieldCode: string, value: string) {
  updateDraft((draft) => {
    const state = draft.filters[fieldCode]
    if (!state) return
    state.raw = value
  })
}

function setFilterItems(fieldCode: string, value: unknown) {
  const items = coerceReportComposerLookupItems(value)
  updateDraft((draft) => {
    const state = draft.filters[fieldCode]
    if (!state) return
    state.items = items.map((item) => ({ ...item }))
    if (items.length > 0) state.raw = ''
  })
}

async function openFilterItem(field: ReportFilterFieldDto) {
  const target = await resolveReportLookupTarget({
    hint: field.lookup ?? null,
    value: selectedFilterItem(field),
    routeFullPath: route.fullPath,
  })

  if (!target) return
  await router.push(target)
}

function setFilterIncludeDescendants(fieldCode: string, value: boolean) {
  updateDraft((draft) => {
    const state = draft.filters[fieldCode]
    if (!state) return
    state.includeDescendants = value
  })
}

function setMeasureCode(index: number, measureCode: string) {
  updateDraft((draft) => {
    const entry = draft.measures[index]
    if (!entry) return
    entry.measureCode = measureCode
    entry.aggregation = resolveDefaultAggregation(props.definition, measureCode)
    entry.labelOverride = null
  })
}

function setMeasureAggregation(index: number, value: unknown) {
  updateDraft((draft) => {
    const entry = draft.measures[index]
    if (!entry) return
    entry.aggregation = coerceReportAggregationKind(value)
  })
}

function measureLabelValue(index: number): string {
  const entry = props.modelValue.measures[index]
  return entry ? resolveMeasureLabel(props.definition, entry) : ''
}

function setMeasureLabel(index: number, value: string) {
  updateDraft((draft) => {
    const entry = draft.measures[index]
    if (!entry) return

    const trimmed = String(value ?? '').trim()
    const autoLabel = resolveMeasureLabel(props.definition, { ...entry, labelOverride: null })
    entry.labelOverride = trimmed.length === 0 || trimmed === autoLabel ? null : trimmed
  })
}

function addDetailField() {
  const fallback = selectableFields.value[0]
  if (!fallback) return

  const existing = new Set(props.modelValue.detailFields)
  const chosen = selectableFields.value.find((field) => !existing.has(field.code)) ?? fallback

  updateDraft((draft) => {
    draft.detailFields.push(chosen.code)
  })
}

function removeDetailField(index: number) {
  updateDraft((draft) => {
    draft.detailFields.splice(index, 1)
  })
}

function detailFieldOptionsFor(currentFieldCode: string) {
  const selected = new Set(props.modelValue.detailFields.filter((fieldCode) => fieldCode !== currentFieldCode))
  return selectableFieldOptions.value.filter((option) => !selected.has(String(option.value)))
}

function sortFieldOptionsFor(_currentFieldCode: string) {
  return sortFieldOptions.value
}

function isSortTimeGrainLocked(fieldCode: string, axis: 'row' | 'column' | 'detail'): boolean {
  return sortTimeGrainOptionsForFieldAndAxis(fieldCode, axis).length <= 1
}

function setDetailField(index: number, fieldCode: string) {
  updateDraft((draft) => {
    if (fieldCode.trim().length === 0) return
    const duplicateIndex = draft.detailFields.findIndex((entry, entryIndex) => entry === fieldCode && entryIndex !== index)
    if (duplicateIndex >= 0) return
    draft.detailFields[index] = fieldCode
  })
}

function setSortField(index: number, fieldCode: string) {
  updateDraft((draft) => {
    const entry = draft.sorts[index]
    if (!entry) return
    entry.fieldCode = fieldCode
    entry.groupKey = null
    const firstAxis = String(sortAxisOptionsForField(fieldCode)[0]?.value ?? 'row')
    const axis = firstAxis === 'column' ? 'column' : firstAxis === 'detail' ? 'detail' : 'row'
    entry.appliesToColumnAxis = axis === 'column'
    const options = sortTimeGrainOptionsForFieldAndAxis(fieldCode, axis)
    entry.timeGrain = axis === 'detail' ? null : coerceReportTimeGrain(options[0]?.value ?? null)
  })
}

function setSortAxis(index: number, value: string) {
  updateDraft((draft) => {
    const entry = draft.sorts[index]
    if (!entry) return

    const axis = value === 'column' ? 'column' : value === 'detail' ? 'detail' : 'row'
    entry.appliesToColumnAxis = axis === 'column'
    entry.groupKey = null
    const options = sortTimeGrainOptionsForFieldAndAxis(entry.fieldCode, axis)
    entry.timeGrain = axis === 'detail' ? null : coerceReportTimeGrain(options[0]?.value ?? null)
  })
}

function setSortDirection(index: number, value: unknown) {
  updateDraft((draft) => {
    const entry = draft.sorts[index]
    if (!entry) return
    entry.direction = coerceReportSortDirection(value) ?? ReportSortDirection.Asc
  })
}

function setSortTimeGrain(index: number, value: unknown) {
  updateDraft((draft) => {
    const entry = draft.sorts[index]
    if (!entry) return
    entry.timeGrain = coerceReportTimeGrain(value)
  })
}

function moveItem<T>(items: T[], from: number, to: number) {
  if (from === to || from < 0 || to < 0 || from >= items.length || to >= items.length) return
  const [entry] = items.splice(from, 1)
  items.splice(to, 0, entry!)
}

function onComposerDragStart(section: ComposerDragSection, index: number, e: DragEvent) {
  dragState.value = { section, index }
  try {
    e.dataTransfer?.setData('text/plain', String(index))
    e.dataTransfer?.setDragImage(new Image(), 0, 0)
  } catch {
    // ignore
  }
}

function onComposerDragOver(e: DragEvent) {
  e.preventDefault()
}

function onComposerDrop(section: ComposerDragSection, index: number, e: DragEvent) {
  e.preventDefault()

  const active = dragState.value
  const from = active?.section === section
    ? active.index
    : Number(e.dataTransfer?.getData('text/plain') ?? NaN)

  if (!Number.isFinite(from) || from === index) {
    dragState.value = null
    return
  }

  updateDraft((draft) => {
    switch (section) {
      case 'rowGroups':
        moveItem(draft.rowGroups, from, index)
        break
      case 'columnGroups':
        moveItem(draft.columnGroups, from, index)
        break
      case 'measures':
        moveItem(draft.measures, from, index)
        break
      case 'detailFields':
        moveItem(draft.detailFields, from, index)
        break
      case 'sorts':
        moveItem(draft.sorts, from, index)
        break
    }
  })

  dragState.value = null
}

function setFlag(flag: 'showDetails' | 'showSubtotals' | 'showSubtotalsOnSeparateRows' | 'showGrandTotals', value: boolean) {
  updateDraft((draft) => {
    draft[flag] = value
  })
}
</script>

<template>
  <div data-testid="report-composer-panel" class="flex h-full flex-col">
    <div class="flex items-center gap-3 border-b border-ngb-border px-5 py-4">
      <div class="min-w-0 flex-1">
        <div class="truncate text-base font-semibold text-ngb-text">{{ definition.name }}</div>
        <div v-if="definition.description" class="mt-1 text-sm text-ngb-muted">{{ definition.description }}</div>
      </div>

      <div class="flex items-center gap-2">
        <button type="button" class="ngb-iconbtn" :disabled="running" title="Run" @click="emit('run')">
          <NgbIcon name="play" :size="50" />
        </button>
        <button type="button" class="ngb-iconbtn" title="Close" @click="emit('close')">
          <NgbIcon name="x" />
        </button>
      </div>
    </div>

    <div class="flex-1 overflow-auto px-5 py-4">
      <NgbTabs v-model="activeTab" :tabs="tabs" full-width-bar>
        <template #default="{ active }">
          <div class="space-y-6">
            <section v-if="active === 'general' && hasGeneralTab" class="space-y-6">
              <NgbFormLayout v-if="(definition.parameters?.length ?? 0) > 0">
                <NgbFormRow
                  v-for="parameter in definition.parameters ?? []"
                  :key="parameter.code"
                  :label="parameter.label ?? parameter.code"
                  :hint="parameter.description ?? undefined"
                  dense
                >
                  <NgbDatePicker
                    v-if="isDateParameter(parameter.dataType)"
                    :model-value="normalizeDateParameterValue(modelValue.parameters[parameter.code])"
                    @update:model-value="setParameter(parameter.code, $event ?? '')"
                  />

                  <NgbInput
                    v-else
                    :model-value="modelValue.parameters[parameter.code] ?? ''"
                    :type="parameterInputType(parameter.dataType)"
                    :placeholder="parameter.dataType"
                    @update:model-value="setParameter(parameter.code, String($event ?? ''))"
                  />
                </NgbFormRow>
              </NgbFormLayout>

              <div v-if="hasFormattingContent" class="grid gap-3">
                <div v-if="allowsShowDetails" class="flex items-center justify-between gap-4 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
                  <div>
                    <div class="text-sm font-medium text-ngb-text">Show details</div>
                    <div class="mt-1 text-xs text-ngb-muted">Ask the engine to include detail rows under groups when supported.</div>
                  </div>
                  <NgbSwitch :model-value="modelValue.showDetails" @update:model-value="setFlag('showDetails', $event)" />
                </div>

                <div v-if="allowsSubtotals" class="flex items-center justify-between gap-4 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
                  <div>
                    <div class="text-sm font-medium text-ngb-text">Show subtotals</div>
                    <div class="mt-1 text-xs text-ngb-muted">Render subtotal values for grouped output and row-axis pivot slices.</div>
                  </div>
                  <NgbSwitch :model-value="modelValue.showSubtotals" @update:model-value="setFlag('showSubtotals', $event)" />
                </div>

                <div v-if="allowsSeparateRowSubtotals" class="flex items-center justify-between gap-4 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
                  <div>
                    <div class="text-sm font-medium text-ngb-text">Show subtotals on separate rows</div>
                    <div class="mt-1 text-xs text-ngb-muted">Keep the hierarchy column, move subtotals to dedicated rows.</div>
                  </div>
                  <NgbSwitch
                    :model-value="modelValue.showSubtotalsOnSeparateRows"
                    :disabled="!modelValue.showSubtotals"
                    @update:model-value="setFlag('showSubtotalsOnSeparateRows', $event)"
                  />
                </div>

                <div v-if="allowsGrandTotals" class="flex items-center justify-between gap-4 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
                  <div>
                    <div class="text-sm font-medium text-ngb-text">Show totals</div>
                    <div class="mt-1 text-xs text-ngb-muted">Render trailing total columns and the final total row for pivot layouts.</div>
                  </div>
                  <NgbSwitch :model-value="modelValue.showGrandTotals" @update:model-value="setFlag('showGrandTotals', $event)" />
                </div>
              </div>

              <div v-if="sortingHelpLines.length > 0" class="space-y-1 text-xs text-ngb-muted">
                <p v-for="line in sortingHelpLines" :key="line">{{ line }}</p>
              </div>
            </section>

            <section v-if="active === 'grouping' && hasGroupingTab" class="space-y-6">
              <ReportComposerCollectionSection
                v-if="allowsRowGroups"
                title="Rows"
                add-label="Add row"
                :items="modelValue.rowGroups"
                :columns="groupingSectionColumns"
                empty-message="No row groups selected."
                section="rowGroups"
                :row-key="rowGroupingRowKey"
                @add="addGrouping('rowGroups')"
                @remove="removeGrouping('rowGroups', $event)"
                @dragstart="handleCollectionDragStart"
                @dragover="onComposerDragOver"
                @drop="handleCollectionDrop"
              >
                <template #cells="{ item: group, index }">
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="group.fieldCode"
                        :options="groupFieldOptions"
                        @update:model-value="setGroupingField('rowGroups', index, String($event ?? ''))"
                      />
                    </div>
                  </td>
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="group.timeGrain ?? ''"
                        :options="groupingTimeGrainOptions(group.fieldCode)"
                        :disabled="!hasGroupingTimeGrainOptions(group.fieldCode)"
                        @update:model-value="setGroupingTimeGrain('rowGroups', index, $event)"
                      />
                    </div>
                  </td>
                </template>
              </ReportComposerCollectionSection>

              <ReportComposerCollectionSection
                v-if="allowsColumnGroups"
                title="Columns"
                add-label="Add column"
                :items="modelValue.columnGroups"
                :columns="groupingSectionColumns"
                empty-message="No column groups selected. Add one to switch the sheet into pivot mode."
                section="columnGroups"
                :row-key="columnGroupingRowKey"
                @add="addGrouping('columnGroups')"
                @remove="removeGrouping('columnGroups', $event)"
                @dragstart="handleCollectionDragStart"
                @dragover="onComposerDragOver"
                @drop="handleCollectionDrop"
              >
                <template #cells="{ item: group, index }">
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="group.fieldCode"
                        :options="groupFieldOptions"
                        @update:model-value="setGroupingField('columnGroups', index, String($event ?? ''))"
                      />
                    </div>
                  </td>
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="group.timeGrain ?? ''"
                        :options="groupingTimeGrainOptions(group.fieldCode)"
                        :disabled="!hasGroupingTimeGrainOptions(group.fieldCode)"
                        @update:model-value="setGroupingTimeGrain('columnGroups', index, $event)"
                      />
                    </div>
                  </td>
                </template>
              </ReportComposerCollectionSection>
            </section>

            <section v-if="active === 'filters' && hasFiltersTab" class="space-y-4">
              <NgbFormLayout>
                <NgbFormRow
                  v-for="field in definition.filters ?? []"
                  :key="field.fieldCode"
                  :label="field.label"
                  :hint="field.description ?? undefined"
                  dense
                >
                  <FilterFieldControl
                    :field="field"
                    :state="filterState(field)"
                    :lookup-items="lookupItemsByFilterCode[field.fieldCode] ?? []"
                    select-empty-label="All"
                    :show-open="!!selectedFilterItem(field)"
                    :show-clear="!!selectedFilterItem(field)"
                    allow-include-descendants
                    @lookup-query="emit('filter-query', { fieldCode: field.fieldCode, query: $event })"
                    @update:items="setFilterItems(field.fieldCode, $event)"
                    @update:raw="setFilterRaw(field.fieldCode, $event)"
                    @update:include-descendants="setFilterIncludeDescendants(field.fieldCode, $event)"
                    @open="void openFilterItem(field)"
                  />
                </NgbFormRow>
              </NgbFormLayout>
            </section>

            <section v-if="active === 'fields' && hasFieldsTab" class="space-y-6">
              <ReportComposerCollectionSection
                v-if="allowsMeasures && measureOptions.length > 0"
                title="Measures"
                add-label="Add measure"
                :items="modelValue.measures"
                :columns="measureSectionColumns"
                empty-message="No measures selected."
                section="measures"
                :row-key="measureRowKey"
                @add="addMeasure"
                @remove="removeMeasure($event)"
                @dragstart="handleCollectionDragStart"
                @dragover="onComposerDragOver"
                @drop="handleCollectionDrop"
              >
                <template #cells="{ item: measure, index }">
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="measure.measureCode"
                        :options="measureOptions"
                        @update:model-value="setMeasureCode(index, String($event ?? ''))"
                      />
                    </div>
                  </td>
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="measureAggregationValue(measure)"
                        :options="measureAggregationOptions(measure.measureCode)"
                        @update:model-value="setMeasureAggregation(index, $event)"
                      />
                    </div>
                  </td>
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbInput
                        variant="grid"
                        :model-value="measureLabelValue(index)"
                        @update:model-value="setMeasureLabel(index, String($event ?? ''))"
                      />
                    </div>
                  </td>
                </template>
              </ReportComposerCollectionSection>

              <ReportComposerCollectionSection
                v-if="allowsDetailFields && selectableFieldOptions.length > 0"
                title="Detail fields"
                add-label="Add field"
                :items="modelValue.detailFields"
                :columns="detailFieldSectionColumns"
                empty-message="No detail fields selected."
                section="detailFields"
                :row-key="detailFieldRowKey"
                @add="addDetailField"
                @remove="removeDetailField($event)"
                @dragstart="handleCollectionDragStart"
                @dragover="onComposerDragOver"
                @drop="handleCollectionDrop"
              >
                <template #cells="{ item: fieldCode, index }">
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="fieldCode"
                        :options="detailFieldOptionsFor(fieldCode)"
                        @update:model-value="setDetailField(index, String($event ?? ''))"
                      />
                    </div>
                  </td>
                </template>
              </ReportComposerCollectionSection>
            </section>

            <section v-if="active === 'sorting' && hasSortingTab" class="space-y-4">
              <ReportComposerCollectionSection
                title="Sorting"
                add-label="Add sort"
                :items="modelValue.sorts"
                :columns="sortingSectionColumns"
                empty-message="No sorting selected."
                section="sorts"
                :row-key="sortRowKey"
                :add-disabled="availableSortTargets.length === 0"
                table-class="min-w-[520px] w-full table-fixed text-sm"
                @add="addSort"
                @remove="removeSort($event)"
                @dragstart="handleCollectionDragStart"
                @dragover="onComposerDragOver"
                @drop="handleCollectionDrop"
              >
                <template #cells="{ item: sort, index }">
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="sort.fieldCode"
                        :options="sortFieldOptionsFor(sort.fieldCode)"
                        @update:model-value="setSortField(index, String($event ?? ''))"
                      />
                    </div>
                  </td>
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="resolveSortAxis(sort)"
                        :options="sortAxisOptionsForField(sort.fieldCode)"
                        @update:model-value="setSortAxis(index, String($event ?? 'row'))"
                      />
                    </div>
                  </td>
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="sort.direction"
                        :options="sortDirections"
                        @update:model-value="setSortDirection(index, $event)"
                      />
                    </div>
                  </td>
                  <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
                    <div class="px-1">
                      <NgbSelect
                        variant="grid"
                        :model-value="sort.timeGrain ?? ''"
                        :options="sortTimeGrainOptionsForFieldAndAxis(sort.fieldCode, resolveSortAxis(sort))"
                        :disabled="sortTimeGrainOptionsForFieldAndAxis(sort.fieldCode, resolveSortAxis(sort)).length === 0 || isSortTimeGrainLocked(sort.fieldCode, resolveSortAxis(sort))"
                        @update:model-value="setSortTimeGrain(index, $event)"
                      />
                    </div>
                  </td>
                </template>
              </ReportComposerCollectionSection>

              <div v-if="sortingHelpLines.length > 0" class="space-y-1 text-xs text-ngb-muted">
                <p v-for="line in sortingHelpLines" :key="line">{{ line }}</p>
              </div>
            </section>

            <section v-if="active === 'variant' && hasVariantTab" class="space-y-4">
              <div class="space-y-3">
                <div class="flex items-center justify-end gap-2">
                  <button
                    type="button"
                    class="ngb-iconbtn"
                    :disabled="variantDisabled || createVariantDisabled || running"
                    title="Create variant"
                    @click="emit('create-variant')"
                  >
                    <NgbIcon name="plus" />
                  </button>
                  <button
                    type="button"
                    class="ngb-iconbtn"
                    :disabled="variantDisabled || editVariantDisabled || running"
                    title="Edit variant"
                    @click="emit('edit-variant')"
                  >
                    <NgbIcon name="edit" />
                  </button>
                  <button
                    type="button"
                    class="ngb-iconbtn"
                    :disabled="variantDisabled || saveVariantDisabled || running"
                    title="Save variant"
                    @click="emit('save-variant')"
                  >
                    <NgbIcon name="save" />
                  </button>
                  <button
                    type="button"
                    class="ngb-iconbtn"
                    :disabled="variantDisabled || deleteVariantDisabled || running"
                    title="Delete variant"
                    @click="emit('delete-variant')"
                  >
                    <NgbIcon name="trash" />
                  </button>
                  <button
                    type="button"
                    class="ngb-iconbtn"
                    :disabled="variantDisabled || resetVariantDisabled || running"
                    title="Reset variant"
                    @click="emit('reset-variant')"
                  >
                    <NgbIcon name="undo" />
                  </button>
                  <button
                    type="button"
                    class="ngb-iconbtn"
                    :disabled="variantDisabled || loadVariantDisabled || running"
                    title="Load variant"
                    @click="emit('load-variant')"
                  >
                    <NgbIcon name="load-variant" />
                  </button>
                </div>

                <NgbSelect
                  :model-value="selectedVariantCode ?? ''"
                  :options="variantOptions ?? []"
                  @update:model-value="emit('update:selectedVariantCode', String($event ?? ''))"
                />

                <div class="text-sm text-ngb-muted">{{ variantSummary ?? 'Using the report definition default layout and filters.' }}</div>
              </div>
            </section>
          </div>
        </template>
      </NgbTabs>
    </div>
  </div>
</template>
