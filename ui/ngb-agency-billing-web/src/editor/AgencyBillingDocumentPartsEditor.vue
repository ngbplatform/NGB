<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  clonePlainData,
  dataTypeKind,
  isReferenceValue,
  NgbDatePicker,
  NgbIcon,
  NgbInput,
  NgbLookup,
  NgbSelect,
  resolveLookupHint,
  tryExtractReferenceId,
  type EntityFormModel,
  type FieldMetadata,
  type FieldOption,
  type LookupHint,
  type LookupItem,
  type MetadataFormBehavior,
  type PartMetadata,
  type RecordPartRow,
  type RecordParts,
} from 'ngb-ui-framework'

import {
  calculateAgencyBillingDocumentPartAmount,
  ensureAgencyBillingDocumentPartRowKey,
  listAgencyBillingDocumentPartFields,
  normalizeAgencyBillingDocumentPartRows,
  recomputeAgencyBillingDocumentPartRow,
  resolveAgencyBillingDocumentAmountSourceField,
  syncAgencyBillingDocumentComputedFields,
  type AgencyBillingDocumentPartErrors,
} from './documentParts'

type GridFieldRenderMode =
  | 'select'
  | 'lookup'
  | 'checkbox'
  | 'textarea'
  | 'date'
  | 'input'

type GridFieldState = {
  mode: GridFieldRenderMode
  inputType: 'text' | 'number' | 'datetime-local'
  fieldOptions: FieldOption[] | null
  hint: LookupHint | null
}

const props = withDefaults(defineProps<{
  entityTypeCode: string
  parts: PartMetadata[]
  modelValue?: RecordParts | null
  documentModel?: EntityFormModel | null
  readonly?: boolean
  behavior?: MetadataFormBehavior
  errors?: AgencyBillingDocumentPartErrors | null
}>(), {
  modelValue: null,
  documentModel: null,
  readonly: false,
  behavior: undefined,
  errors: null,
})

const emit = defineEmits<{
  (e: 'update:modelValue', value: RecordParts | null): void
}>()

const route = useRoute()
const router = useRouter()
const lookupItemsByCell = ref<Record<string, LookupItem[]>>({})
const dragState = ref<{ partCode: string; rowIndex: number } | null>(null)

const partFields = computed(() =>
  new Map(props.parts.map((part) => [part.partCode, listAgencyBillingDocumentPartFields(part)] as const)),
)

const partColumns = computed(() =>
  new Map(
    props.parts.map((part) => [
      part.partCode,
      new Map((part.list?.columns ?? []).map((column) => [column.key, column] as const)),
    ] as const),
  ),
)

watch(
  () => props.modelValue,
  () => {
    syncAgencyBillingDocumentComputedFields({
      documentType: props.entityTypeCode,
      partsMeta: props.parts,
      partsModel: props.modelValue,
      model: props.documentModel,
    })
  },
  { deep: true, immediate: true },
)

function partRows(partCode: string): RecordPartRow[] {
  return normalizeAgencyBillingDocumentPartRows(props.modelValue?.[partCode]?.rows)
}

function cloneParts(): RecordParts {
  return clonePlainData(props.modelValue ?? {}) as RecordParts
}

function cloneNormalizedParts(): RecordParts {
  const next = cloneParts()

  for (const part of props.parts) {
    next[part.partCode] = {
      rows: partRows(part.partCode).map((row) => ({ ...row })),
    }
  }

  return next
}

function emitParts(parts: RecordParts): void {
  const next: RecordParts = {}
  for (const part of props.parts) {
    next[part.partCode] = {
      rows: normalizeAgencyBillingDocumentPartRows(parts[part.partCode]?.rows)
        .map((row) => recomputeAgencyBillingDocumentPartRow(props.entityTypeCode, row)),
    }
  }
  emit('update:modelValue', next)
}

