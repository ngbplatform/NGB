<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import NgbBadge from '../primitives/NgbBadge.vue'
import NgbDatePicker from '../primitives/NgbDatePicker.vue'
import NgbDialog from '../components/NgbDialog.vue'
import NgbDrawer from '../components/NgbDrawer.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import NgbInput from '../primitives/NgbInput.vue'
import NgbLookup from '../primitives/NgbLookup.vue'
import NgbSwitch from '../primitives/NgbSwitch.vue'
import NgbPageHeader from '../site/NgbPageHeader.vue'
import { splitFilterValues, summarizeFilterValues } from '../metadata/filtering'
import { currentRouteBackTarget, navigateBack, resolveBackTarget } from '../router/backNavigation'
import { setCleanRouteQuery } from '../router/queryParams'
import { useCommandPalettePageContext } from '../command-palette/useCommandPalettePageContext'
import type { CommandPaletteItemSeed } from '../command-palette/types'
import { toErrorMessage } from '../utils/errorMessage'
import { stableStringify } from '../utils/stableValue'

import DocumentDateRangeFilter from './NgbReportDateRangeFilter.vue'
import { getConfiguredNgbReporting, resolveReportLookupTarget } from './config'
import ReportComposerPanel from './NgbReportComposerPanel.vue'
import ReportSheet from './NgbReportSheet.vue'
import {
  deleteReportVariant,
  executeReport,
  exportReportXlsx,
  getReportDefinition,
  getReportVariants,
  saveReportVariant,
} from './api'
import {
  applyExecutionRequestToDraft,
  applyVariantToDraft,
  buildExecutionRequest,
  buildExportRequest,
  buildVariantDto,
  coerceReportComposerLookupItem,
  cloneComposerDraft,
  createComposerDraft,
  getReportComposerFilterState,
  getSelectedReportComposerFilterItem,
} from './composer'
import { hydrateReportLookupItemsFromFilters, searchReportLookupItems } from './lookupFilters'
import {
  canAutoRunReport,
  chooseAvailableVariantCode,
  isInlineDateParameterDataType,
  normalizeCode,
  normalizeReportDateValue,
  parameterLabel,
  tryResolveOptionLabel,
  type ReportPageBadge,
} from './pageHelpers'
import { buildAppendRequest, canAppendReportResponse, countLoadedReportRows, mergePagedReportResponses } from './paging'
import {
  clearReportPageExecutionSnapshot,
  clearReportPageScrollTop,
  loadReportPageExecutionSnapshot,
  loadReportPageScrollTop,
  saveReportPageExecutionSnapshot,
  saveReportPageScrollTop,
} from './pageSession'
import type {
  ReportComposerDraft,
  ReportComposerLookupItem,
  ReportDefinitionDto,
  ReportExecutionResponseDto,
  ReportFilterFieldDto,
  ReportVariantDto,
} from './types'
import {
  buildBackToSourceUrl,
  buildCurrentReportContext,
  buildReportPageUrl,
  decodeReportRouteContextParam,
  decodeReportSourceTrailParam,
  type ReportRouteContext,
  type ReportSourceTrail,
  encodeReportRouteContextParam,
  encodeReportSourceTrailParam,
} from './navigation'

const route = useRoute()
const router = useRouter()
const lookupStore = getConfiguredNgbReporting().useLookupStore()

type ReportSheetHandle = {
  restoreScrollTop: (value: number) => void
}

const loadingDefinition = ref(false)
const running = ref(false)
const loadingMore = ref(false)
const downloading = ref(false)
const savingVariant = ref(false)
const error = ref<string | null>(null)
const definition = ref<ReportDefinitionDto | null>(null)
const draft = ref<ReportComposerDraft | null>(null)
const response = ref<ReportExecutionResponseDto | null>(null)
const composerOpen = ref(false)
const lookupItemsByFilterCode = ref<Record<string, ReportComposerLookupItem[]>>({})
const variants = ref<ReportVariantDto[]>([])
const selectedVariantCode = ref('')
const activeVariantCode = ref('')
const variantDialogOpen = ref(false)
const variantDialogMode = ref<'create' | 'edit'>('create')
const variantDialogName = ref('')
const variantDialogDefault = ref(false)
const variantDialogError = ref<string | null>(null)
const deleteVariantOpen = ref(false)
const suppressedBootstrapKey = ref<string | null>(null)
const reportSheetRef = ref<ReportSheetHandle | null>(null)
const consumedAppendCursors = ref<string[]>([])
const pendingScrollRestore = ref(0)

let loadSeq = 0
let runSeq = 0

