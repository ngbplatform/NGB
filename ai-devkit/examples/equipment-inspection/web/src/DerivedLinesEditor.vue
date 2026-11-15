<script setup lang="ts">
import { onBeforeUnmount, ref, shallowRef, toRaw, watch } from 'vue'
import { ApiError, NgbDocumentLines, NgbLookup, type LookupItem } from '@ngbplatform/ui'

export type DerivedLine = {
  id: string
  item: LookupItem
  amountMinor: number
}
export type DerivedDocument = {
  source: LookupItem | null
  customer: LookupItem | null
  lines: DerivedLine[]
  feeMinor: number | null
  totalMinor: number | null
}

const props = defineProps<{
  modelValue: DerivedDocument
  documentKey: string
  businessDate: string
  readonly?: boolean
  eligibleSources: LookupItem[]
  canOpenItem: (item: LookupItem) => boolean
  loadSource: (id: string, date: string, signal: AbortSignal) => Promise<{
    customer: LookupItem
    lines: DerivedLine[]
  }>
  loadFee: (lines: DerivedLine[], date: string, signal: AbortSignal) => Promise<number>
}>()
const emit = defineEmits<{
  'update:modelValue': [value: DerivedDocument]
  openItem: [item: LookupItem]
}>()
const draft = shallowRef(props.modelValue)
const message = ref('')
const busy = ref(false)
let pending: AbortController | null = null
const columns = [{ key: 'item', label: 'Item' }, { key: 'amountMinor', label: 'Amount (cents)' }]

function publish(value: DerivedDocument): void {
  draft.value = value
  emit('update:modelValue', value)
}

function invalidate(): void {
  pending?.abort()
  pending = null
  busy.value = false
  message.value = ''
}

async function choose(source: LookupItem | null): Promise<void> {
  if (props.readonly) return
  invalidate()
  // Invalidate the entire dependency chain before any request, including its header totals.
  publish({ source, customer: null, lines: [], feeMinor: null, totalMinor: null })
  if (!source || !props.businessDate) return

  const request = new AbortController()
  const date = props.businessDate
  pending = request
  busy.value = true
  try {
    const result = await props.loadSource(source.id, date, request.signal)
    if (pending !== request) return
    // Both stages share the same lifetime. A second independent counter would let
    // a late fee response restore totals after the source has been cleared.
    const feeMinor = await props.loadFee(result.lines, date, request.signal)
    if (pending !== request) return
    const totalMinor = result.lines.reduce((sum, line) => sum + line.amountMinor, feeMinor)
    if (![feeMinor, totalMinor, ...result.lines.map(line => line.amountMinor)]
      .every(value => Number.isSafeInteger(value) && value >= 0)) {
      throw new Error('Invalid monetary response.')
    }
    publish({ source, customer: result.customer, lines: result.lines, feeMinor, totalMinor })
  } catch (error) {
    if (pending === request) {
      message.value = error instanceof ApiError && error.status >= 400 && error.status < 500
        ? error.message
        : 'Unable to calculate this document. Retry or choose another source.'
    }
  } finally {
    if (pending === request) {
      pending = null
      busy.value = false
    }
  }
}

watch(() => props.modelValue, value => {
  // A loaded/reloaded document is authoritative. Hydration is not a source edit.
  if (toRaw(value) !== toRaw(draft.value)) invalidate()
  draft.value = value
})
watch(() => props.businessDate, () => {
  void choose(draft.value.source)
})
watch(() => props.documentKey, invalidate)
watch(() => props.readonly, value => {
  if (value) invalidate()
})
onBeforeUnmount(invalidate)
</script>

<template>
  <div class="space-y-4">
    <NgbLookup
      :model-value="draft.source"
      :items="eligibleSources"
      :readonly="readonly"
      :show-clear="!readonly"
      label="Source"
      placeholder="Choose a confirmed source"
      hint="Charges are copied from the selected source. Change the source to recalculate them."
      @update:model-value="choose"
    />
    <p v-if="busy" role="status">Calculating charges…</p>
    <div v-if="message" role="alert">
      {{ message }}
      <button type="button" class="ngb-focus underline" @click="choose(draft.source)">Retry</button>
    </div>
    <p>Customer: {{ draft.customer?.label ?? '—' }}</p>
    <NgbDocumentLines
      title="Charges"
      :rows="draft.lines"
      :columns="columns"
      :row-key="row => row.id"
      readonly
    >
      <template #cell="{ row, column }">
        <NgbLookup
          v-if="column.key === 'item'"
          :model-value="row.item"
          :items="[]"
          readonly
          :show-open="canOpenItem(row.item)"
          variant="grid"
          @open="emit('openItem', row.item)"
        />
        <span v-else class="inline-flex min-h-8 items-center px-2">{{ row.amountMinor }}</span>
      </template>
    </NgbDocumentLines>
    <p>Fee (cents): {{ draft.feeMinor ?? '—' }}</p>
    <p>Total (cents): {{ draft.totalMinor ?? '—' }}</p>
  </div>
</template>
