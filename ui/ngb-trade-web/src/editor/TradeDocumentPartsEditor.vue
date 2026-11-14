<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NgbDocumentLines } from '@ngbplatform/ui/editor'
import {
  dataTypeKind,
  isReferenceValue,
  type EntityFormModel,
  NgbDatePicker,
  NgbInput,
  NgbLookup,
  NgbSelect,
  normalizeJsonValue,
  resolveLookupHint,
  tryExtractReferenceId,
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
  calculateTradeDocumentAmount,
  calculateTradeDocumentPartAmount,
  ensureTradeDocumentPartRowKey,
  listTradeDocumentPartFields,
  normalizeTradeDocumentPartRows,
  resolveTradeDocumentAmountSourceField,
  type TradeDocumentPartErrors,
} from './documentParts'
import {
  resolveTradeDocumentLineDefaults,
  type TradeDocumentLineDefaultsRequest,
  type TradeDocumentLineDefaultsRowResult,
} from './tradeDocumentLineDefaultsApi'

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

const TRADE_SALES_INVOICE = 'trd.sales_invoice'
const TRADE_PURCHASE_RECEIPT = 'trd.purchase_receipt'
const TRADE_CUSTOMER_RETURN = 'trd.customer_return'
const TRADE_VENDOR_RETURN = 'trd.vendor_return'
const TRADE_INVENTORY_ADJUSTMENT = 'trd.inventory_adjustment'
const TRADE_ITEM_PRICE_UPDATE = 'trd.item_price_update'

const DOCUMENT_TYPES_WITH_LINE_DEFAULTS = new Set<string>([
  TRADE_PURCHASE_RECEIPT,
  TRADE_SALES_INVOICE,
  TRADE_INVENTORY_ADJUSTMENT,
  TRADE_CUSTOMER_RETURN,
  TRADE_VENDOR_RETURN,
  TRADE_ITEM_PRICE_UPDATE,
])

const props = withDefaults(defineProps<{
  entityTypeCode: string
  parts: PartMetadata[]
  modelValue?: RecordParts | null
  documentModel?: EntityFormModel | null
  readonly?: boolean
  behavior?: MetadataFormBehavior
  errors?: TradeDocumentPartErrors | null
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
const autoManagedValuesByRow = ref<Record<string, Partial<Record<string, string>>>>({})
const pendingForcedRowKeys = ref<Set<string>>(new Set())
const defaultsAbortController = ref<AbortController | null>(null)
const defaultsRequestVersion = ref(0)

const partFields = computed(() =>
  new Map(props.parts.map((part) => [part.partCode, listTradeDocumentPartFields(part)] as const)),
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
      normalizeTradeDocumentPartRows(props.modelValue?.[part.partCode]?.rows),
    ] as const),
  ),
)

const rowStructureSignature = computed(() => props.parts
  .map((part) => `${part.partCode}:${partRows(part.partCode).map((row) => ensureTradeDocumentPartRowKey(row)).join(',')}`)
  .join('|'))

watch(rowStructureSignature, () => {
  const liveRowKeys = new Set(props.parts.flatMap((part) =>
    partRows(part.partCode).map((row) => ensureTradeDocumentPartRowKey(row))))
  autoManagedValuesByRow.value = Object.fromEntries(
    Object.entries(autoManagedValuesByRow.value).filter(([rowKey]) => liveRowKeys.has(rowKey)),
  )
  pendingForcedRowKeys.value = new Set(
    Array.from(pendingForcedRowKeys.value).filter((rowKey) => liveRowKeys.has(rowKey)),
  )
  lookupControllers.forEach((controller) => controller.abort())
  lookupControllers.clear()
  lookupItemsByCell.value = {}
})

const documentAmount = computed(() => calculateTradeDocumentAmount(props.parts, props.modelValue))

function partRows(partCode: string): RecordPartRow[] {
  return normalizedRowsByPart.value.get(partCode) ?? []
}