const reportCode = computed(() => decodeURIComponent(String(route.params.reportCode ?? '').trim()))
const routeBootstrapKey = computed(() => stableStringify({
  reportCode: reportCode.value,
  variant: route.query.variant ?? null,
  ctx: route.query.ctx ?? null,
  src: route.query.src ?? null,
}))
const reportPageStateKey = computed(() => routeBootstrapKey.value)
const pageTitle = computed(() => definition.value?.name || 'Report Composer')
const pageSubtitle = computed(() => definition.value?.description || 'Composable reporting shell')
const selectedVariant = computed(() => variants.value.find((variant) => variant.variantCode === selectedVariantCode.value) ?? null)
const activeVariant = computed(() => variants.value.find((variant) => variant.variantCode === activeVariantCode.value) ?? null)
const variantOptions = computed(() => [
  { value: '', label: 'Definition default' },
  ...variants.value.map((variant) => ({ value: variant.variantCode, label: variant.name })),
])
const variantSummary = computed(() => {
  if (!activeVariant.value) {
    if (!selectedVariant.value) return 'Using the report definition default layout and filters.'
    return `Current draft uses the definition default layout and filters. "${selectedVariant.value.name}" is selected but not loaded.`
  }

  const tags = [activeVariant.value.isShared ? 'Shared' : 'Private']
  if (activeVariant.value.isDefault) tags.push('Default')

  if (!selectedVariant.value && selectedVariantCode.value !== activeVariantCode.value) {
    return `Current draft uses "${activeVariant.value.name}" (${tags.join(' · ')}). Definition default is selected but not loaded.`
  }

  if (selectedVariant.value && selectedVariantCode.value !== activeVariantCode.value) {
    return `Current draft uses "${activeVariant.value.name}" (${tags.join(' · ')}). "${selectedVariant.value.name}" is selected but not loaded.`
  }

  return `Current draft uses "${activeVariant.value.name}" (${tags.join(' · ')}).`
})
const createVariantDisabled = computed(() => !definition.value || !draft.value)
const editVariantDisabled = computed(() => !selectedVariant.value)
const saveVariantDisabled = computed(() => !definition.value || !draft.value || !activeVariant.value || selectedVariantCode.value !== activeVariantCode.value)
const deleteVariantDisabled = computed(() => !selectedVariant.value)
const resetVariantDisabled = computed(() => !definition.value || !draft.value)
const loadVariantDisabled = computed(() => !selectedVariant.value || selectedVariantCode.value === activeVariantCode.value)
const variantDialogTitle = computed(() => variantDialogMode.value === 'create' ? 'Create variant' : 'Edit variant')
const variantDialogSubtitle = computed(() => variantDialogMode.value === 'create'
  ? 'Create a reusable named setup for this composable report.'
  : 'Rename the selected variant or change whether it opens by default.')
const variantDialogConfirmText = computed(() => variantDialogMode.value === 'create' ? 'Create' : 'Save')
const deleteVariantSubtitle = computed(() => {
  if (!selectedVariant.value) return 'Delete the selected variant?'
  return `Delete variant “${selectedVariant.value.name}”? This action cannot be undone.`
})

const inlineDateRange = computed(() => {
  const parameters = (definition.value?.parameters ?? []).filter((parameter) => isInlineDateParameterDataType(parameter.dataType))
  if (parameters.length < 2) return null

  const from = parameters.find((parameter) => {
    const code = normalizeCode(parameter.code)
    return code === 'from_utc' || code === 'frominclusive' || code.endsWith('_from') || code.startsWith('from_')
  })
  const to = parameters.find((parameter) => {
    const code = normalizeCode(parameter.code)
    return code === 'to_utc' || code === 'toinclusive' || code.endsWith('_to') || code.startsWith('to_')
  })

  return from && to ? {
    fromCode: from.code,
    toCode: to.code,
    fromLabel: parameterLabel(from),
    toLabel: parameterLabel(to),
    title: [from.description, to.description].filter((x) => !!String(x ?? '').trim()).join(' · ') || null,
  } : null
})

const inlineDateParameter = computed(() => {
  if (inlineDateRange.value) return null
  const parameters = (definition.value?.parameters ?? []).filter((parameter) => isInlineDateParameterDataType(parameter.dataType))
  if (parameters.length === 0) return null

  const preferred = parameters.find((parameter) => {
    const code = normalizeCode(parameter.code)
    return code.includes('as_of') || code === 'period' || code === 'asofperiod'
  })

  const parameter = (preferred ?? (parameters.length === 1 ? parameters[0] : null)) ?? null
  return parameter ? {
    code: parameter.code,
    label: parameterLabel(parameter),
    hint: parameter.description ?? null,
  } : null
})

const inlineRequiredFilters = computed(() =>
  (definition.value?.filters ?? []).filter((field) => !!field.isRequired && !field.isMulti)
)

const activeParameterBadges = computed<ReportPageBadge[]>(() => {
  if (!definition.value || !draft.value) return []

  return (definition.value.parameters ?? [])
    .filter((parameter) => {
      const code = parameter.code
      if (inlineDateRange.value && (code === inlineDateRange.value.fromCode || code === inlineDateRange.value.toCode)) return false
      if (inlineDateParameter.value && code === inlineDateParameter.value.code) return false
      return true
    })
    .map((parameter) => {
      const raw = String(draft.value?.parameters[parameter.code] ?? '').trim()
      if (raw.length === 0) return null
      return {
        key: `parameter:${parameter.code}`,
        text: `${parameterLabel(parameter)}: ${raw}`,
      }
    })
    .filter((entry): entry is ReportPageBadge => !!entry)
})

