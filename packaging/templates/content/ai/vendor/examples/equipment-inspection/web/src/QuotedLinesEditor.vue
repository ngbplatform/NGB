<script setup lang="ts">
import { onBeforeUnmount, reactive, shallowRef, watch } from 'vue'
import { NgbDocumentLines, NgbInput, NgbSelect } from '@ngbplatform/ui'
import LineReferenceSummary from './LineReferenceSummary.vue'

export type QuotedLine = {
  id: string
  item: string
  quantity: number
  unitPriceMinor: number | null
  pricingDate: string | null
  totalMinor: number | null
}

const props = defineProps<{
  modelValue: QuotedLine[]
  businessDate: string
  documentKey: string
  readonly?: boolean
  items: { value: string | null, label: string }[]
  loadItem?: (id: string, signal: AbortSignal) => Promise<{ preparationMinutes: number }>
  quote: (item: string, date: string, signal: AbortSignal) => Promise<{
    unitPriceMinor: number
    pricingDate: string
  }>
}>()
const emit = defineEmits<{ 'update:modelValue': [rows: QuotedLine[]] }>()
const draftRows = shallowRef(props.modelValue)
watch(() => props.modelValue, rows => {
  draftRows.value = rows
})
const pending = new Map<string, AbortController>()
const messages = reactive<Record<string, string>>({})
const failedQuotes = reactive(new Set<string>())
const columns = [
  { key: 'item', label: 'Item' },
  { key: 'quantity', label: 'Quantity' },
  { key: 'unitPriceMinor', label: 'Unit price (cents)' },
  { key: 'pricingDate', label: 'Pricing date' },
  { key: 'totalMinor', label: 'Total (cents)' },
]

function publish(rows: QuotedLine[]): void {
  draftRows.value = rows
  emit('update:modelValue', rows)
}

function replace(id: string, change: Partial<QuotedLine>): void {
  publish(draftRows.value.map(row => row.id === id ? { ...row, ...change } : row))
}

function invalidate(id: string): void {
  pending.get(id)?.abort()
  pending.delete(id)
  delete messages[id]
  failedQuotes.delete(id)
}

async function refresh(id: string, item: string): Promise<void> {
  // Only enabled controls and the editable-date watcher initiate a quote.
  invalidate(id)
  replace(id, { item, unitPriceMinor: null, pricingDate: null, totalMinor: null })
  if (!item || !props.businessDate) return

  const date = props.businessDate
  const request = new AbortController()
  pending.set(id, request)
  messages[id] = 'Loading price…'
  try {
    const result = await props.quote(item, date, request.signal)
    const row = draftRows.value.find(candidate => candidate.id === id)
    // Some transports resolve after abort. Identity and input checks are required too.
    if (pending.get(id) !== request || !row || row.item !== item || props.businessDate !== date || props.readonly) return
    if (!Number.isSafeInteger(result.unitPriceMinor) || result.unitPriceMinor <= 0
      || !Number.isSafeInteger(result.unitPriceMinor * row.quantity)) {
      failedQuotes.add(id)
      messages[id] = 'The price cannot be represented in minor currency units.'
      return
    }
    replace(id, { ...result, totalMinor: result.unitPriceMinor * row.quantity })
    delete messages[id]
  } catch {
    if (pending.get(id) === request) {
      failedQuotes.add(id)
      messages[id] = 'Price unavailable. Retry the calculation.'
    }
  } finally {
    if (pending.get(id) === request) pending.delete(id)
  }
}

function quantity(row: QuotedLine, value: string | number): void {
  const count = Number(value)
  if (!Number.isSafeInteger(count) || count <= 0
    || (row.unitPriceMinor !== null && !Number.isSafeInteger(count * row.unitPriceMinor))) {
    messages[row.id] = 'Enter a positive whole quantity within the supported amount range.'
    return
  }
  if (!pending.has(row.id) && !failedQuotes.has(row.id)) delete messages[row.id]
  replace(row.id, { quantity: count, totalMinor: row.unitPriceMinor === null ? null : row.unitPriceMinor * count })
}

function add(): void {
  publish([...draftRows.value, {
    id: crypto.randomUUID(),
    item: '',
    quantity: 1,
    unitPriceMinor: null,
    pricingDate: null,
    totalMinor: null,
  }])
}

function remove(index: number): void {
  invalidate(draftRows.value[index].id)
  publish(draftRows.value.filter((_, position) => position !== index))
}

function reorder(from: number, to: number): void {
  const rows = [...draftRows.value]
  const [row] = rows.splice(from, 1)
  rows.splice(to, 0, row)
  publish(rows)
}

watch(() => props.businessDate, () => {
  if (!props.readonly) {
    for (const row of draftRows.value) void refresh(row.id, row.item)
  }
})
watch(() => props.modelValue.map(row => row.id), ids => {
  for (const id of Object.keys(messages)) {
    if (!ids.includes(id)) invalidate(id)
  }
})
watch(() => props.readonly, readonly => {
  if (readonly) {
    for (const id of Object.keys(messages)) invalidate(id)
  }
})
watch(() => props.documentKey, () => {
  for (const id of Object.keys(messages)) invalidate(id)
})
onBeforeUnmount(() => {
  for (const id of pending.keys()) invalidate(id)
})
</script>

<template>
  <NgbDocumentLines
    title="Lines"
    :rows="draftRows"
    :columns="columns"
    :row-key="row => row.id"
    :readonly="readonly"
    add-label="Add line"
    @add="add"
    @remove="remove"
    @reorder="reorder"
  >
    <template #cell="{ row, column }">
      <div v-if="column.key === 'item'">
        <NgbSelect
          :model-value="row.item || null"
          :options="items"
          :disabled="readonly"
          aria-label="Item"
          variant="grid"
          @update:model-value="value => refresh(row.id, String(value ?? ''))"
        />
        <span v-if="messages[row.id]" role="status">{{ messages[row.id] }}</span>
        <button v-if="failedQuotes.has(row.id) && !readonly" type="button" class="ngb-focus underline"
          @click="refresh(row.id, row.item)">Retry price</button>
      </div>
      <NgbInput
        v-else-if="column.key === 'quantity'"
        :model-value="row.quantity"
        :readonly="readonly"
        type="number"
        title="Quantity"
        variant="grid"
        @update:model-value="value => quantity(row, value)"
      />
      <span v-else class="inline-flex min-h-8 items-center px-2">{{ row[column.key as keyof QuotedLine] ?? '—' }}</span>
    </template>
  </NgbDocumentLines>
  <LineReferenceSummary v-if="loadItem" :rows="draftRows" :document-key="documentKey" :load-item="loadItem" />
</template>
