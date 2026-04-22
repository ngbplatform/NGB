<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import NgbBadge from '../primitives/NgbBadge.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import NgbInput from '../primitives/NgbInput.vue'
import NgbLookup from '../primitives/NgbLookup.vue'
import NgbSelect from '../primitives/NgbSelect.vue'
import { useLookupStore } from '../lookup/store'
import { buildLookupFieldTargetUrl } from '../lookup/navigation'
import { getGeneralJournalEntryAccountContext } from './generalJournalEntryApi'
import {
  createGeneralJournalEntryLine,
  formatGeneralJournalEntryMoney,
  parseGeneralJournalEntryAmount,
} from './generalJournalEntry'
import type {
  GeneralJournalEntryAccountContextDto,
  GeneralJournalEntryDimensionRuleDto,
  GeneralJournalEntryEditorLineModel,
} from './generalJournalEntryTypes'

const props = withDefaults(
  defineProps<{
    modelValue: GeneralJournalEntryEditorLineModel[]
    readonly?: boolean
    preloadedAccountContexts?: Record<string, GeneralJournalEntryAccountContextDto | null>
  }>(),
  {
    readonly: false,
    preloadedAccountContexts: () => ({}),
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: GeneralJournalEntryEditorLineModel[]): void
}>()

const lookupStore = useLookupStore()
const router = useRouter()
const route = useRoute()

const accountItemsByRow = ref<Record<string, GeneralJournalEntryEditorLineModel['account'][]>>({})
const dimensionItemsByCell = ref<Record<string, GeneralJournalEntryEditorLineModel['account'][]>>({})
const accountContextsByRow = ref<Record<string, GeneralJournalEntryAccountContextDto | null>>({})
const accountContextCache = ref<Record<string, GeneralJournalEntryAccountContextDto | null>>({})
const loadingContexts = ref<Record<string, string>>({})

const rows = computed(() => props.modelValue ?? [])
const canEdit = computed(() => !props.readonly)

const sideOptions = [
  { value: 1, label: 'Debit' },
  { value: 2, label: 'Credit' },
]

const totals = computed(() => {
  let debit = 0
  let credit = 0
  for (const row of rows.value) {
    const amount = parseGeneralJournalEntryAmount(row.amount)
    if (!Number.isFinite(amount)) continue
    if (row.side === 1) debit += amount
    else credit += amount
  }
  return {
    debit,
    credit,
    diff: debit - credit,
  }
})

function emitRows(next: GeneralJournalEntryEditorLineModel[]) {
  emit('update:modelValue', next)
}

function updateRow(rowIndex: number, patch: Partial<GeneralJournalEntryEditorLineModel>) {
  const next = rows.value.map((row, index) => {
    if (index !== rowIndex) return { ...row, dimensions: { ...(row.dimensions ?? {}) } }
    return {
      ...row,
      ...patch,
      dimensions: { ...(patch.dimensions ?? row.dimensions ?? {}) },
    }
  })
  emitRows(next)
}

function addRow() {
  if (!canEdit.value) return
  emitRows([
    ...rows.value.map((row) => ({ ...row, dimensions: { ...(row.dimensions ?? {}) } })),
    createGeneralJournalEntryLine(),
  ])
}

function removeRow(rowIndex: number) {
  if (!canEdit.value) return
  const next = rows.value
    .filter((_, index) => index !== rowIndex)
    .map((row) => ({ ...row, dimensions: { ...(row.dimensions ?? {}) } }))
  emitRows(next.length > 0 ? next : [createGeneralJournalEntryLine()])
}

function humanizeDimensionLabel(code: string): string {
  const raw = String(code ?? '').trim()
  if (!raw) return 'Dimension'
  const last = raw.includes('.') ? raw.split('.').pop() ?? raw : raw
  return last
    .replace(/_/g, ' ')
    .replace(/\b\w/g, (m) => m.toUpperCase())
}

function cellKey(rowKey: string, dimensionId: string): string {
  return `${rowKey}:${dimensionId}`
}

watch(
  () => props.preloadedAccountContexts,
  (next) => {
    accountContextCache.value = { ...(next ?? {}) }
  },
  { immediate: true, deep: true },
)