const optionalFilterBadges = computed<ReportPageBadge[]>(() => {
  if (!definition.value || !draft.value) return []

  return (definition.value.filters ?? [])
    .filter((field) => !field.isRequired)
    .map((field) => {
      const state = draft.value?.filters[field.fieldCode]
      if (!state) return null

      const itemLabels = state.items
        .map((item) => String(item.label ?? item.id ?? '').trim())
        .filter((label) => label.length > 0)

      const rawValues = itemLabels.length === 0
        ? splitFilterValues(state.raw).map((value) => tryResolveOptionLabel(field, value))
        : []
      const summary = summarizeFilterValues(itemLabels.length > 0 ? itemLabels : rawValues)
      if (!summary) return null

      return {
        key: `filter:${field.fieldCode}`,
        text: `${field.label}: ${summary}`,
      }
    })
    .filter((entry): entry is ReportPageBadge => !!entry)
})

const activeBadges = computed(() => [...activeParameterBadges.value, ...optionalFilterBadges.value])
const currentRouteContext = computed<ReportRouteContext | null>(() => {
  if (!definition.value || !draft.value) return null

  const context = buildCurrentReportContext(definition.value, draft.value)
  return {
    ...context,
    request: {
      ...context.request,
      limit: resolveDefinitionInitialPageLimit(context.request.limit ?? 500),
      variantCode: activeVariantCode.value || null,
    },
  }
})

const sourceTrail = computed<ReportSourceTrail | null>(() => decodeReportSourceTrailParam(route.query.src))
const backToSourceUrl = computed(() => buildBackToSourceUrl(sourceTrail.value, resolveBackTarget(route)))
const currentBackTarget = computed(() => {
  if (!definition.value || !draft.value) return currentRouteBackTarget(route)
  return buildReportPageUrl(reportCode.value, {
    context: currentRouteContext.value,
    sourceTrail: sourceTrail.value,
    backTarget: resolveBackTarget(route),
  })
})

const canLoadMore = computed(() => canAppendReportResponse(response.value) && !loadingDefinition.value && !running.value && !loadingMore.value)
const reportPresentation = computed(() => definition.value?.presentation ?? null)
const emptyReportMessage = computed(() => {
  const configured = String(reportPresentation.value?.emptyStateMessage ?? '').trim()
  if (configured.length > 0) return configured
  return 'Open the Composer, adjust filters, rows, measures, or sorting, and run again.'
})
const hasPagedExecutionState = computed(() => !!response.value && (response.value.hasMore || consumedAppendCursors.value.length > 0))
const showEndOfList = computed(() => hasPagedExecutionState.value && !canLoadMore.value && !loadingMore.value && !running.value && (response.value?.sheet.rows?.length ?? 0) > 0)
const loadedRowCount = computed(() => countLoadedReportRows(response.value?.sheet))
const totalRowCount = computed(() => {
  const total = response.value?.total
  return typeof total === 'number' && Number.isFinite(total) && total >= 0 ? total : null
})
const reportRowNoun = computed(() => {
  const configured = String(reportPresentation.value?.rowNoun ?? '').trim()
  return configured.length > 0 ? configured : 'row'
})

function resolveDefinitionInitialPageLimit(fallback: number) {
  const normalizedFallback = Number.isFinite(fallback) && fallback > 0 ? Math.max(1, Math.floor(fallback)) : 500
  const configured = reportPresentation.value?.initialPageSize
  return typeof configured === 'number' && Number.isFinite(configured) && configured > 0
    ? Math.max(1, Math.floor(configured))
    : normalizedFallback
}

function buildPageExecutionRequest() {
  if (!definition.value || !draft.value) throw new Error('Report definition and draft are required.')

  const request = buildExecutionRequest(definition.value, draft.value)
  return {
    ...request,
    limit: resolveDefinitionInitialPageLimit(request.limit ?? 500),
  }
}

function clearReportPageSnapshot() {
  clearReportPageExecutionSnapshot(reportPageStateKey.value)
  clearReportPageScrollTop(reportPageStateKey.value)
}

function persistReportExecutionSnapshot() {
  if (!response.value) {
    clearReportPageExecutionSnapshot(reportPageStateKey.value)
    return
  }

  saveReportPageExecutionSnapshot(reportPageStateKey.value, response.value, consumedAppendCursors.value)
}

function onReportScrollTopChange(scrollTop: number) {
  saveReportPageScrollTop(reportPageStateKey.value, scrollTop)
}

async function restorePendingScrollPosition() {
  const scrollTop = pendingScrollRestore.value
  if (scrollTop <= 0) return

  pendingScrollRestore.value = 0
  await nextTick()
  reportSheetRef.value?.restoreScrollTop(scrollTop)
}

function tryRestoreReportExecutionSnapshot(): boolean {
  const snapshot = loadReportPageExecutionSnapshot(reportPageStateKey.value)
  if (!snapshot) return false

  response.value = snapshot.response
  consumedAppendCursors.value = snapshot.consumedCursors
  pendingScrollRestore.value = loadReportPageScrollTop(reportPageStateKey.value)
  error.value = null
  return true
}

