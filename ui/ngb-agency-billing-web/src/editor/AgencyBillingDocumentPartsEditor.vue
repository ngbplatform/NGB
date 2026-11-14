<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NgbDocumentLines } from '@ngbplatform/ui/editor'
import {
  dataTypeKind,
  isReferenceValue,
  normalizeJsonValue,
  NgbDatePicker,
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
} from '@ngbplatform/ui'

import {
  calculateAgencyBillingDocumentComputedFields,
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
const lookupControllers = new Map<string, AbortController>()

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

const normalizedRowsByPart = computed(() =>
  new Map(
    props.parts.map((part) => [
      part.partCode,
      normalizeAgencyBillingDocumentPartRows(props.modelValue?.[part.partCode]?.rows),
    ] as const),
  ),
)

const computedDocumentFields = computed(() => calculateAgencyBillingDocumentComputedFields(
  props.entityTypeCode,
  props.parts,
  props.modelValue,
))

watch(
  [computedDocumentFields, () => props.documentModel],
  ([calculated]) => {
    syncAgencyBillingDocumentComputedFields({
      documentType: props.entityTypeCode,
      partsMeta: props.parts,
      partsModel: props.modelValue,
      model: props.documentModel,
    }, calculated)
  },
  { immediate: true },
)

function partRows(partCode: string): RecordPartRow[] {
  return normalizedRowsByPart.value.get(partCode) ?? []
}

function emitRows(partCode: string, rows: RecordPartRow[]): void {
  const next: RecordParts = { ...props.modelValue }
  next[partCode] = {
    rows: normalizeAgencyBillingDocumentPartRows(rows)
      .map((row) => recomputeAgencyBillingDocumentPartRow(props.entityTypeCode, row)),
  }
  emit('update:modelValue', next)
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
  const rows = partRows(partCode)
  emitRows(partCode, [...rows, createEmptyRow(partCode)])
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
    [fieldKey]: normalizeJsonValue(value),
  })

  emitRows(partCode, rows)
}

function lookupCellKey(partCode: string, rowIndex: number, fieldKey: string): string {
  return `${partCode}:${rowIndex}:${fieldKey}`
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
  const isDate = field.uiControl === 6 || dataType === 'Date'
  const isDateTime = field.uiControl === 7 || dataType === 'DateTime'
  const isNumber = field.uiControl === 3 || dataType === 'Int32' || dataType === 'Decimal'
  const isMoney = field.uiControl === 4 || dataType === 'Money'

  if (fieldOptions) return { mode: 'select', inputType: 'text', fieldOptions, hint }
  if (isLookup && hint) return { mode: 'lookup', inputType: 'text', fieldOptions, hint }
  if (isCheckbox) return { mode: 'checkbox', inputType: 'text', fieldOptions, hint }
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
  lookupControllers.get(key)?.abort()
  const search = props.behavior?.searchLookup
  const hint = resolveFieldState(field, row).hint
  const normalizedQuery = String(query ?? '').trim()

  if (!search || !hint || !normalizedQuery) {
    lookupItemsByCell.value = { ...lookupItemsByCell.value, [key]: [] }
    return
  }

  const controller = new AbortController()
  lookupControllers.set(key, controller)
  try {
    const items = await Promise.resolve(search({ hint, query: normalizedQuery, signal: controller.signal }))
    if (lookupControllers.get(key) === controller)
      lookupItemsByCell.value = { ...lookupItemsByCell.value, [key]: items }
  } catch {
    if (lookupControllers.get(key) !== controller) return
    lookupItemsByCell.value = { ...lookupItemsByCell.value, [key]: [] }
  } finally {
    if (lookupControllers.get(key) === controller) lookupControllers.delete(key)
  }
}

onBeforeUnmount(() => {
  lookupControllers.forEach((controller) => controller.abort())
  lookupControllers.clear()
})

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

function fieldWidth(partCode: string, field: FieldMetadata): string | undefined {
  const widthPx = partColumns.value.get(partCode)?.get(field.key)?.widthPx
  if (typeof widthPx === 'number' && widthPx > 0) return `${widthPx}px`

  const dataType = dataTypeKind(field.dataType)
  if (field.lookup || dataType === 'Lookup') return '260px'
  if (dataType === 'Boolean') return '96px'
  if (dataType === 'Date' || dataType === 'DateTime') return '170px'
  if (dataType === 'Int32' || dataType === 'Decimal' || dataType === 'Money') return '160px'
  return undefined
}

function lineColumns(partCode: string) {
  return (partFields.value.get(partCode) ?? []).map((field) => ({
    ...field,
    width: fieldWidth(partCode, field),
  }))
}

function reorderRows(partCode: string, fromIndex: number, toIndex: number): void {
  if (!canManageRows(partCode)) return
  const next = partRows(partCode).map((row) => ({ ...row }))
  const [moved] = next.splice(fromIndex, 1)
  if (!moved) return
  next.splice(toIndex, 0, moved)
  emitRows(partCode, next)
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
  <NgbDocumentLines
    v-for="part in parts"
    :key="part.partCode"
    :title="part.title"
    :rows="partRows(part.partCode)"
    :columns="lineColumns(part.partCode)"
    :row-key="(row) => String(row.__row_key)"
    :readonly="readonly"
    :allow-add-remove-rows="part.allowAddRemoveRows !== false"
    :allow-reorder="part.allowAddRemoveRows !== false"
    :errors="errors?.[part.partCode]"
    @add="addRow(part.partCode)"
    @remove="removeRow(part.partCode, $event)"
    @reorder="(from, to) => reorderRows(part.partCode, from, to)"
  >
    <template #cell="{ row, rowIndex, column: field }">
      <template v-if="resolveFieldState(field, row).mode === 'lookup'">
        <NgbLookup
          :model-value="lookupValue(row, field.key)"
          :items="lookupItemsByCell[lookupCellKey(part.partCode, rowIndex, field.key)] ?? []"
          :readonly="readonly"
          :show-open="!!lookupValue(row, field.key)"
          :show-clear="!readonly && !!lookupValue(row, field.key)"
          variant="grid"
          :placeholder="`Select ${field.label.toLowerCase()}…`"
          @query="onLookupQuery(part.partCode, rowIndex, field, row, $event)"
          @update:model-value="onLookupSelect(part.partCode, rowIndex, field.key, $event)"
          @open="void openLookup(field, row)"
        />
      </template>

      <template v-else-if="resolveFieldState(field, row).mode === 'select'">
        <NgbSelect
          :model-value="row[field.key] ?? null"
          :options="resolveFieldState(field, row).fieldOptions!"
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
    </template>
    <template #footer>
      <div
        v-if="resolveAgencyBillingDocumentAmountSourceField(part)"
        class="flex items-center justify-end gap-4 border-t border-ngb-border px-4 py-3"
      >
        <span class="text-xs font-semibold uppercase tracking-[0.12em] text-ngb-muted">Amount</span>
        <span class="text-base font-semibold text-ngb-text">{{ formatAmount(partAmount(part)) }}</span>
      </div>
    </template>
  </NgbDocumentLines>
</template>