function emitRows(partCode: string, rows: RecordPartRow[]): void {
  const next = cloneNormalizedParts()
  next[partCode] = {
    rows: normalizeAgencyBillingDocumentPartRows(rows)
      .map((row) => recomputeAgencyBillingDocumentPartRow(props.entityTypeCode, row)),
  }
  emitParts(next)
}

function createEmptyRow(partCode: string): RecordPartRow {
  const row: RecordPartRow = {}

  for (const field of partFields.value.get(partCode) ?? []) {
    row[field.key] = dataTypeKind(field.dataType) === 'Boolean' ? false : null
  }

  row.__row_key = ensureAgencyBillingDocumentPartRowKey(row)
  row.ordinal = partRows(partCode).length + 1
  return recomputeAgencyBillingDocumentPartRow(props.entityTypeCode, row)
}

function canManageRows(partCode: string): boolean {
  const part = props.parts.find((entry) => entry.partCode === partCode)
  return !props.readonly && part?.allowAddRemoveRows !== false
}

function addRow(partCode: string): void {
  if (!canManageRows(partCode)) return
  emitRows(partCode, [...partRows(partCode), createEmptyRow(partCode)])
}

function removeRow(partCode: string, rowIndex: number): void {
  if (!canManageRows(partCode)) return
  emitRows(partCode, partRows(partCode).filter((_, index) => index !== rowIndex))
}

function updateCell(partCode: string, rowIndex: number, fieldKey: string, value: unknown): void {
  const rows = partRows(partCode)
  const row = rows[rowIndex]
  if (!row) return

  rows[rowIndex] = recomputeAgencyBillingDocumentPartRow(props.entityTypeCode, {
    ...row,
    [fieldKey]: value,
  })

  emitRows(partCode, rows)
}

function lookupCellKey(partCode: string, rowIndex: number, fieldKey: string): string {
  return `${partCode}:${rowIndex}:${fieldKey}`
}

function fieldError(partCode: string, rowIndex: number, fieldKey: string): string | null {
  return props.errors?.[partCode]?.[rowIndex]?.[fieldKey] ?? null
}

function rowHasErrors(partCode: string, rowIndex: number): boolean {
  const rowErrors = props.errors?.[partCode]?.[rowIndex]
  if (!rowErrors) return false
  return Object.values(rowErrors).some((message) => String(message ?? '').trim().length > 0)
}

function resolveFieldState(field: FieldMetadata, row: RecordPartRow): GridFieldState {
  const fieldOptions = props.behavior?.resolveFieldOptions?.({
    entityTypeCode: props.entityTypeCode,
    model: row,
    field,
  }) ?? null
  const hint = resolveLookupHint({
    entityTypeCode: props.entityTypeCode,
    model: row,
    field,
    behavior: props.behavior,
  })
  const dataType = dataTypeKind(field.dataType)

  const isLookup = !!hint || dataType === 'Lookup'
  const isCheckbox = field.uiControl === 5 || dataType === 'Boolean'
  const isTextArea = field.uiControl === 2
  const isDate = field.uiControl === 6 || dataType === 'Date'
  const isDateTime = field.uiControl === 7 || dataType === 'DateTime'
  const isNumber = field.uiControl === 3 || dataType === 'Int32' || dataType === 'Decimal'
  const isMoney = field.uiControl === 4 || dataType === 'Money'

  if (fieldOptions) return { mode: 'select', inputType: 'text', fieldOptions, hint }
  if (isLookup && hint) return { mode: 'lookup', inputType: 'text', fieldOptions, hint }
  if (isCheckbox) return { mode: 'checkbox', inputType: 'text', fieldOptions, hint }
  if (isTextArea) return { mode: 'textarea', inputType: 'text', fieldOptions, hint }
  if (isDate) return { mode: 'date', inputType: 'text', fieldOptions, hint }

  return {
    mode: 'input',
    inputType: isDateTime ? 'datetime-local' : (isNumber || isMoney ? 'number' : 'text'),
    fieldOptions,
    hint,
  }
}