function updateDraft(mutator: (next: ReportComposerDraft) => void) {
  if (!draft.value) return
  const next = cloneComposerDraft(draft.value)
  mutator(next)
  draft.value = next
}

function filterState(field: ReportFilterFieldDto) {
  return getReportComposerFilterState(draft.value, field)
}

function selectedFilterItem(field: ReportFilterFieldDto): ReportComposerLookupItem | null {
  return getSelectedReportComposerFilterItem(draft.value, field)
}

function setParameterValue(code: string, value: string | null) {
  updateDraft((next) => {
    next.parameters[code] = value ?? ''
  })
}

function setFilterRaw(fieldCode: string, value: string) {
  updateDraft((next) => {
    const state = next.filters[fieldCode]
    if (!state) return
    state.raw = value
  })
}

function setFilterItem(fieldCode: string, value: unknown) {
  const item = coerceReportComposerLookupItem(value)
  updateDraft((next) => {
    const state = next.filters[fieldCode]
    if (!state) return
    state.items = item ? [{ ...item }] : []
    if (item) state.raw = ''
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

async function onFilterQuery(payload: { fieldCode: string; query: string }) {
  const field = definition.value?.filters?.find((entry) => entry.fieldCode === payload.fieldCode)
  if (!field?.lookup) return

  const items = await searchReportLookupItems(lookupStore, field.lookup, payload.query)
  lookupItemsByFilterCode.value = {
    ...lookupItemsByFilterCode.value,
    [payload.fieldCode]: items,
  }
}

async function createDraftFromVariant(definitionValue: ReportDefinitionDto, variant: ReportVariantDto): Promise<ReportComposerDraft> {
  const nextDraft = applyVariantToDraft(definitionValue, variant)
  await hydrateReportLookupItemsFromFilters(lookupStore, definitionValue, nextDraft, variant.filters)
  return nextDraft
}

async function createDraftFromContext(definitionValue: ReportDefinitionDto, context: ReportRouteContext): Promise<ReportComposerDraft> {
  const nextDraft = applyExecutionRequestToDraft(definitionValue, context.request)
  await hydrateReportLookupItemsFromFilters(lookupStore, definitionValue, nextDraft, context.request.filters)
  return nextDraft
}

async function refreshVariants(options?: {
  preferredSelectedVariantCode?: string
  preferredActiveVariantCode?: string
}) {
  const list = await getReportVariants(reportCode.value)
  variants.value = list

  const preferredActive = options?.preferredActiveVariantCode ?? activeVariantCode.value
  activeVariantCode.value = preferredActive && list.some((variant) => variant.variantCode === preferredActive)
    ? preferredActive
    : ''

  const preferredSelected = options?.preferredSelectedVariantCode ?? selectedVariantCode.value
  if (preferredSelected && list.some((variant) => variant.variantCode === preferredSelected)) {
    selectedVariantCode.value = preferredSelected
    return
  }

  if (activeVariantCode.value && list.some((variant) => variant.variantCode === activeVariantCode.value)) {
    selectedVariantCode.value = activeVariantCode.value
    return
  }

  selectedVariantCode.value = ''
}

async function syncRouteStateWithCurrentReportContext() {
  if (!definition.value || !draft.value) return

  const nextQuery: Record<string, unknown> = { ...route.query }
  const nextContext = encodeReportRouteContextParam(currentRouteContext.value)
  const nextSourceTrail = encodeReportSourceTrailParam(sourceTrail.value)

  if (nextContext) nextQuery.ctx = nextContext
  else delete nextQuery.ctx

  if (nextSourceTrail) nextQuery.src = nextSourceTrail
  else delete nextQuery.src

  if (activeVariantCode.value) nextQuery.variant = activeVariantCode.value
  else delete nextQuery.variant

  const currentCtx = String(route.query.ctx ?? '').trim()
  const currentSrc = String(route.query.src ?? '').trim()
  const currentVariant = String(route.query.variant ?? '').trim()
  const nextVariant = String(nextQuery.variant ?? '').trim()

  if (currentCtx === String(nextQuery.ctx ?? '').trim()
    && currentSrc === String(nextQuery.src ?? '').trim()
    && currentVariant === nextVariant) return

  suppressedBootstrapKey.value = stableStringify({
    reportCode: reportCode.value,
    variant: nextQuery.variant ?? null,
    ctx: nextQuery.ctx ?? null,
    src: nextQuery.src ?? null,
  })

  await setCleanRouteQuery(route, router, nextQuery, 'replace')
}

async function runReport() {
  if (!definition.value || !draft.value) return

  const seq = ++runSeq
  running.value = true
  loadingMore.value = false
  error.value = null

  try {
    const result = await executeReport(reportCode.value, buildPageExecutionRequest())
    if (seq !== runSeq) return

    response.value = result
    consumedAppendCursors.value = []
    pendingScrollRestore.value = 0
    await syncRouteStateWithCurrentReportContext()
    persistReportExecutionSnapshot()
    saveReportPageScrollTop(reportPageStateKey.value, 0)
  } catch (err) {
    if (seq !== runSeq) return
    error.value = toErrorMessage(err, 'Failed to execute the report.')
  } finally {
    if (seq === runSeq) running.value = false
  }
}

async function appendReportPage() {
  if (!definition.value || !draft.value || !response.value) return

  const nextCursor = String(response.value.nextCursor ?? '').trim()
  if (!nextCursor || loadingMore.value || running.value) return
  if (consumedAppendCursors.value.includes(nextCursor)) return

  const seq = runSeq
  loadingMore.value = true
  error.value = null

  try {
    const page = await executeReport(reportCode.value, buildAppendRequest(buildPageExecutionRequest(), nextCursor))
    if (seq !== runSeq) return

    response.value = mergePagedReportResponses(response.value, page)
    consumedAppendCursors.value = [...consumedAppendCursors.value, nextCursor]
    persistReportExecutionSnapshot()
  } catch (err) {
    if (seq !== runSeq) return
    error.value = toErrorMessage(err, 'Failed to load more rows.')
  } finally {
    if (seq === runSeq) loadingMore.value = false
  }
}

async function downloadReport() {
  if (!definition.value || !draft.value) return
  downloading.value = true
  error.value = null

  try {
    const file = await exportReportXlsx(reportCode.value, buildExportRequest(definition.value, draft.value))
    const url = URL.createObjectURL(file.blob)
    const link = document.createElement('a')
    link.href = url
    link.download = file.fileName || `${reportCode.value.replace(/[^a-z0-9]+/gi, '-') || 'report'}.xlsx`
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
  } catch (err) {
    error.value = toErrorMessage(err, 'Failed to export the report.')
  } finally {
    downloading.value = false
  }
}

async function loadSelectedVariant() {
  if (!definition.value) return

  const variant = selectedVariant.value
  if (!variant) return

  try {
    draft.value = await createDraftFromVariant(definition.value, variant)
    activeVariantCode.value = variant.variantCode
    selectedVariantCode.value = variant.variantCode
    if (draft.value && canAutoRunReport(definition.value, draft.value)) await runReport()
    else {
      response.value = null
      consumedAppendCursors.value = []
      error.value = null
      await syncRouteStateWithCurrentReportContext()
      clearReportPageSnapshot()
    }
  } catch (err) {
    error.value = toErrorMessage(err, 'Failed to load the report variant.')
  }
}

async function resetToDefault() {
  if (!definition.value) return

  try {
    const defaultVariant = variants.value.find((variant) => !!variant.isDefault) ?? null
    if (defaultVariant) {
      selectedVariantCode.value = defaultVariant.variantCode
      activeVariantCode.value = defaultVariant.variantCode
      draft.value = await createDraftFromVariant(definition.value, defaultVariant)
    } else {
      selectedVariantCode.value = ''
      activeVariantCode.value = ''
      draft.value = createComposerDraft(definition.value)
    }

    if (draft.value && canAutoRunReport(definition.value, draft.value)) await runReport()
    else {
      response.value = null
      consumedAppendCursors.value = []
      error.value = null
      await syncRouteStateWithCurrentReportContext()
      clearReportPageSnapshot()
    }
  } catch (err) {
    error.value = toErrorMessage(err, 'Failed to reset the report.')
  }
}

function openCreateVariantDialog() {
  variantDialogMode.value = 'create'
  variantDialogName.value = ''
  variantDialogDefault.value = false
  variantDialogError.value = null
  variantDialogOpen.value = true
}

function openEditVariantDialog() {
  if (!selectedVariant.value) return

  variantDialogMode.value = 'edit'
  variantDialogName.value = selectedVariant.value.name ?? ''
  variantDialogDefault.value = !!selectedVariant.value.isDefault
  variantDialogError.value = null
  variantDialogOpen.value = true
}

async function submitVariantDialog() {
  if (!definition.value || !draft.value) return

  const name = String(variantDialogName.value ?? '').trim()
  savingVariant.value = true
  variantDialogError.value = null
  error.value = null

  try {
    if (variantDialogMode.value === 'create') {
      const variantCode = chooseAvailableVariantCode(name, variants.value)
      const payload = buildVariantDto(definition.value, draft.value, {
        variantCode,
        name,
        isDefault: variantDialogDefault.value,
        isShared: true,
      })

      const saved = await saveReportVariant(reportCode.value, variantCode, payload)
      activeVariantCode.value = saved.variantCode
      selectedVariantCode.value = saved.variantCode
      await refreshVariants({
        preferredActiveVariantCode: saved.variantCode,
        preferredSelectedVariantCode: saved.variantCode,
      })
      variantDialogOpen.value = false
      await syncRouteStateWithCurrentReportContext()
      return
    }

    const variant = selectedVariant.value
    if (!variant) return

    const payload: ReportVariantDto = {
      ...variant,
      name,
      isDefault: variantDialogDefault.value,
    }

    const saved = await saveReportVariant(reportCode.value, variant.variantCode, payload)
    const shouldKeepActive = activeVariantCode.value === variant.variantCode ? saved.variantCode : activeVariantCode.value
    await refreshVariants({
      preferredActiveVariantCode: shouldKeepActive,
      preferredSelectedVariantCode: saved.variantCode,
    })
    variantDialogOpen.value = false
    await syncRouteStateWithCurrentReportContext()
  } catch (err) {
    variantDialogError.value = toErrorMessage(err, 'Failed to save the report variant.')
  } finally {
    savingVariant.value = false
  }
}

function openDeleteVariantDialog() {
  if (!selectedVariant.value) return
  deleteVariantOpen.value = true
}

async function deleteSelectedVariant() {
  const variant = selectedVariant.value
  if (!variant) return

  savingVariant.value = true
  error.value = null

  try {
    await deleteReportVariant(reportCode.value, variant.variantCode)
    deleteVariantOpen.value = false

    const wasActive = activeVariantCode.value === variant.variantCode
    const preferredSelected = selectedVariantCode.value === variant.variantCode
      ? activeVariantCode.value
      : selectedVariantCode.value

    if (wasActive) {
      await refreshVariants({
        preferredActiveVariantCode: '',
        preferredSelectedVariantCode: '',
      })
      await resetToDefault()
      return
    }

    await refreshVariants({
      preferredActiveVariantCode: activeVariantCode.value,
      preferredSelectedVariantCode: preferredSelected,
    })
    await syncRouteStateWithCurrentReportContext()
  } catch (err) {
    error.value = toErrorMessage(err, 'Failed to delete the report variant.')
  } finally {
    savingVariant.value = false
  }
}

async function saveCurrentVariant() {
  if (!definition.value || !draft.value || !activeVariant.value) return

  savingVariant.value = true
  error.value = null

  try {
    const saved = await saveReportVariant(reportCode.value, activeVariant.value.variantCode, buildVariantDto(definition.value, draft.value, {
      variantCode: activeVariant.value.variantCode,
      name: activeVariant.value.name,
      isDefault: !!activeVariant.value.isDefault,
      isShared: activeVariant.value.isShared !== false,
    }))

    activeVariantCode.value = saved.variantCode
    selectedVariantCode.value = saved.variantCode
    await refreshVariants({
      preferredActiveVariantCode: saved.variantCode,
      preferredSelectedVariantCode: saved.variantCode,
    })
    await syncRouteStateWithCurrentReportContext()
  } catch (err) {
    error.value = toErrorMessage(err, 'Failed to save the report variant.')
  } finally {
    savingVariant.value = false
  }
}

async function loadDefinitionAndRun() {
  const code = reportCode.value
  if (!code) return

  runSeq += 1
  const seq = ++loadSeq
  loadingDefinition.value = true
  running.value = false
  error.value = null
  definition.value = null
  draft.value = null
  response.value = null
  loadingMore.value = false
  composerOpen.value = false
  variantDialogOpen.value = false
  deleteVariantOpen.value = false
  lookupItemsByFilterCode.value = {}
  variants.value = []
  selectedVariantCode.value = ''
  activeVariantCode.value = ''
  consumedAppendCursors.value = []
  pendingScrollRestore.value = 0

  try {
    const [loadedDefinition, loadedVariants] = await Promise.all([
      getReportDefinition(code),
      getReportVariants(code),
    ])

    if (seq !== loadSeq) return

    definition.value = loadedDefinition
    variants.value = loadedVariants

    const requestedVariantCode = String(route.query.variant ?? '').trim()
    const requestedVariant = requestedVariantCode
      ? loadedVariants.find((variant) => variant.variantCode === requestedVariantCode) ?? null
      : null
    const defaultVariant = loadedVariants.find((variant) => !!variant.isDefault) ?? null
    const routeContext = decodeReportRouteContextParam(route.query.ctx)

    if (routeContext && routeContext.reportCode === loadedDefinition.reportCode) {
      const contextVariantCode = String(routeContext.request.variantCode ?? requestedVariantCode).trim()
      activeVariantCode.value = contextVariantCode && loadedVariants.some((variant) => variant.variantCode === contextVariantCode)
        ? contextVariantCode
        : ''
      selectedVariantCode.value = activeVariantCode.value
      draft.value = await createDraftFromContext(loadedDefinition, routeContext)
    } else if (requestedVariant) {
      selectedVariantCode.value = requestedVariant.variantCode
      activeVariantCode.value = requestedVariant.variantCode
      draft.value = await createDraftFromVariant(loadedDefinition, requestedVariant)
    } else if (defaultVariant) {
      selectedVariantCode.value = defaultVariant.variantCode
      activeVariantCode.value = defaultVariant.variantCode
      draft.value = await createDraftFromVariant(loadedDefinition, defaultVariant)
    } else {
      selectedVariantCode.value = ''
      activeVariantCode.value = ''
      draft.value = createComposerDraft(loadedDefinition)
    }

    if (tryRestoreReportExecutionSnapshot()) {
      await restorePendingScrollPosition()
      return
    }

    if (draft.value && canAutoRunReport(loadedDefinition, draft.value)) await runReport()
    else {
      response.value = null
      error.value = null
      clearReportPageSnapshot()
    }
  } catch (err) {
    if (seq !== loadSeq) return
    error.value = toErrorMessage(err, 'Failed to load the report definition.')
  } finally {
    if (seq === loadSeq) loadingDefinition.value = false
  }
}

const commandPaletteActions = computed<CommandPaletteItemSeed[]>(() => {
  const actions: CommandPaletteItemSeed[] = []

  if (activeVariant.value) {
    actions.push({
      key: 'current:save-variant',
      group: 'actions',
      kind: 'command',
      scope: 'commands',
      title: 'Save current variant',
      subtitle: activeVariant.value.name,
      icon: 'save',
      badge: 'Save',
      hint: null,
      route: null,
      commandCode: 'save-variant',
      status: null,
      openInNewTabSupported: false,
      keywords: ['save variant', activeVariant.value.name],
      defaultRank: 978,
      isCurrentContext: true,
      perform: saveCurrentVariant,
    })
  }

  if (selectedVariant.value && selectedVariantCode.value !== activeVariantCode.value) {
    actions.push({
      key: 'current:load-variant',
      group: 'actions',
      kind: 'command',
      scope: 'commands',
      title: 'Load selected variant',
      subtitle: selectedVariant.value.name,
      icon: 'load-variant',
      badge: 'Load',
      hint: null,
      route: null,
      commandCode: 'load-variant',
      status: null,
      openInNewTabSupported: false,
      keywords: ['load variant', selectedVariant.value.name],
      defaultRank: 976,
      isCurrentContext: true,
      perform: loadSelectedVariant,
    })
  }

  return actions
})

useCommandPalettePageContext(() => ({
  entityType: 'report',
  documentType: null,
  catalogType: null,
  entityId: null,
  title: pageTitle.value,
  actions: commandPaletteActions.value,
}))

watch(reportPageStateKey, (nextKey, prevKey) => {
  if (nextKey === prevKey || !response.value) return
  persistReportExecutionSnapshot()
}, { flush: 'post' })

watch(response, async (nextResponse) => {
  if (!nextResponse) return
  await restorePendingScrollPosition()
}, { flush: 'post' })

watch(routeBootstrapKey, (nextKey) => {
  if (suppressedBootstrapKey.value === nextKey) {
    suppressedBootstrapKey.value = null
    return
  }

  void loadDefinitionAndRun()
}, { immediate: true })
</script>

<template>
  <div class="h-full min-h-0 flex flex-col" data-testid="report-page">
    <NgbPageHeader :title="pageTitle" can-back @back="navigateBack(router, route, backToSourceUrl)">
      <template #secondary>
        <div v-if="pageSubtitle" class="text-xs text-ngb-muted truncate">{{ pageSubtitle }}</div>
      </template>
      <template #actions>
        <div class="flex flex-wrap items-center justify-end gap-2">
          <div
            v-for="field in inlineRequiredFilters"
            :key="field.fieldCode"
            class="w-[17rem] max-w-full"
          >
            <NgbLookup
              v-if="field.lookup"
              :model-value="selectedFilterItem(field)"
              :items="lookupItemsByFilterCode[field.fieldCode] ?? []"
              :disabled="loadingDefinition || running || downloading || !draft"
              :show-open="!!selectedFilterItem(field)"
              :show-clear="!!selectedFilterItem(field)"
              :placeholder="`Type ${field.label.toLowerCase()}…`"
              variant="compact"
              @query="onFilterQuery({ fieldCode: field.fieldCode, query: $event })"
              @update:model-value="setFilterItem(field.fieldCode, $event)"
              @open="void openFilterItem(field)"
            />

            <NgbInput
              v-else
              :model-value="filterState(field).raw"
              :disabled="loadingDefinition || running || downloading || !draft"
              :placeholder="field.label"
              @update:model-value="setFilterRaw(field.fieldCode, String($event ?? ''))"
            />
          </div>

          <DocumentDateRangeFilter
            v-if="inlineDateRange && draft"
            :from-date="String(draft.parameters[inlineDateRange.fromCode] ?? '')"
            :to-date="String(draft.parameters[inlineDateRange.toCode] ?? '')"
            :from-placeholder="inlineDateRange.fromLabel"
            :to-placeholder="inlineDateRange.toLabel"
            :title="inlineDateRange.title ?? undefined"
            :disabled="loadingDefinition || running || downloading || !draft"
            @update:from-date="setParameterValue(inlineDateRange.fromCode, $event)"
            @update:to-date="setParameterValue(inlineDateRange.toCode, $event)"
          />

          <div
            v-else-if="inlineDateParameter && draft"
            class="relative inline-flex h-[26px] items-stretch rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card text-xs shadow-card"
            :title="inlineDateParameter.hint ?? undefined"
          >
            <div class="h-full w-[10rem]">
              <NgbDatePicker
                :model-value="normalizeReportDateValue(draft.parameters[inlineDateParameter.code])"
                :placeholder="inlineDateParameter.label"
                grouped
                :disabled="loadingDefinition || running || downloading || !draft"
                @update:model-value="setParameterValue(inlineDateParameter.code, $event)"
              />
            </div>
          </div>

          <div class="mx-1 h-5 w-px bg-ngb-border" aria-hidden="true" />

          <div class="flex items-center gap-1.5">
            <button type="button" class="ngb-iconbtn" :disabled="loadingDefinition || running || !definition || !draft" title="Run" @click="runReport">
              <NgbIcon name="play" :size="50" />
            </button>
            <button type="button" class="ngb-iconbtn" :disabled="loadingDefinition || !definition" title="Composer" @click="composerOpen = true">
              <NgbIcon name="composer" />
            </button>
            <button type="button" class="ngb-iconbtn" :disabled="loadingDefinition || running || downloading || !definition || !draft || !definition?.capabilities?.allowsXlsxExport" title="Download" @click="downloadReport">
              <NgbIcon name="download" />
            </button>
          </div>
        </div>
      </template>
    </NgbPageHeader>

    <div class="flex-1 min-h-0 flex flex-col gap-4 overflow-hidden p-6" data-testid="report-page-content">
      <div v-if="error" class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100">
        {{ error }}
      </div>

      <div v-if="activeBadges.length > 0" class="-mb-1 flex flex-wrap items-center gap-2" data-testid="report-page-active-badges">
        <NgbBadge v-for="badge in activeBadges" :key="badge.key" tone="neutral">{{ badge.text }}</NgbBadge>
      </div>

      <div
        v-if="loadingDefinition && !definition"
        class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-5 shadow-card"
      >
        <div class="text-sm font-semibold text-ngb-text">Loading report definition…</div>
        <div class="mt-2 text-sm text-ngb-muted">Fetching the report metadata and default layout.</div>
      </div>

      <div v-else-if="definition" class="flex min-h-0 flex-1 flex-col overflow-hidden">
        <ReportSheet
          ref="reportSheetRef"
          class="min-h-0 flex-1"
          :sheet="response?.sheet ?? null"
          :loading="running"
          :loading-more="loadingMore"
          :can-load-more="canLoadMore"
          :current-report-context="currentRouteContext"
          :source-trail="sourceTrail"
          :back-target="currentBackTarget"
          :show-end-of-list="showEndOfList"
          :loaded-count="loadedRowCount"
          :total-count="totalRowCount"
          :row-noun="reportRowNoun"
          empty-title="No rows for this layout"
          :empty-message="emptyReportMessage"
          @load-more="appendReportPage"
          @scroll-top-change="onReportScrollTopChange"
        />
      </div>
    </div>
  </div>

  <NgbDrawer v-model:open="composerOpen" title="" hide-header flush-body>
    <ReportComposerPanel
      v-if="definition && draft"
      :definition="definition"
      :model-value="draft"
      :lookup-items-by-filter-code="lookupItemsByFilterCode"
      :running="running"
      :variant-options="variantOptions"
      :selected-variant-code="selectedVariantCode"
      :variant-summary="variantSummary"
      :variant-disabled="loadingDefinition || savingVariant || !definition || !draft"
      :create-variant-disabled="createVariantDisabled"
      :edit-variant-disabled="editVariantDisabled"
      :save-variant-disabled="saveVariantDisabled"
      :delete-variant-disabled="deleteVariantDisabled"
      :reset-variant-disabled="resetVariantDisabled"
      :load-variant-disabled="loadVariantDisabled"
      @update:model-value="draft = $event"
      @filter-query="onFilterQuery"
      @update:selected-variant-code="selectedVariantCode = $event"
      @create-variant="openCreateVariantDialog"
      @edit-variant="openEditVariantDialog"
      @save-variant="saveCurrentVariant"
      @delete-variant="openDeleteVariantDialog"
      @reset-variant="resetToDefault"
      @load-variant="loadSelectedVariant"
      @run="runReport(); composerOpen = false"
      @close="composerOpen = false"
    />
  </NgbDrawer>

  <NgbDialog
    :open="variantDialogOpen"
    :title="variantDialogTitle"
    :subtitle="variantDialogSubtitle"
    :confirm-text="variantDialogConfirmText"
    cancel-text="Cancel"
    :confirm-loading="savingVariant"
    @update:open="variantDialogOpen = $event; variantDialogError = null"
    @confirm="submitVariantDialog"
  >
    <div class="space-y-4">
      <div v-if="variantDialogError" class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100">
        {{ variantDialogError }}
      </div>

      <div>
        <div class="mb-1 text-sm font-medium text-ngb-text">Variant name</div>
        <NgbInput v-model="variantDialogName" placeholder="Month-end ledger" />
      </div>

      <div class="flex items-center justify-between gap-3 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 py-2">
        <div>
          <div class="text-sm font-medium text-ngb-text">Set as default</div>
          <div class="text-xs text-ngb-muted">Use this variant automatically when the page opens.</div>
        </div>
        <NgbSwitch v-model="variantDialogDefault" />
      </div>
    </div>
  </NgbDialog>

  <NgbDialog
    :open="deleteVariantOpen"
    title="Delete variant"
    :subtitle="deleteVariantSubtitle"
    confirm-text="Delete"
    cancel-text="Cancel"
    danger
    :confirm-loading="savingVariant"
    @update:open="deleteVariantOpen = $event"
    @confirm="deleteSelectedVariant"
  >
    <div class="text-sm text-ngb-muted">
      The current report draft will stay unchanged unless you delete the variant that is currently loaded.
    </div>
  </NgbDialog>
</template>