function hasCachedAccountContext(accountId: string): boolean {
  return Object.prototype.hasOwnProperty.call(accountContextCache.value, accountId)
}

function rowByClientKey(clientKey: string): GeneralJournalEntryEditorLineModel | undefined {
  return rows.value.find((entry) => entry.clientKey === clientKey)
}

function clearLoadingContext(rowKey: string) {
  if (!loadingContexts.value[rowKey]) return
  const next = { ...loadingContexts.value }
  delete next[rowKey]
  loadingContexts.value = next
}

async function ensureAccountContext(row: GeneralJournalEntryEditorLineModel) {
  const accountId = row.account?.id
  if (!accountId) {
    clearLoadingContext(row.clientKey)
    accountContextsByRow.value = { ...accountContextsByRow.value, [row.clientKey]: null }
    return
  }

  const existing = accountContextsByRow.value[row.clientKey]
  if (existing?.accountId === accountId) return

  if (hasCachedAccountContext(accountId)) {
    accountContextsByRow.value = {
      ...accountContextsByRow.value,
      [row.clientKey]: accountContextCache.value[accountId] ?? null,
    }
    return
  }

  const requestKey = `${row.clientKey}:${accountId}`
  if (loadingContexts.value[row.clientKey] === requestKey) return

  loadingContexts.value = { ...loadingContexts.value, [row.clientKey]: requestKey }
  try {
    const context = await getGeneralJournalEntryAccountContext(accountId)
    if (loadingContexts.value[row.clientKey] !== requestKey) return

    const latestRow = rowByClientKey(row.clientKey)
    if (latestRow?.account?.id !== accountId) return

    accountContextCache.value = { ...accountContextCache.value, [accountId]: context }
    accountContextsByRow.value = { ...accountContextsByRow.value, [row.clientKey]: context }
  } catch {
    if (loadingContexts.value[row.clientKey] !== requestKey) return
    accountContextsByRow.value = { ...accountContextsByRow.value, [row.clientKey]: null }
  } finally {
    if (loadingContexts.value[row.clientKey] === requestKey) clearLoadingContext(row.clientKey)
  }
}

watch(
  () => rows.value.map((row) => `${row.clientKey}:${row.account?.id ?? ''}`).join('|'),
  async () => {
    for (const row of rows.value) {
      await ensureAccountContext(row)
    }
  },
  { immediate: true },
)

function contextForRow(row: GeneralJournalEntryEditorLineModel): GeneralJournalEntryAccountContextDto | null {
  return accountContextsByRow.value[row.clientKey] ?? null
}

function selectedDimensionItem(row: GeneralJournalEntryEditorLineModel, rule: GeneralJournalEntryDimensionRuleDto) {
  return row.dimensions?.[rule.dimensionId] ?? null
}

async function onAccountQuery(row: GeneralJournalEntryEditorLineModel, query: string) {
  const q = String(query ?? '').trim()
  if (!q) {
    accountItemsByRow.value = { ...accountItemsByRow.value, [row.clientKey]: [] }
    return
  }

  const items = await lookupStore.searchCoa(q)
  accountItemsByRow.value = { ...accountItemsByRow.value, [row.clientKey]: items }
}

function onAccountSelect(rowIndex: number, item: GeneralJournalEntryEditorLineModel['account']) {
  const key = rows.value[rowIndex]?.clientKey
  if (key) clearLoadingContext(key)

  updateRow(rowIndex, { account: item, dimensions: {} })

  if (!key) return

  if (!item) {
    accountContextsByRow.value = { ...accountContextsByRow.value, [key]: null }
    return
  }

  if (hasCachedAccountContext(item.id)) {
    accountContextsByRow.value = {
      ...accountContextsByRow.value,
      [key]: accountContextCache.value[item.id] ?? null,
    }
    return
  }

  accountContextsByRow.value = { ...accountContextsByRow.value, [key]: null }
}

