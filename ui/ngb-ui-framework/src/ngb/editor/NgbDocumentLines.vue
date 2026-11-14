<script setup lang="ts" generic="TRow, TColumn extends DocumentLinesColumn">
import { computed, nextTick, ref, useId, watch } from 'vue'

import { useValidationFocus } from '../metadata/useValidationFocus'
import NgbIcon from '../primitives/NgbIcon.vue'
import type { DocumentLinesColumn, DocumentLinesErrors, DocumentLinesHandle } from './documentLines'

const props = withDefaults(defineProps<{
  title: string
  description?: string
  rows: readonly TRow[]
  columns: readonly TColumn[]
  rowKey?: (row: TRow, index: number) => string | number
  readonly?: boolean
  allowAddRemoveRows?: boolean
  allowReorder?: boolean
  readonlyActions?: 'hide' | 'disable'
  pageSize?: number
  addLabel?: string
  emptyLabel?: string
  errors?: DocumentLinesErrors | null
  summaryErrors?: readonly string[]
}>(), {
  description: '',
  readonly: false,
  allowAddRemoveRows: true,
  allowReorder: true,
  readonlyActions: 'hide',
  pageSize: 100,
  addLabel: 'Add row',
  emptyLabel: 'No rows yet.',
  errors: null,
  summaryErrors: () => [],
})

const emit = defineEmits<{
  add: []
  remove: [rowIndex: number]
  reorder: [fromIndex: number, toIndex: number]
}>()

const rootRef = ref<HTMLElement | null>(null)
const titleId = useId()
const requestedPage = ref(0)
const pendingAdd = ref(false)
const dragFrom = ref<number | null>(null)
const validationFocus = useValidationFocus(rootRef, { attribute: 'data-validation-cell' })
const size = computed(() => Number.isFinite(props.pageSize)
  ? Math.max(1, Math.floor(props.pageSize))
  : 100)
const pageCount = computed(() => Math.max(1, Math.ceil(props.rows.length / size.value)))
const page = computed(() => Math.min(requestedPage.value, pageCount.value - 1))
const start = computed(() => page.value * size.value)
const visibleRows = computed(() => props.rows.slice(start.value, start.value + size.value)
  .map((row, index) => ({ row, rowIndex: start.value + index })))
const canManage = computed(() => !props.readonly && props.allowAddRemoveRows)
const canReorder = computed(() => !props.readonly && props.allowReorder && props.rows.length > 1)
const showActions = computed(() => props.allowAddRemoveRows && (!props.readonly || props.readonlyActions === 'disable'))
const summary = computed(() => props.summaryErrors.filter((message) => message.trim()))

watch(() => props.rows.length, (count, previous) => {
  if (pendingAdd.value && count > previous) {
    requestedPage.value = Math.floor((count - 1) / size.value)
    void nextTick(() => focusCell(count - 1, props.columns[0]?.key ?? ''))
  } else {
    requestedPage.value = page.value
  }
  pendingAdd.value = false
  dragFrom.value = null
})

function fieldError(rowIndex: number, key: string): string | undefined {
  const value = props.errors?.[rowIndex]?.[key]
  return typeof value === 'string' ? value.trim() || undefined : value?.find((message) => message.trim())
}

function rowHasErrors(rowIndex: number): boolean {
  return Object.keys(props.errors?.[rowIndex] ?? {}).some((key) => !!fieldError(rowIndex, key))
}

function focusCell(rowIndex: number, fieldKey: string): boolean {
  if (rowIndex < 0 || rowIndex >= props.rows.length || !Number.isInteger(rowIndex)) return false
  const targetPage = Math.floor(rowIndex / size.value)
  if (targetPage !== page.value) {
    requestedPage.value = targetPage
    void nextTick(() => validationFocus.focus(`${rowIndex}:${fieldKey}`))
    return true
  }
  return validationFocus.focus(`${rowIndex}:${fieldKey}`)
}