function lookupValue(row: RecordPartRow, fieldKey: string): LookupItem | null {
  const value = row[fieldKey]
  if (!value) return null
  if (isReferenceValue(value)) return { id: value.id, label: value.display }

  const id = tryExtractReferenceId(value)
  return id ? { id, label: id } : null
}

async function onLookupQuery(partCode: string, rowIndex: number, field: FieldMetadata, row: RecordPartRow, query: string): Promise<void> {
  const key = lookupCellKey(partCode, rowIndex, field.key)
  const search = props.behavior?.searchLookup
  const hint = resolveFieldState(field, row).hint
  const normalizedQuery = String(query ?? '').trim()

  if (!search || !hint || !normalizedQuery) {
    lookupItemsByCell.value = { ...lookupItemsByCell.value, [key]: [] }
    return
  }

  const items = await Promise.resolve(search({ hint, query: normalizedQuery }))
  lookupItemsByCell.value = { ...lookupItemsByCell.value, [key]: items }
}

function onLookupSelect(partCode: string, rowIndex: number, fieldKey: string, item: LookupItem | null): void {
  updateCell(
    partCode,
    rowIndex,
    fieldKey,
    item
      ? { id: item.id, display: item.label }
      : null,
  )
}

async function openLookup(field: FieldMetadata, row: RecordPartRow): Promise<void> {
  const targetBuilder = props.behavior?.buildLookupTargetUrl
  const hint = resolveFieldState(field, row).hint
  if (!targetBuilder || !hint) return

  const target = await targetBuilder({
    hint,
    value: row[field.key],
    routeFullPath: route.fullPath,
  })

  if (!target) return
  await router.push(target)
}

function canReorder(partCode: string): boolean {
  return canManageRows(partCode) && partRows(partCode).length > 1
}

function onDragStart(partCode: string, rowIndex: number, event: DragEvent): void {
  if (!canReorder(partCode)) return

  dragState.value = { partCode, rowIndex }
  try {
    event.dataTransfer?.setData('text/plain', `${partCode}:${rowIndex}`)
    event.dataTransfer?.setDragImage(new Image(), 0, 0)
  } catch {
    // Ignore browser drag API differences.
  }
}

function onDragOver(partCode: string, event: DragEvent): void {
  if (!canReorder(partCode)) return
  event.preventDefault()
}

function onDrop(partCode: string, rowIndex: number, event: DragEvent): void {
  if (!canReorder(partCode)) return

  event.preventDefault()
  const fallback = event.dataTransfer?.getData('text/plain') ?? ''
  const [dragPartCode, dragRowIndexRaw] = fallback.split(':')

  const sourcePartCode = dragState.value?.partCode ?? dragPartCode
  const sourceRowIndex = dragState.value?.rowIndex ?? Number.parseInt(dragRowIndexRaw, 10)
  dragState.value = null

  if (sourcePartCode !== partCode || !Number.isFinite(sourceRowIndex) || sourceRowIndex === rowIndex) return

  const next = partRows(partCode).map((row) => ({ ...row }))
  const [moved] = next.splice(sourceRowIndex, 1)
  if (!moved) return

  next.splice(rowIndex, 0, moved)
  emitRows(partCode, next)
}

function fieldColStyle(partCode: string, field: FieldMetadata): string | undefined {
  const widthPx = partColumns.value.get(partCode)?.get(field.key)?.widthPx
  if (typeof widthPx === 'number' && widthPx > 0) return `width:${widthPx}px`

  const dataType = dataTypeKind(field.dataType)
  if (field.lookup || dataType === 'Lookup') return 'width:260px'
  if (dataType === 'Boolean') return 'width:96px'
  if (dataType === 'Date' || dataType === 'DateTime') return 'width:170px'
  if (dataType === 'Int32' || dataType === 'Decimal' || dataType === 'Money') return 'width:160px'
  return undefined
}

function partAmount(part: PartMetadata): number | null {
  return calculateAgencyBillingDocumentPartAmount(part, props.modelValue?.[part.partCode]?.rows)
}