async function onDimensionQuery(
  row: GeneralJournalEntryEditorLineModel,
  rule: GeneralJournalEntryDimensionRuleDto,
  query: string,
) {
  const q = String(query ?? '').trim()
  const lookup = rule.lookup
  const key = cellKey(row.clientKey, rule.dimensionId)

  if (!q || !lookup) {
    dimensionItemsByCell.value = { ...dimensionItemsByCell.value, [key]: [] }
    return
  }

  let items: GeneralJournalEntryEditorLineModel['account'][] = []
  if (lookup.kind === 'catalog') items = await lookupStore.searchCatalog(lookup.catalogType, q)
  else if (lookup.kind === 'coa') items = await lookupStore.searchCoa(q)
  else if (lookup.kind === 'document') items = await lookupStore.searchDocuments(lookup.documentTypes, q)

  dimensionItemsByCell.value = { ...dimensionItemsByCell.value, [key]: items }
}

function onDimensionSelect(
  rowIndex: number,
  rule: GeneralJournalEntryDimensionRuleDto,
  item: GeneralJournalEntryEditorLineModel['account'],
) {
  const current = rows.value[rowIndex]
  if (!current) return

  const nextDimensions = { ...(current.dimensions ?? {}) }
  if (!item) delete nextDimensions[rule.dimensionId]
  else nextDimensions[rule.dimensionId] = item

  updateRow(rowIndex, { dimensions: nextDimensions })
}

function dimensionItems(row: GeneralJournalEntryEditorLineModel, rule: GeneralJournalEntryDimensionRuleDto) {
  return dimensionItemsByCell.value[cellKey(row.clientKey, rule.dimensionId)] ?? []
}

async function openAccount(row: GeneralJournalEntryEditorLineModel) {
  const target = await buildLookupFieldTargetUrl({
    hint: { kind: 'coa' },
    value: row.account,
    route,
  })

  if (!target) return
  await router.push(target)
}

async function openDimension(row: GeneralJournalEntryEditorLineModel, rule: GeneralJournalEntryDimensionRuleDto) {
  const target = await buildLookupFieldTargetUrl({
    hint: rule.lookup ?? null,
    value: selectedDimensionItem(row, rule),
    route,
  })

  if (!target) return
  await router.push(target)
}

function badgeToneForDiff(): 'success' | 'warn' {
  return Math.abs(totals.value.diff) < 0.000001 ? 'success' : 'warn'
}
</script>