function focusFirstError(): boolean {
  for (const rowIndex of Object.keys(props.errors ?? {}).map(Number).sort((a, b) => a - b)) {
    for (const key of [...props.columns.map((column) => column.key), 'ordinal']) {
      if (fieldError(rowIndex, key) && focusCell(rowIndex, key)) return true
    }
  }
  if (summary.value.length) {
    rootRef.value?.scrollIntoView({ block: 'center', behavior: 'smooth' })
    return true
  }
  return false
}

function addRow(): void {
  if (!canManage.value) return
  pendingAdd.value = true
  emit('add')
  void nextTick(() => {
    pendingAdd.value = false
  })
}

function removeRow(rowIndex: number): void {
  if (canManage.value) emit('remove', rowIndex)
}

function reorder(fromIndex: number, toIndex: number): void {
  if (!canReorder.value || fromIndex === toIndex || fromIndex < 0 || toIndex < 0
    || fromIndex >= props.rows.length || toIndex >= props.rows.length) return
  emit('reorder', fromIndex, toIndex)
}

function onDragStart(rowIndex: number, event: DragEvent): void {
  if (!canReorder.value) return
  dragFrom.value = rowIndex
  try {
    event.dataTransfer?.setData('text/plain', `${titleId}:${rowIndex}`)
  } catch {
    // Local drag state also supports browsers that restrict transfer writes.
  }
}

function onDragOver(event: DragEvent): void {
  if (canReorder.value && dragFrom.value !== null) event.preventDefault()
}

function onDrop(rowIndex: number, event: DragEvent): void {
  if (dragFrom.value === null) return
  event.preventDefault()
  const from = dragFrom.value
  dragFrom.value = null
  reorder(from, rowIndex)
}

function onReorderKey(rowIndex: number, event: KeyboardEvent): void {
  if (!event.altKey || !['ArrowUp', 'ArrowDown'].includes(event.key) || !canReorder.value) return
  event.preventDefault()
  const target = rowIndex + (event.key === 'ArrowUp' ? -1 : 1)
  reorder(rowIndex, target)
  if (target >= 0 && target < props.rows.length) {
    void nextTick(() => focusCell(target, '__reorder'))
  }
}

defineExpose<DocumentLinesHandle>({ focusCell, focusFirstError })
</script>

