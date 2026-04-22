<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import NgbBadge from '../primitives/NgbBadge.vue'

import { resolveReportCellActionUrl } from './config'
import { ReportRowKind, type ReportCellDto, type ReportSheetDto, type ReportSheetRowDto } from './types'
import type { ReportRouteContext, ReportSourceTrail } from './navigation'

const props = defineProps<{
  sheet: ReportSheetDto | null
  loading?: boolean
  loadingMore?: boolean
  canLoadMore?: boolean
  showEndOfList?: boolean
  loadedCount?: number | null
  totalCount?: number | null
  rowNoun?: string | null
  emptyTitle?: string
  emptyMessage?: string
  currentReportContext?: ReportRouteContext | null
  sourceTrail?: ReportSourceTrail | null
  backTarget?: string | null
}>()

const emit = defineEmits<{
  (e: 'load-more'): void
  (e: 'scroll-top-change', value: number): void
}>()

const hasRows = computed(() => (props.sheet?.rows?.length ?? 0) > 0)
const headerRows = computed(() => props.sheet?.headerRows ?? [])
const hasColumnGroups = computed(() => headerRows.value.length > 0)
const rowAxisColumnCount = computed(() => {
  if (!hasColumnGroups.value) return 0

  const firstHeaderRow = headerRows.value[0]
  if (!firstHeaderRow) return 0

  const headerDepth = headerRows.value.length
  let count = 0
  for (const cell of firstHeaderRow.cells) {
    if ((cell.rowSpan ?? 1) !== headerDepth) break
    count += 1
  }

  return count
})
const totalColumnStartIndex = computed(() => {
  if (!hasColumnGroups.value) return -1
  return props.sheet?.columns?.findIndex(column => column.semanticRole === 'pivot-total') ?? -1
})
const totalMeasureColumnCount = computed(() => {
  if (!hasColumnGroups.value) return 0
  return props.sheet?.columns?.filter(column => column.semanticRole === 'pivot-total').length ?? 0
})

const router = useRouter()
const decimalFormatter = new Intl.NumberFormat(undefined, {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})
const integerFormatter = new Intl.NumberFormat(undefined, {
  maximumFractionDigits: 0,
})
const scrollHost = ref<HTMLDivElement | null>(null)
const loadMoreSentinel = ref<HTMLDivElement | null>(null)
let loadMoreObserver: IntersectionObserver | null = null
let loadMoreRequestPending = false

function drilldownRoute(cell: ReportCellDto): string | null {
  return resolveReportCellActionUrl(cell.action, {
    currentReportContext: props.currentReportContext ?? null,
    sourceTrail: props.sourceTrail ?? null,
    backTarget: props.backTarget ?? null,
  })
}

async function onCellActivate(cell: ReportCellDto) {
  const to = drilldownRoute(cell)
  if (!to) return
  await router.push(to)
}

function rowKindLabel(row: ReportSheetRowDto): string | null {
  switch (row.rowKind) {
    case ReportRowKind.Group: return 'Group'
    case ReportRowKind.Subtotal: return 'Subtotal'
    case ReportRowKind.Total: return 'Total'
    default: return null
  }
}

function rowClass(row: ReportSheetRowDto): string {
  if (hasColumnGroups.value) {
    switch (row.rowKind) {
      case ReportRowKind.Group:
        return 'bg-ngb-card font-medium'
      case ReportRowKind.Subtotal:
        return 'bg-[rgba(11,60,93,.04)] font-medium'
      case ReportRowKind.Total:
        return 'bg-[rgba(11,60,93,.08)] font-semibold'
      default:
        return 'bg-ngb-card'
    }
  }

  switch (row.rowKind) {
    case ReportRowKind.Group:
      return 'bg-[var(--ngb-row-hover)] font-medium'
    case ReportRowKind.Subtotal:
      return 'bg-[rgba(11,60,93,.06)] font-medium'
    case ReportRowKind.Total:
      return 'bg-[rgba(11,60,93,.10)] font-semibold'
    default:
      return 'bg-ngb-card'
  }
}

function isSubtotalOrTotal(row: ReportSheetRowDto): boolean {
  return row.rowKind === ReportRowKind.Subtotal
    || row.rowKind === ReportRowKind.Total
    || row.rowKind === 'Subtotal'
    || row.rowKind === 'Total'
    || row.rowKind === 'subtotal'
    || row.rowKind === 'total'
}