watch(
  documentAmount,
  (value) => {
    if (!props.documentModel || !Object.prototype.hasOwnProperty.call(props.documentModel, 'amount') || value == null) {
      return
    }

    if (parseDecimal(props.documentModel.amount) === value) return
    props.documentModel.amount = value
  },
  { immediate: true },
)

function emitRows(partCode: string, rows: RecordPartRow[]): void {
  const next: RecordParts = { ...props.modelValue }
  next[partCode] = {
    rows: normalizeTradeDocumentPartRows(rows),
  }
  emit('update:modelValue', next)
}

function createEmptyRow(partCode: string): RecordPartRow {
  const row: RecordPartRow = {}

  for (const field of partFields.value.get(partCode) ?? []) {
    row[field.key] = dataTypeKind(field.dataType) === 'Boolean' ? false : null
  }

  row.__row_key = ensureTradeDocumentPartRowKey(row)
  row.ordinal = partRows(partCode).length + 1
  return recomputeDerivedFields(props.entityTypeCode, row)
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

  rows[rowIndex] = recomputeDerivedFields(props.entityTypeCode, {
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

function parseDecimal(value: unknown): number | null {
  if (value == null) return null
  if (typeof value === 'number') return Number.isFinite(value) ? value : null

  const normalized = String(value).trim().replace(/,/g, '')
  if (!normalized) return null

  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : null
}

function roundTo4(value: number): number {
  return Math.round(value * 10000) / 10000
}

function isBlankValue(value: unknown): boolean {
  return value == null || String(value).trim().length === 0
}

function normalizeManagedValue(value: unknown): string {
  if (isReferenceValue(value)) return `ref:${value.id}`

  const numeric = parseDecimal(value)
  if (numeric != null) return `num:${roundTo4(numeric).toFixed(4)}`

  return String(value ?? '').trim()
}

function markForcedRowKey(rowKey: string): void {
  const next = new Set(pendingForcedRowKeys.value)
  next.add(rowKey)
  pendingForcedRowKeys.value = next
}

function shouldOverwriteManagedField(rowKey: string, fieldKey: string, currentValue: unknown, force: boolean): boolean {
  if (force) return true
  if (isBlankValue(currentValue)) return true

  const previousAutoValue = autoManagedValuesByRow.value[rowKey]?.[fieldKey]
  return !!previousAutoValue && previousAutoValue === normalizeManagedValue(currentValue)
}

function recordManagedFieldValue(rowKey: string, fieldKey: string, value: unknown): void {
  autoManagedValuesByRow.value = {
    ...autoManagedValuesByRow.value,
    [rowKey]: {
      ...(autoManagedValuesByRow.value[rowKey] ?? {}),
      [fieldKey]: normalizeManagedValue(value),
    },
  }
}

function resolveLineAmountField(documentType: string): { quantityField: string; amountField: string } | null {
  switch (documentType) {
    case TRADE_PURCHASE_RECEIPT:
    case TRADE_VENDOR_RETURN:
      return { quantityField: 'quantity', amountField: 'unit_cost' }
    case TRADE_SALES_INVOICE:
    case TRADE_CUSTOMER_RETURN:
      return { quantityField: 'quantity', amountField: 'unit_price' }
    case TRADE_INVENTORY_ADJUSTMENT:
      return { quantityField: 'quantity_delta', amountField: 'unit_cost' }
    default:
      return null
  }
}

function recomputeDerivedFields(documentType: string, row: RecordPartRow): RecordPartRow {
  const rule = resolveLineAmountField(documentType)
  if (!rule) return row

  const quantity = parseDecimal(row[rule.quantityField])
  const amount = parseDecimal(row[rule.amountField])
  if (quantity == null || amount == null) {
    return {
      ...row,
      line_amount: null,
    }
  }

  const normalizedQuantity = documentType === TRADE_INVENTORY_ADJUSTMENT ? Math.abs(quantity) : quantity

  return {
    ...row,
    line_amount: roundTo4(normalizedQuantity * amount),
  }
}

function resolveHeaderReferenceId(fieldKey: string): string | null {
  return tryExtractReferenceId(props.documentModel?.[fieldKey] ?? null)
}

function resolveAsOfDateInput(): string | null {
  const raw = props.entityTypeCode === TRADE_ITEM_PRICE_UPDATE
    ? props.documentModel?.effective_date
    : props.documentModel?.document_date_utc

  const normalized = String(raw ?? '').trim()
  return normalized || null
}

function buildLineDefaultsRequest(): TradeDocumentLineDefaultsRequest | null {
  if (props.readonly || !DOCUMENT_TYPES_WITH_LINE_DEFAULTS.has(props.entityTypeCode)) return null

  const rows = props.parts.flatMap((part) =>
    partRows(part.partCode)
      .map((row) => {
        const itemId = tryExtractReferenceId(row.item_id)
        if (!itemId) return null

        return {
          rowKey: ensureTradeDocumentPartRowKey(row),
          itemId,
          priceTypeId: props.entityTypeCode === TRADE_ITEM_PRICE_UPDATE
            ? tryExtractReferenceId(row.price_type_id)
            : null,
        }
      })
      .filter((row): row is NonNullable<typeof row> => row !== null),
  )

  if (rows.length === 0) return null

  return {
    documentType: props.entityTypeCode,
    asOfDate: resolveAsOfDateInput(),
    warehouseId: resolveHeaderReferenceId('warehouse_id'),
    priceTypeId: resolveHeaderReferenceId('price_type_id'),
    salesInvoiceId: resolveHeaderReferenceId('sales_invoice_id'),
    purchaseReceiptId: resolveHeaderReferenceId('purchase_receipt_id'),
    rows,
  }
}

function defaultsRefreshSignature(): string {
  if (!DOCUMENT_TYPES_WITH_LINE_DEFAULTS.has(props.entityTypeCode)) return ''

  const rowTokens = props.parts.flatMap((part) =>
    partRows(part.partCode).map((row) => ([
      part.partCode,
      ensureTradeDocumentPartRowKey(row),
      tryExtractReferenceId(row.item_id) ?? '',
      props.entityTypeCode === TRADE_ITEM_PRICE_UPDATE ? (tryExtractReferenceId(row.price_type_id) ?? '') : '',
    ].join(':'))),
  )

  return JSON.stringify({
    entityTypeCode: props.entityTypeCode,
    asOfDate: resolveAsOfDateInput(),
    warehouseId: resolveHeaderReferenceId('warehouse_id'),
    priceTypeId: resolveHeaderReferenceId('price_type_id'),
    salesInvoiceId: resolveHeaderReferenceId('sales_invoice_id'),
    purchaseReceiptId: resolveHeaderReferenceId('purchase_receipt_id'),
    rows: rowTokens,
  })
}

function applyResolvedDefaults(results: readonly TradeDocumentLineDefaultsRowResult[], forcedRowKeys: ReadonlySet<string>): void {
  if (results.length === 0) return

  const resultByRowKey = new Map(results.map((row) => [row.rowKey, row] as const))
  const next: RecordParts = { ...props.modelValue }
  let changed = false

  for (const part of props.parts) {
    const sourceRows = props.modelValue?.[part.partCode]?.rows ?? []
    let rows: RecordPartRow[] | null = null
    let partChanged = false

    for (let rowIndex = 0; rowIndex < sourceRows.length; rowIndex += 1) {
      const row = sourceRows[rowIndex]!
      const rowKey = ensureTradeDocumentPartRowKey(row)
      const resolved = resultByRowKey.get(rowKey)
      if (!resolved) continue

      const force = forcedRowKeys.has(rowKey)
      const patch: Record<string, unknown> = {}

      if ('price_type_id' in row && (resolved.priceType || force) && shouldOverwriteManagedField(rowKey, 'price_type_id', row.price_type_id, force)) {
        patch.price_type_id = resolved.priceType ?? null
      }

      if ('unit_price' in row && (resolved.unitPrice != null || force) && shouldOverwriteManagedField(rowKey, 'unit_price', row.unit_price, force)) {
        patch.unit_price = resolved.unitPrice ?? null
      }

      if ('currency' in row && (resolved.currency != null || force) && shouldOverwriteManagedField(rowKey, 'currency', row.currency, force)) {
        patch.currency = resolved.currency ?? null
      }

      if ('unit_cost' in row && (resolved.unitCost != null || force) && shouldOverwriteManagedField(rowKey, 'unit_cost', row.unit_cost, force)) {
        patch.unit_cost = resolved.unitCost ?? null
      }

      if (Object.keys(patch).length === 0) continue

      const nextRow = recomputeDerivedFields(props.entityTypeCode, {
        ...row,
        __row_key: rowKey,
        ...Object.fromEntries(Object.entries(patch).map(([key, value]) => [key, normalizeJsonValue(value)])),
      })

      rows ??= Array.from(sourceRows)
      rows[rowIndex] = nextRow
      partChanged = true

      for (const [fieldKey, fieldValue] of Object.entries(patch)) {
        recordManagedFieldValue(rowKey, fieldKey, fieldValue)
      }
    }

    if (!partChanged) continue

    next[part.partCode] = { rows: rows! }
    changed = true
  }

  if (changed) emit('update:modelValue', next)
}

async function refreshLineDefaults(): Promise<void> {
  const request = buildLineDefaultsRequest()
  defaultsAbortController.value?.abort()

  if (!request) return

  const controller = new AbortController()
  defaultsAbortController.value = controller
  const requestVersion = defaultsRequestVersion.value + 1
  defaultsRequestVersion.value = requestVersion
  const forcedRowKeys = new Set(pendingForcedRowKeys.value)
  pendingForcedRowKeys.value = new Set()

  try {
    const response = await resolveTradeDocumentLineDefaults(request, controller.signal)
    if (requestVersion !== defaultsRequestVersion.value) return
    applyResolvedDefaults(response.rows ?? [], forcedRowKeys)
  } catch (error) {
    if (!controller.signal.aborted) {
      console.error('Trade line defaults resolution failed.', error)
    }
  } finally {
    if (defaultsAbortController.value === controller) {
      defaultsAbortController.value = null
    }
  }
}

watch(
  defaultsRefreshSignature,
  () => void refreshLineDefaults(),
  { immediate: true },
)

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
  defaultsRequestVersion.value += 1
  defaultsAbortController.value?.abort()
  defaultsAbortController.value = null
  lookupControllers.forEach((controller) => controller.abort())
  lookupControllers.clear()
  autoManagedValuesByRow.value = {}
  pendingForcedRowKeys.value = new Set()
})

function onLookupSelect(partCode: string, rowIndex: number, fieldKey: string, item: LookupItem | null): void {
  const row = partRows(partCode)[rowIndex]
  const rowKey = row ? ensureTradeDocumentPartRowKey(row) : null

  updateCell(
    partCode,
    rowIndex,
    fieldKey,
    item
      ? { id: item.id, display: item.label }
      : null,
  )

  if (rowKey && fieldKey === 'item_id') {
    markForcedRowKey(rowKey)
  }
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
  return calculateTradeDocumentPartAmount(part, props.modelValue?.[part.partCode]?.rows)
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
        v-if="resolveTradeDocumentAmountSourceField(part)"
        class="flex items-center justify-end gap-4 border-t border-ngb-border px-4 py-3"
      >
        <span class="text-xs font-semibold uppercase tracking-[0.12em] text-ngb-muted">Amount</span>
        <span class="text-base font-semibold text-ngb-text">{{ formatAmount(partAmount(part)) }}</span>
      </div>
    </template>
  </NgbDocumentLines>
</template>
