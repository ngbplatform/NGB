<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch, type CSSProperties } from 'vue'
import { useRouter } from 'vue-router'
import NgbBadge from '../primitives/NgbBadge.vue'

import { resolveReportCellActionUrl } from './config'
import { ReportRowKind, type ReportCellDto, type ReportSheetDto, type ReportSheetRowDto } from './types'
import type { ReportDisplayRow, GroupAction } from './groupTree'
import type { ReportRouteContext, ReportSourceTrail } from './navigation'

const props = defineProps<{
  sheet: ReportSheetDto | null
  loading?: boolean
  loadingMore?: boolean
  canLoadMore?: boolean
  canLoadPrevious?: boolean
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
  (e: 'load-previous'): void
  (e: 'group-action', id: string, action: GroupAction): void
  (e: 'scroll-top-change', value: number): void
}>()

const columns = computed(() => props.sheet!.columns)
const rows = computed(() => (props.sheet?.rows ?? []) as ReportDisplayRow[])
const hasRows = computed(() => rows.value.length > 0)
const headerRows = computed(() => props.sheet?.headerRows ?? [])
const hasColumnGroups = computed(() => headerRows.value.length > 0)
const rowAxisColumnCount = computed(() => {
  const firstHeaderRow = headerRows.value[0]!

  const headerDepth = headerRows.value.length
  let count = 0
  for (const cell of firstHeaderRow.cells) {
    if ((cell.rowSpan ?? 1) !== headerDepth) break
    count += 1
  }

  return count
})
const totalColumnStartIndex = computed(() => {
  return columns.value.findIndex(column => column.semanticRole === 'pivot-total')
})
const totalMeasureColumnCount = computed(() => {
  return columns.value.filter(column => column.semanticRole === 'pivot-total').length
})

const DEFAULT_REPORT_COLUMN_WIDTH = 160
const REPORT_HIERARCHY_COLUMN_WIDTH = 416

// The mounted row window must not determine column widths: width changes alter
// wrapping and row measurements, which can repeatedly change that same window.
const columnWidths = computed(() => columns.value.map((column, index) => {
  if (column.width != null && Number.isFinite(column.width) && column.width > 0) return column.width
  return hasColumnGroups.value && index === 0 ? REPORT_HIERARCHY_COLUMN_WIDTH : DEFAULT_REPORT_COLUMN_WIDTH
}))
const tableStyle = computed<CSSProperties>(() => ({
  width: `${columnWidths.value.reduce((total, width) => total + width, 0)}px`,
}))

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
const scrollTop = ref(0)
const viewportHeight = ref(600)
const measuredHeightVersion = ref(0)
const measuredRowHeights = new WeakMap<ReportSheetRowDto, number>()
const rowByElement = new WeakMap<Element, ReportSheetRowDto>()
const elementByRow = new WeakMap<ReportSheetRowDto, Element>()
let loadMoreObserver: IntersectionObserver | null = null
let viewportResizeObserver: ResizeObserver | null = null
let rowResizeObserver: ResizeObserver | null = null
let loadMoreRequestPending = false
const pendingGroups = new Set<string>()
let scrollFrame: number | null = null
let measurementFrame: number | null = null
let pendingScrollTop = 0

const REPORT_VIRTUALIZATION_THRESHOLD = 200
const ESTIMATED_REPORT_ROW_HEIGHT = 49
const REPORT_VIRTUAL_OVERSCAN_PX = 600

const virtualLayout = computed(() => {
  void measuredHeightVersion.value
  const source = rows.value
  const offsets = new Array<number>(source.length + 1)
  offsets[0] = 0
  for (let index = 0; index < source.length; index += 1) {
    offsets[index + 1] = offsets[index]! + (measuredRowHeights.get(source[index]!) ?? ESTIMATED_REPORT_ROW_HEIGHT)
  }
  return offsets
})

function firstRowEndingAfter(offsets: readonly number[], target: number): number {
  let low = 0
  let high = Math.max(0, offsets.length - 1)
  while (low < high) {
    const middle = Math.floor((low + high) / 2)
    if (offsets[middle + 1]! <= target) low = middle + 1
    else high = middle
  }
  return low
}