function normalizeValueType(valueType?: string | null): string {
  return String(valueType ?? '').trim().toLowerCase()
}

function isDecimalValueType(valueType?: string | null): boolean {
  const normalized = normalizeValueType(valueType)
  return normalized === 'decimal' || normalized === 'double' || normalized === 'float' || normalized === 'single'
}

function normalizeCount(value: unknown): number | null {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) return null
  return Math.floor(value)
}

function normalizeRowNoun(value: string | null | undefined): string {
  const normalized = String(value ?? '').trim().toLowerCase()
  return normalized.length > 0 ? normalized : 'row'
}

function pluralizeRowNoun(count: number, noun: string): string {
  if (count === 1) return noun
  if (noun.endsWith('s')) return noun
  if (/[^aeiou]y$/i.test(noun)) return `${noun.slice(0, -1)}ies`
  return `${noun}s`
}

function formatCountWithRowNoun(count: number): string {
  const noun = pluralizeRowNoun(count, normalizeRowNoun(props.rowNoun))
  return `${integerFormatter.format(count)} ${noun}`
}

const footerStatusText = computed(() => {
  const loadedCount = normalizeCount(props.loadedCount) ?? (props.sheet?.rows?.length ?? 0)
  const totalCount = normalizeCount(props.totalCount)

  if (props.loadingMore) return `Loading more ${pluralizeRowNoun(Math.max(loadedCount, 2), normalizeRowNoun(props.rowNoun))}…`
  if (props.canLoadMore) return `Loaded ${formatCountWithRowNoun(loadedCount)}. Scroll to continue loading.`
  if (props.showEndOfList) {
    if (totalCount != null && totalCount >= loadedCount) {
      return `Loaded ${formatCountWithRowNoun(totalCount)}. End of list.`
    }

    return `Loaded ${formatCountWithRowNoun(loadedCount)}. End of list.`
  }

  return null
})

function tryFormatDecimal(value: unknown): string | null {
  if (typeof value === 'number' && Number.isFinite(value)) return decimalFormatter.format(value)
  if (typeof value === 'string') {
    const trimmed = value.trim()
    if (trimmed.length === 0) return null
    const normalized = trimmed.replace(/,/g, '')
    const parsed = Number(normalized)
    if (Number.isFinite(parsed)) return decimalFormatter.format(parsed)
  }

  return null
}

function cellText(cell: ReportCellDto): string {
  if (isDecimalValueType(cell.valueType)) {
    const formattedValue = tryFormatDecimal(cell.value)
    if (formattedValue != null) return formattedValue

    const formattedDisplay = tryFormatDecimal(cell.display)
    if (formattedDisplay != null) return formattedDisplay
  }

  if (cell.display != null && String(cell.display).trim().length > 0) return String(cell.display)
  if (cell.value == null) return ''
  if (typeof cell.value === 'string') return cell.value
  if (typeof cell.value === 'number' || typeof cell.value === 'boolean') return String(cell.value)
  return JSON.stringify(cell.value)
}

function headerCellStyle(rowIndex: number) {
  return {
    position: 'sticky',
    top: `${rowIndex * 49}px`,
    zIndex: 10,
  }
}

function rowRenderKey(row: ReportSheetRowDto, rowIndex: number): string {
  return `${String(row.rowKind ?? 'row')}:${String(row.groupKey ?? 'nogroup')}:${rowIndex}`
}

function headerCellClass(cell: ReportCellDto, headerIndex: number, cellIndex: number): string {
  const classes = ['border-b', 'border-ngb-border', 'text-left', 'leading-snug', 'whitespace-pre-wrap', 'break-words']

  if (!hasColumnGroups.value) {
    classes.push('bg-ngb-card', 'px-4', 'py-3', 'font-semibold', 'text-ngb-text')
    return classes.join(' ')
  }

  classes.push('bg-ngb-card', 'px-4')

  if (headerIndex === headerRows.value.length - 1) {
    classes.push('py-3', 'font-semibold', 'text-ngb-text')
  } else {
    classes.push('py-2.5', 'font-medium', 'text-ngb-muted')
  }

  if (headerIndex === 0 && cellIndex === rowAxisColumnCount.value - 1 && rowAxisColumnCount.value > 0)
    classes.push('border-r-2')

  if (cellText(cell) === 'Total')
    classes.push('border-l-2', 'text-ngb-text')

  if (isTotalLeafHeaderCell(headerIndex, cellIndex))
    classes.push('border-l-2')

  return classes.join(' ')
}