function formatAmount(value: number | null): string {
  const numeric = value ?? 0
  return numeric.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4,
  })
}
</script>

<template>
  <section
    v-for="part in parts"
    :key="part.partCode"
    class="overflow-hidden rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card"
  >
    <div class="border-b border-ngb-border px-4 py-3">
      <div class="text-sm font-semibold text-ngb-text">{{ part.title }}</div>
      <div class="mt-0.5 text-xs text-ngb-muted">Tabular section</div>
    </div>

    <table v-if="partRows(part.partCode).length > 0" class="w-full table-fixed text-sm">
      <colgroup>
        <col style="width:28px" />
        <col style="width:44px" />
        <col
          v-for="field in partFields.get(part.partCode) ?? []"
          :key="`${part.partCode}:col:${field.key}`"
          :style="fieldColStyle(part.partCode, field)"
        />
        <col v-if="!readonly && part.allowAddRemoveRows !== false" style="width:40px" />
      </colgroup>

      <thead class="bg-ngb-bg text-xs text-ngb-muted">
        <tr>
          <th class="px-2 py-2"></th>
          <th class="border-r border-dotted border-ngb-border px-2 py-2 text-right font-semibold">#</th>
          <th
            v-for="field in partFields.get(part.partCode) ?? []"
            :key="`${part.partCode}:head:${field.key}`"
            class="border-r border-dotted border-ngb-border px-3 py-2 font-semibold truncate"
          >
            {{ field.label }}
          </th>
          <th v-if="!readonly && part.allowAddRemoveRows !== false" class="px-2 py-2"></th>
        </tr>
      </thead>

      <tbody>
        <tr
          v-for="(row, rowIndex) in partRows(part.partCode)"
          :key="`${part.partCode}:${String(row.__row_key ?? rowIndex)}`"
          class="border-t border-ngb-border align-top transition-colors hover:bg-ngb-bg"
          :class="rowHasErrors(part.partCode, rowIndex) ? 'bg-red-50/40 dark:bg-red-950/10' : ''"
          :draggable="canReorder(part.partCode)"
          @dragstart="onDragStart(part.partCode, rowIndex, $event)"
          @dragover="onDragOver(part.partCode, $event)"
          @drop="onDrop(part.partCode, rowIndex, $event)"
        >
          <td class="px-1 py-1 align-middle">
            <div
              class="flex h-8 w-6 items-center justify-center text-ngb-muted"
              :class="canReorder(part.partCode) ? 'cursor-grab active:cursor-grabbing' : 'opacity-50'"
              :title="canReorder(part.partCode) ? 'Drag to reorder' : ''"
            >
              <NgbIcon name="grip-vertical" :size="16" />
            </div>
          </td>

          <td class="border-r border-dotted border-ngb-border px-2 py-1 align-top text-right text-ngb-muted">
            <div class="flex min-h-8 items-center justify-end">{{ rowIndex + 1 }}</div>
          </td>

          <td
            v-for="field in partFields.get(part.partCode) ?? []"
            :key="`${part.partCode}:${rowIndex}:${field.key}`"
            class="border-r border-dotted border-ngb-border px-0 py-1 align-top"
          >
            <template v-if="resolveFieldState(field, row).mode === 'lookup'">
              <NgbLookup
                :model-value="lookupValue(row, field.key)"
                :items="lookupItemsByCell[lookupCellKey(part.partCode, rowIndex, field.key)] ?? []"
                :readonly="readonly"
                :show-open="!!lookupValue(row, field.key)"
                :show-clear="!readonly && !!lookupValue(row, field.key)"
                variant="grid"
                :placeholder="`Select ${field.label.toLowerCase()}...`"
                @query="onLookupQuery(part.partCode, rowIndex, field, row, $event)"
                @update:model-value="onLookupSelect(part.partCode, rowIndex, field.key, $event)"
                @open="void openLookup(field, row)"
              />
            </template>

            <template v-else-if="resolveFieldState(field, row).mode === 'select'">
              <NgbSelect
                :model-value="row[field.key] ?? null"
                :options="resolveFieldState(field, row).fieldOptions ?? []"
                :disabled="readonly"
                variant="grid"
                @update:model-value="updateCell(part.partCode, rowIndex, field.key, $event)"
              />
            </template>

            <template v-else-if="resolveFieldState(field, row).mode === 'checkbox'">
              <div class="flex min-h-8 items-center justify-center px-2">
                <input
                  type="checkbox"
                  class="h-4 w-4 rounded-[3px] border border-ngb-border bg-ngb-card"
                  :checked="!!row[field.key]"
                  :disabled="readonly"
                  @change="updateCell(part.partCode, rowIndex, field.key, ($event.target as HTMLInputElement).checked)"
                />
              </div>
            </template>

            <template v-else-if="resolveFieldState(field, row).mode === 'date'">
              <div class="px-1 py-0.5">
                <NgbDatePicker
                  :model-value="(row[field.key] as string | null | undefined) ?? null"
                  :disabled="readonly"
                  :readonly="readonly"
                  :grouped="true"
                  @update:model-value="updateCell(part.partCode, rowIndex, field.key, $event)"
                />
              </div>
            </template>

            <template v-else-if="resolveFieldState(field, row).mode === 'textarea'">
              <textarea
                class="min-h-[64px] w-full rounded-none border border-transparent bg-transparent px-2 py-2 text-sm text-ngb-text placeholder:text-ngb-muted/70 ngb-focus"
                :value="String(row[field.key] ?? '')"
                :readonly="readonly"
                @input="updateCell(part.partCode, rowIndex, field.key, ($event.target as HTMLTextAreaElement).value)"
              />
            </template>

            <template v-else>
              <NgbInput
                :type="resolveFieldState(field, row).inputType"
                :model-value="(row[field.key] as string | number | undefined) ?? ''"
                :readonly="readonly"
                :disabled="readonly"
                variant="grid"
                @update:model-value="updateCell(part.partCode, rowIndex, field.key, $event)"
              />
            </template>

            <div v-if="fieldError(part.partCode, rowIndex, field.key)" class="px-2 pb-1 text-xs text-ngb-danger">
              {{ fieldError(part.partCode, rowIndex, field.key) }}
            </div>
          </td>

          <td v-if="!readonly && part.allowAddRemoveRows !== false" class="px-1 py-1 align-middle">
            <button
              type="button"
              class="flex h-8 w-8 items-center justify-center rounded-[var(--ngb-radius)] text-ngb-muted hover:bg-ngb-bg hover:text-ngb-text ngb-focus"
              title="Delete"
              @click="removeRow(part.partCode, rowIndex)"
            >
              <NgbIcon name="trash" :size="16" />
            </button>
          </td>
        </tr>
      </tbody>
    </table>

    <div v-else class="px-4 py-6 text-sm text-ngb-muted">
      No rows yet.
    </div>

    <div
      v-if="resolveAgencyBillingDocumentAmountSourceField(part)"
      class="flex items-center justify-end gap-4 border-t border-ngb-border px-4 py-3"
    >
      <span class="text-xs font-semibold uppercase tracking-[0.12em] text-ngb-muted">Amount</span>
      <span class="text-base font-semibold text-ngb-text">{{ formatAmount(partAmount(part)) }}</span>
    </div>

    <div v-if="!readonly && part.allowAddRemoveRows !== false" class="flex justify-end border-t border-ngb-border px-3 py-2">
      <button
        type="button"
        class="inline-flex items-center gap-2 h-9 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 text-sm text-ngb-text hover:bg-ngb-bg ngb-focus"
        @click="addRow(part.partCode)"
      >
        <NgbIcon name="plus" :size="16" />
        <span>Add row</span>
      </button>
    </div>
  </section>
</template>