const virtualWindow = computed(() => {
  const source = rows.value
  if (source.length <= REPORT_VIRTUALIZATION_THRESHOLD) {
    return {
      top: 0,
      bottom: 0,
      entries: source.map((row, index) => ({ row, index })),
    }
  }

  const offsets = virtualLayout.value
  const startTarget = Math.max(0, scrollTop.value - REPORT_VIRTUAL_OVERSCAN_PX)
  const endTarget = scrollTop.value + Math.max(1, viewportHeight.value) + REPORT_VIRTUAL_OVERSCAN_PX
  const start = Math.min(source.length, firstRowEndingAfter(offsets, startTarget))
  const end = Math.min(source.length, firstRowEndingAfter(offsets, endTarget) + 1)

  return {
    top: offsets[start]!,
    bottom: Math.max(0, offsets[source.length]! - offsets[end]!),
    entries: source.slice(start, end).map((row, index) => ({ row, index: start + index })),
  }
})

function observeVirtualRow(element: unknown, row: ReportSheetRowDto) {
  const previous = elementByRow.get(row)
  if (previous === element) return
  if (previous) {
    rowResizeObserver?.unobserve(previous)
    rowByElement.delete(previous)
    elementByRow.delete(row)
  }
  if (!(element instanceof Element)) return
  elementByRow.set(row, element)
  rowByElement.set(element, row)
  rowResizeObserver?.observe(element)
}

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
}

function normalizeValueType(valueType?: string | null): string {
  return String(valueType ?? '').trim().toLowerCase()
}

function isDecimalValueType(valueType?: string | null): boolean {
  const normalized = normalizeValueType(valueType)
  return normalized === 'decimal' || normalized === 'double' || normalized === 'float' || normalized === 'single'
}

function normalizeCount(value: number | null | undefined): number | null {
  if (value == null || !Number.isFinite(value) || value < 0) return null
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
  const loadedCount = normalizeCount(props.loadedCount) ?? rows.value.length
  const totalCount = normalizeCount(props.totalCount)

  if (props.loadingMore) return `Loading more ${pluralizeRowNoun(Math.max(loadedCount, 2), normalizeRowNoun(props.rowNoun))}…`
  if (props.canLoadMore) return `Loaded ${formatCountWithRowNoun(loadedCount)}. Scroll to continue loading.`
  if (totalCount != null && totalCount >= loadedCount) {
    return `Loaded ${formatCountWithRowNoun(totalCount)}. End of list.`
  }

  return `Loaded ${formatCountWithRowNoun(loadedCount)}. End of list.`
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

function headerCellStyle(rowIndex: number): CSSProperties {
  return {
    position: 'sticky',
    top: `${rowIndex * 49}px`,
    zIndex: 10,
  }
}

function rowRenderKey(row: ReportSheetRowDto, rowIndex: number): string {
  return (row as ReportDisplayRow).viewKey ?? `${String(row.rowKind)}:${String(row.groupKey ?? 'nogroup')}:${rowIndex}`
}

function headerCellClass(cell: ReportCellDto, headerIndex: number, cellIndex: number): string {
  const classes = ['border-b', 'border-ngb-border', 'text-left', 'leading-snug', 'whitespace-pre-wrap', 'break-words']

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

const tableClass = 'min-w-full table-fixed border-collapse text-sm'

function bodyRowHoverClass(): string {
  return hasColumnGroups.value ? 'hover:bg-[rgba(11,60,93,.025)]' : ''
}

function isTotalLeafHeaderCell(headerIndex: number, cellIndex: number): boolean {
  if (headerIndex !== headerRows.value.length - 1) return false
  if (totalMeasureColumnCount.value <= 0) return false

  const firstTotalHeaderIndex = headerRows.value[headerIndex]!.cells.length - totalMeasureColumnCount.value
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
  loadMoreObserver = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue
      if (entry.target === loadMoreSentinel.value) requestLoadMore()
      else {
        const id = (entry.target as HTMLElement).dataset.groupNext
        if (id && !pendingGroups.has(id)) {
          pendingGroups.add(id)
          emit('group-action', id, 'next')
        }
      }
    }
  }, {
    root: scrollHost.value!,
    rootMargin: '0px 0px 320px 0px',
    threshold: 0.01,
  })

  if (loadMoreSentinel.value && shouldEmitLoadMore()) loadMoreObserver.observe(loadMoreSentinel.value)
  for (const element of scrollHost.value?.querySelectorAll('[data-group-next]') ?? []) loadMoreObserver.observe(element)
}