function bodyCellClass(row: ReportSheetRowDto, cellIndex: number): string {
  const classes = ['border-b', 'border-ngb-border/70', 'px-4', 'py-3', 'align-top', 'text-ngb-text']

  if (isSubtotalOrTotal(row))
    classes.push('font-semibold')

  if (hasColumnGroups.value) {
    if (cellIndex === rowAxisColumnCount.value - 1 && rowAxisColumnCount.value > 0)
      classes.push('border-r-2')

    if (totalColumnStartIndex.value >= 0 && cellIndex === totalColumnStartIndex.value)
      classes.push('border-l-2')
  }

  return classes.join(' ')
}

function tableClass(): string {
  return hasColumnGroups.value
    ? 'min-w-full border-collapse text-sm'
    : 'min-w-full border-collapse text-sm'
}

function bodyRowHoverClass(): string {
  return hasColumnGroups.value ? 'hover:bg-[rgba(11,60,93,.025)]' : ''
}

function bodyContentClass(cellIndex: number): string {
  if (hasColumnGroups.value && cellIndex === 0) return 'flex items-start gap-2 min-w-[26rem]'
  return 'flex items-start gap-2'
}

function isTotalLeafHeaderCell(headerIndex: number, cellIndex: number): boolean {
  if (!hasColumnGroups.value) return false
  if (headerIndex !== headerRows.value.length - 1) return false
  if (totalMeasureColumnCount.value <= 0) return false

  const firstTotalHeaderIndex = headerRows.value[headerIndex].cells.length - totalMeasureColumnCount.value
  return cellIndex === firstTotalHeaderIndex
}

function shouldEmitLoadMore(): boolean {
  return !!props.canLoadMore && !props.loadingMore && !props.loading && hasRows.value
}

function requestLoadMore() {
  if (loadMoreRequestPending) return
  if (!shouldEmitLoadMore()) return
  loadMoreRequestPending = true
  emit('load-more')
}

function disconnectLoadMoreObserver() {
  loadMoreObserver?.disconnect()
  loadMoreObserver = null
}

function syncLoadMoreObserver() {
  disconnectLoadMoreObserver()

  if (typeof IntersectionObserver === 'undefined') return
  if (!shouldEmitLoadMore()) return
  if (!scrollHost.value || !loadMoreSentinel.value) return

  loadMoreObserver = new IntersectionObserver((entries) => {
    if (entries.some((entry) => entry.isIntersecting)) requestLoadMore()
  }, {
    root: scrollHost.value,
    rootMargin: '0px 0px 320px 0px',
    threshold: 0.01,
  })

  loadMoreObserver.observe(loadMoreSentinel.value)
}

function onScroll() {
  emit('scroll-top-change', scrollHost.value?.scrollTop ?? 0)
}

function restoreScrollTop(value: number) {
  if (!scrollHost.value) return
  scrollHost.value.scrollTop = Math.max(0, Math.floor(value))
}

defineExpose({
  restoreScrollTop,
})

watch(
  () => [props.canLoadMore, props.loadingMore, props.loading, hasRows.value, props.sheet, props.sheet?.rows?.length ?? 0],
  async () => {
    loadMoreRequestPending = false
    await nextTick()
    syncLoadMoreObserver()
  },
)

onMounted(async () => {
  loadMoreRequestPending = false
  await nextTick()
  syncLoadMoreObserver()
  emit('scroll-top-change', scrollHost.value?.scrollTop ?? 0)
})

onBeforeUnmount(() => {
  loadMoreRequestPending = false
  disconnectLoadMoreObserver()
})
</script>