<template>
  <section
    ref="rootRef"
    class="overflow-hidden rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card"
    :aria-labelledby="titleId"
  >
    <div class="border-b border-ngb-border px-4 py-3">
      <div :id="titleId" class="text-sm font-semibold text-ngb-text">{{ title }}</div>
      <div v-if="description" class="mt-0.5 text-xs text-ngb-muted">{{ description }}</div>
      <ul v-if="summary.length" class="mt-2 list-disc space-y-1 pl-5 text-xs text-ngb-danger">
        <li v-for="(message, index) in summary" :key="index">{{ message }}</li>
      </ul>
      <slot name="header" />
    </div>

    <div v-if="rows.length" class="overflow-x-auto">
      <table class="w-full table-fixed text-sm" :aria-labelledby="titleId">
        <colgroup>
          <col style="width:28px" />
          <col style="width:44px" />
          <col v-for="column in columns" :key="column.key" :style="{ width: column.width }" />
          <col v-if="showActions" style="width:40px" />
        </colgroup>
        <thead class="bg-ngb-bg text-xs text-ngb-muted">
          <tr>
            <th class="px-2 py-2"><span class="sr-only">Order</span></th>
            <th scope="col" class="border-r border-dotted border-ngb-border px-2 py-2 text-right font-semibold">#</th>
            <th
              v-for="column in columns"
              :key="column.key"
              scope="col"
              class="truncate border-r border-dotted border-ngb-border px-3 py-2 font-semibold"
              :style="{ textAlign: column.align }"
            >
              {{ column.label }}
            </th>
            <th v-if="showActions" class="px-2 py-2"><span class="sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="{ row, rowIndex } in visibleRows"
            :key="rowKey?.(row, rowIndex) ?? rowIndex"
            class="border-t border-ngb-border align-top transition-colors hover:bg-ngb-bg"
            :class="rowHasErrors(rowIndex) ? 'bg-red-50/40 dark:bg-red-950/10' : ''"
            :draggable="canReorder"
            @dragstart="onDragStart(rowIndex, $event)"
            @dragover="onDragOver"
            @drop="onDrop(rowIndex, $event)"
            @dragend="dragFrom = null"
          >
            <td class="px-1 py-1 align-middle" :data-validation-cell="`${rowIndex}:__reorder`">
              <button
                type="button"
                class="flex h-8 w-6 items-center justify-center text-ngb-muted ngb-focus"
                :class="canReorder ? 'cursor-grab active:cursor-grabbing' : 'opacity-50'"
                :disabled="!canReorder"
                :aria-label="`Reorder row ${rowIndex + 1}`"
                title="Drag to reorder; Alt+Up or Alt+Down to move"
                @keydown="onReorderKey(rowIndex, $event)"
              >
                <NgbIcon name="grip-vertical" :size="16" />
              </button>
            </td>
            <td class="border-r border-dotted border-ngb-border px-2 py-1 text-right text-ngb-muted" :data-validation-cell="`${rowIndex}:ordinal`">
              <div class="flex min-h-8 items-center justify-end">{{ rowIndex + 1 }}</div>
              <div v-if="fieldError(rowIndex, 'ordinal')" class="mt-1 text-left text-xs text-ngb-danger">
                {{ fieldError(rowIndex, 'ordinal') }}
              </div>
            </td>
            <td
              v-for="column in columns"
              :key="column.key"
              class="border-r border-dotted border-ngb-border px-0 py-1 align-top"
              :data-validation-cell="`${rowIndex}:${column.key}`"
            >
              <slot name="cell" :row="row" :row-index="rowIndex" :column="column" :readonly="readonly" />
              <div v-if="fieldError(rowIndex, column.key)" class="px-2 pb-1 text-xs text-ngb-danger">
                {{ fieldError(rowIndex, column.key) }}
              </div>
            </td>
            <td v-if="showActions" class="px-1 py-1 align-middle">
              <button
                type="button"
                class="flex h-8 w-8 items-center justify-center rounded-[var(--ngb-radius)] text-ngb-muted hover:bg-ngb-bg hover:text-ngb-text ngb-focus"
                title="Delete"
                :aria-label="`Delete row ${rowIndex + 1}`"
                :disabled="!canManage"
                @click="removeRow(rowIndex)"
              >
                <NgbIcon name="trash" :size="16" />
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-else class="px-4 py-6 text-sm text-ngb-muted">{{ emptyLabel }}</div>

    <div
      v-if="pageCount > 1"
      class="flex items-center justify-between border-t border-ngb-border px-4 py-2 text-xs text-ngb-muted"
    >
      <span>Rows {{ start + 1 }}–{{ Math.min(rows.length, start + size) }} of {{ rows.length }}</span>
      <div class="flex items-center gap-2">
        <button
          type="button"
          class="rounded-[var(--ngb-radius)] border border-ngb-border px-3 py-1 ngb-focus"
          :disabled="page === 0"
          @click="requestedPage = page - 1"
        >
          Previous
        </button>
        <span>Page {{ page + 1 }} of {{ pageCount }}</span>
        <button
          type="button"
          class="rounded-[var(--ngb-radius)] border border-ngb-border px-3 py-1 ngb-focus"
          :disabled="page + 1 >= pageCount"
          @click="requestedPage = page + 1"
        >
          Next
        </button>
      </div>
    </div>
    <slot name="footer" />
    <div v-if="canManage" class="flex justify-end border-t border-ngb-border px-3 py-2">
      <button
        type="button"
        class="inline-flex h-9 items-center gap-2 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 text-sm text-ngb-text hover:bg-ngb-bg ngb-focus"
        @click="addRow"
      >
        <NgbIcon name="plus" :size="16" />
        <span>{{ addLabel }}</span>
      </button>
    </div>
  </section>
</template>