function onScroll(event: Event) {
  pendingScrollTop = (event.currentTarget as HTMLDivElement).scrollTop
  scrollTop.value = pendingScrollTop
  if (pendingScrollTop < 320 && props.canLoadPrevious && !props.loadingMore && !props.loading) emit('load-previous')
  if (scrollFrame != null) return

  scrollFrame = window.requestAnimationFrame(() => {
    scrollFrame = null
    emit('scroll-top-change', pendingScrollTop)
  })
}

function restoreScrollTop(value: number) {
  if (!scrollHost.value) return
  const normalized = Math.max(0, Math.floor(value))
  scrollHost.value.scrollTop = normalized
  pendingScrollTop = scrollHost.value.scrollTop
  scrollTop.value = scrollHost.value.scrollTop
}

function captureAnchor() {
  const index = Math.min(rows.value.length - 1, firstRowEndingAfter(virtualLayout.value, scrollTop.value))
  const row = rows.value[index]
  return row ? { key: rowRenderKey(row, index), offset: virtualLayout.value[index]! - scrollTop.value } : null
}

function restoreAnchor(anchor: { key: string; offset: number }) {
  const index = rows.value.findIndex((row, index) => rowRenderKey(row, index) === anchor.key)
  if (index >= 0) restoreScrollTop(virtualLayout.value[index]! - anchor.offset)
}

defineExpose({
  captureAnchor,
  restoreAnchor,
  restoreScrollTop,
  getScrollTop: () => scrollTop.value,
  prefixHeight: (count: number) => virtualLayout.value[Math.min(count, rows.value.length)] ?? 0,
})

watch(
  () => [props.canLoadMore, props.loadingMore, props.loading, hasRows.value, props.sheet, rows.value.length],
  () => {
    loadMoreRequestPending = false
    pendingGroups.clear()
    syncLoadMoreObserver()
  },
  { flush: 'post' },
)

watch(virtualWindow, syncLoadMoreObserver, { flush: 'post' })

onMounted(() => {
  viewportHeight.value = scrollHost.value?.clientHeight || viewportHeight.value
  scrollTop.value = scrollHost.value?.scrollTop ?? 0

  if (typeof ResizeObserver !== 'undefined') {
    if (scrollHost.value) {
      viewportResizeObserver = new ResizeObserver(() => {
        viewportHeight.value = scrollHost.value?.clientHeight || viewportHeight.value
      })
      viewportResizeObserver.observe(scrollHost.value)
    }

    rowResizeObserver = new ResizeObserver((entries) => {
      let changed = false
      for (const entry of entries) {
        const row = rowByElement.get(entry.target)
        if (!row) continue
        const height = Math.max(1, entry.borderBoxSize?.[0]?.blockSize ?? entry.contentRect.height)
        if (Math.abs((measuredRowHeights.get(row) ?? 0) - height) < 0.5) continue
        measuredRowHeights.set(row, height)
        changed = true
      }
      // Commit measurements outside ResizeObserver delivery. Updating the virtual
      // window during delivery would resize observed rows again in the same frame.
      if (changed && measurementFrame == null) measurementFrame = window.requestAnimationFrame(() => {
        measurementFrame = null
        measuredHeightVersion.value += 1
      })
    })
    for (const element of scrollHost.value?.querySelectorAll('tbody tr') ?? []) {
      if (rowByElement.has(element)) rowResizeObserver.observe(element)
    }
  }

  loadMoreRequestPending = false
  syncLoadMoreObserver()
  emit('scroll-top-change', scrollHost.value?.scrollTop ?? 0)
})