<template>
  <div class="flex min-h-0 min-w-0 flex-1 flex-col">
    <div
      v-if="loading && !hasRows"
      data-testid="report-sheet-loading"
      class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-5 shadow-card"
    >
      <div class="text-sm font-semibold text-ngb-text">Running report…</div>
      <div class="mt-2 text-sm text-ngb-muted">The Report Composer is materializing the first sheet for the selected layout.</div>
    </div>

    <div
      v-else-if="!hasRows"
      data-testid="report-sheet-empty"
      class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-5 shadow-card"
    >
      <div class="text-sm font-semibold text-ngb-text">{{ emptyTitle ?? 'No rows for this layout' }}</div>
      <div class="mt-2 text-sm text-ngb-muted">{{ emptyMessage ?? 'Adjust filters, grouping, or measures and run the report again.' }}</div>
    </div>

    <div
      v-else
      ref="scrollHost"
      data-testid="report-sheet-scroll"
      class="min-h-0 min-w-0 overflow-auto overscroll-contain rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card"
      @scroll="onScroll"
    >
      <table :class="tableClass()" data-testid="report-sheet-table">
        <thead class="bg-ngb-card">
          <template v-if="headerRows.length > 0">
            <tr v-for="(headerRow, headerIndex) in headerRows" :key="headerRow.groupKey ?? `header:${headerIndex}`">
              <th
                v-for="(cell, cellIndex) in headerRow.cells"
                :key="`${headerIndex}:${cellIndex}`"
                :class="headerCellClass(cell, headerIndex, cellIndex)"
                :colspan="cell.colSpan ?? 1"
                :rowspan="cell.rowSpan ?? 1"
                :style="headerCellStyle(headerIndex)"
              >
                <button
                  v-if="drilldownRoute(cell)"
                  type="button"
                  class="cursor-pointer whitespace-pre-wrap break-words text-left hover:underline"
                  @click="onCellActivate(cell)"
                >
                  {{ cellText(cell) }}
                </button>
                <span v-else>{{ cellText(cell) }}</span>
              </th>
            </tr>
          </template>

          <tr v-else class="sticky top-0 z-10 bg-ngb-card">
            <th
              v-for="column in sheet?.columns ?? []"
              :key="column.code"
              class="border-b border-ngb-border px-4 py-3 text-left font-semibold leading-snug text-ngb-text whitespace-pre-wrap break-words"
            >
              {{ column.title }}
            </th>
          </tr>
        </thead>

        <tbody>
          <tr
            v-for="(row, rowIndex) in sheet?.rows ?? []"
            :key="rowRenderKey(row, rowIndex)"
            :class="[rowClass(row), bodyRowHoverClass()]"
          >
            <td
              v-for="(cell, cellIndex) in row.cells"
              :key="`${rowRenderKey(row, rowIndex)}:${cellIndex}`"
              :class="bodyCellClass(row, cellIndex)"
              :colspan="cell.colSpan ?? 1"
              :rowspan="cell.rowSpan ?? 1"
            >
              <div
                :class="bodyContentClass(cellIndex)"
                :style="cellIndex === 0 ? { paddingLeft: `${(row.outlineLevel ?? 0) * 16}px` } : undefined"
              >
                <NgbBadge v-if="cellIndex === 0 && rowKindLabel(row)" tone="neutral">{{ rowKindLabel(row) }}</NgbBadge>
                <button
                  v-if="drilldownRoute(cell)"
                  type="button"
                  class="cursor-pointer whitespace-pre-wrap break-words text-left hover:underline"
                  :class="isSubtotalOrTotal(row) ? 'font-semibold' : undefined"
                  @click="onCellActivate(cell)"
                >
                  {{ cellText(cell) }}
                </button>
                <span v-else class="whitespace-pre-wrap break-words" :class="isSubtotalOrTotal(row) ? 'font-semibold' : undefined">{{ cellText(cell) }}</span>
              </div>
            </td>
          </tr>
        </tbody>
      </table>

      <div ref="loadMoreSentinel" class="h-px w-full" aria-hidden="true" />

      <div
        v-if="canLoadMore || loadingMore || showEndOfList"
        class="sticky bottom-0 flex items-center justify-between gap-3 border-t border-ngb-border bg-ngb-card/95 px-4 py-3 backdrop-blur"
      >
        <div class="text-sm text-ngb-muted">{{ footerStatusText }}</div>

        <button v-if="canLoadMore && !loadingMore" type="button" class="inline-flex items-center justify-center rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 py-1.5 text-sm font-medium text-ngb-text shadow-card transition hover:bg-[var(--ngb-row-hover)]" @click="requestLoadMore">
          Load more
        </button>
      </div>
    </div>
  </div>
</template>