<template>
  <div class="overflow-hidden rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card">
    <div class="border-b border-ngb-border px-4 py-3">
      <div class="text-sm font-semibold text-ngb-text">Lines</div>
      <div class="mt-0.5 text-xs text-ngb-muted">Debit/credit lines with dimension-aware lookups resolved from the selected account.</div>

      <div class="mt-3 flex flex-wrap items-center gap-2">
        <NgbBadge tone="neutral">Debit: {{ formatGeneralJournalEntryMoney(totals.debit) }}</NgbBadge>
        <NgbBadge tone="neutral">Credit: {{ formatGeneralJournalEntryMoney(totals.credit) }}</NgbBadge>
        <NgbBadge :tone="badgeToneForDiff()">Difference: {{ formatGeneralJournalEntryMoney(totals.diff) }}</NgbBadge>
      </div>
    </div>

    <table class="w-full table-fixed text-sm">
      <colgroup>
        <col style="width:44px" />
        <col style="width:140px" />
        <col />
        <col style="width:160px" />
        <col style="width:320px" />
        <col style="width:40px" />
      </colgroup>

      <thead class="bg-ngb-bg text-xs text-ngb-muted">
        <tr>
          <th class="border-r border-dotted border-ngb-border px-2 py-2 text-right font-semibold">#</th>
          <th class="border-r border-dotted border-ngb-border px-3 py-2 font-semibold truncate">Side</th>
          <th class="border-r border-dotted border-ngb-border px-3 py-2 font-semibold truncate">Account</th>
          <th class="border-r border-dotted border-ngb-border px-3 py-2 font-semibold truncate">Amount</th>
          <th class="border-r border-dotted border-ngb-border px-3 py-2 font-semibold truncate">Memo</th>
          <th class="px-2 py-2" />
        </tr>
      </thead>

      <tbody>
        <template v-for="(row, rowIndex) in rows" :key="row.clientKey">
          <tr class="border-t border-ngb-border align-top transition-colors hover:bg-ngb-bg">
            <td class="border-r border-dotted border-ngb-border px-2 py-1 align-top text-right text-ngb-muted">
              <div class="flex h-8 items-center justify-end">{{ rowIndex + 1 }}</div>
            </td>

            <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
              <NgbSelect
                :model-value="row.side"
                :options="sideOptions"
                :disabled="!canEdit"
                variant="grid"
                @update:model-value="updateRow(rowIndex, { side: Number($event ?? 1) })"
              />
            </td>

            <td class="border-r border-dotted border-ngb-border px-0 py-1 align-top">
              <NgbLookup
                :model-value="row.account"
                :items="accountItemsByRow[row.clientKey] ?? []"
                :readonly="!canEdit"
                :show-open="!!row.account"
                :show-clear="canEdit && !!row.account"
                variant="grid"
                placeholder="Type account code or name…"
                @query="onAccountQuery(row, $event)"
                @update:model-value="onAccountSelect(rowIndex, $event)"
                @open="void openAccount(row)"
              />
            </td>

            <td class="border-r border-dotted border-ngb-border px-1 py-1 align-top">
              <NgbInput
                :model-value="row.amount"
                type="number"
                :disabled="!canEdit"
                variant="grid"
                placeholder="0.00"
                @update:model-value="updateRow(rowIndex, { amount: String($event ?? '') })"
              />
            </td>

            <td class="border-r border-dotted border-ngb-border px-1 py-1 align-top">
              <NgbInput
                :model-value="row.memo"
                :disabled="!canEdit"
                variant="grid"
                placeholder="Memo"
                @update:model-value="updateRow(rowIndex, { memo: String($event ?? '') })"
              />
            </td>

            <td class="px-1 py-1 align-middle">
              <button
                type="button"
                class="flex h-8 w-8 items-center justify-center rounded-[var(--ngb-radius)] text-ngb-muted hover:bg-ngb-bg hover:text-ngb-text ngb-focus"
                title="Delete"
                :disabled="!canEdit"
                @click="removeRow(rowIndex)"
              >
                <NgbIcon name="trash" :size="16" />
              </button>
            </td>
          </tr>

          <tr v-if="row.account && loadingContexts[row.clientKey]" class="border-t border-ngb-border bg-ngb-bg/40">
            <td colspan="6" class="px-4 py-3 text-sm text-ngb-muted">Loading dimension rules…</td>
          </tr>

          <tr v-if="contextForRow(row)?.dimensionRules?.length" class="border-t border-ngb-border bg-ngb-bg/40">
            <td colspan="6" class="px-4 py-3">
              <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                <div
                  v-for="rule in contextForRow(row)?.dimensionRules ?? []"
                  :key="rule.dimensionId"
                  class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-3"
                >
                  <div class="mb-2 flex items-center gap-2">
                    <div class="text-xs font-semibold text-ngb-text">{{ humanizeDimensionLabel(rule.dimensionCode) }}</div>
                    <NgbBadge v-if="rule.isRequired" tone="warn">Required</NgbBadge>
                  </div>

                  <NgbLookup
                    :model-value="selectedDimensionItem(row, rule)"
                    :items="dimensionItems(row, rule)"
                    :disabled="!rule.lookup"
                    :readonly="!canEdit"
                    :show-open="!!rule.lookup && !!selectedDimensionItem(row, rule)"
                    :show-clear="canEdit && !!rule.lookup && !!selectedDimensionItem(row, rule)"
                    placeholder="Type to search…"
                    @query="onDimensionQuery(row, rule, $event)"
                    @update:model-value="onDimensionSelect(rowIndex, rule, $event)"
                    @open="void openDimension(row, rule)"
                  />
                </div>
              </div>
            </td>
          </tr>
        </template>
      </tbody>
    </table>

    <div class="flex items-center justify-between gap-3 border-t border-ngb-border px-3 py-2">
      <div class="text-sm text-ngb-muted">{{ rows.length }} line(s)</div>

      <button
        type="button"
        class="inline-flex h-9 items-center gap-2 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 text-sm text-ngb-text hover:bg-ngb-bg ngb-focus"
        :disabled="!canEdit"
        @click="addRow"
      >
        <NgbIcon name="plus" :size="16" />
        <span>Add line</span>
      </button>
    </div>
  </div>
</template>