onBeforeUnmount(() => {
  if (scrollFrame != null) window.cancelAnimationFrame(scrollFrame)
  if (measurementFrame != null) window.cancelAnimationFrame(measurementFrame)
  measurementFrame = null
  scrollFrame = null
  emit('scroll-top-change', pendingScrollTop || scrollHost.value?.scrollTop || 0)
  loadMoreRequestPending = false
  disconnectLoadMoreObserver()
  viewportResizeObserver?.disconnect()
  viewportResizeObserver = null
  rowResizeObserver?.disconnect()
  rowResizeObserver = null
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
      <div class="mt-2 text-sm text-ngb-muted">Loading the first rows for the selected layout.</div>
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
      <table :class="tableClass" :style="tableStyle" data-testid="report-sheet-table">
        <colgroup>
          <col v-for="(column, index) in columns" :key="column.code" :style="{ width: `${columnWidths[index]}px` }" />
        </colgroup>
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
              v-for="column in columns"
              :key="column.code"
              class="border-b border-ngb-border px-4 py-3 text-left font-semibold leading-snug text-ngb-text whitespace-pre-wrap break-words"
            >
              {{ column.title }}
            </th>
          </tr>
        </thead>

        <tbody>
          <tr v-if="virtualWindow.top > 0" aria-hidden="true">
            <td :colspan="Math.max(1, columns.length)" class="border-0 p-0" :style="{ height: `${virtualWindow.top}px` }" />
          </tr>
          <tr
            v-for="entry in virtualWindow.entries"
            :ref="(element) => observeVirtualRow(element, entry.row)"
            :key="rowRenderKey(entry.row, entry.index)"
            :class="[rowClass(entry.row), bodyRowHoverClass()]"
          >
            <td v-if="entry.row.control" :colspan="Math.max(1, columns.length)"
              class="border-b border-ngb-border/70 px-4 py-3 text-sm"
              :data-group-next="entry.row.control.autoLoad ? entry.row.control.id : undefined"
              :style="{ paddingLeft: `${16 + (entry.row.outlineLevel ?? 0) * 16}px` }"
              :role="entry.row.control.error ? 'alert' : 'status'">
              <span v-if="!entry.row.control.action || entry.row.control.error">{{ entry.row.control.text }}</span>
              <button v-if="entry.row.control.action" type="button" class="ngb-btn ml-3 px-3 py-1"
                @click="emit('group-action', entry.row.control.id, entry.row.control.action)">
                {{ entry.row.control.error ? 'Retry' : entry.row.control.text }}
              </button>
            </td>
            <template v-else>
            <td
              v-for="(cell, cellIndex) in entry.row.cells"
              :key="`${rowRenderKey(entry.row, entry.index)}:${cellIndex}`"
              :class="bodyCellClass(entry.row, cellIndex)"
              :colspan="cell.colSpan ?? 1"
              :rowspan="cell.rowSpan ?? 1"
            >
              <div
                class="flex min-w-0 items-start gap-2"
                :style="cellIndex === 0 ? { paddingLeft: `${(entry.row.outlineLevel ?? 0) * 16}px` } : undefined"
              >
                <NgbBadge v-if="cellIndex === 0 && rowKindLabel(entry.row)" tone="neutral">{{ rowKindLabel(entry.row) }}</NgbBadge>
                <button v-if="cellIndex === 0 && entry.row.group" type="button" class="ngb-btn px-2 py-0"
                  :aria-label="`${entry.row.group.expanded ? 'Collapse' : 'Expand'} group ${cellText(cell)}`"
                  :aria-expanded="entry.row.group.expanded"
                  @click="emit('group-action', entry.row.group.id, 'toggle')">{{ entry.row.group.expanded ? '⌄' : '›' }}</button>
                <button
                  v-if="drilldownRoute(cell)"
                  type="button"
                  class="min-w-0 cursor-pointer whitespace-pre-wrap break-words text-left hover:underline"
                  :class="isSubtotalOrTotal(entry.row) ? 'font-semibold' : undefined"
                  @click="onCellActivate(cell)"
                >
                  {{ cellText(cell) }}
                </button>
                <span v-else class="min-w-0 whitespace-pre-wrap break-words" :class="isSubtotalOrTotal(entry.row) ? 'font-semibold' : undefined">{{ cellText(cell) }}</span>
              </div>
            </td>
            </template>
          </tr>
          <tr v-if="virtualWindow.bottom > 0" aria-hidden="true">
            <td :colspan="Math.max(1, columns.length)" class="border-0 p-0" :style="{ height: `${virtualWindow.bottom}px` }" />
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
