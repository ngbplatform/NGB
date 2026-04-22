<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NgbBadge, NgbDocumentPeriodFilter as DocumentPeriodFilter, NgbIcon, NgbPageHeader, monthValueToDateOnly, relativeMonthValue } from 'ngb-ui-framework'

import {
  normalizeMonthQueryValue,
  replaceCleanRouteQuery,
  type QueryPatch,
} from 'ngb-ui-framework'
import {
  encodeReconciliationStatusFilter,
  normalizeReconciliationMode,
  normalizeReconciliationStatusFilter,
  useReconciliationLegacyQueryCompat,
} from './queryState'
import type {
  ReconciliationMode,
  ReconciliationPageDefinition,
  ReconciliationReport,
  ReconciliationRow,
  ReconciliationStatusFilter,
} from './types'

const props = defineProps<{
  definition: ReconciliationPageDefinition
}>()

const route = useRoute()
const router = useRouter()
useReconciliationLegacyQueryCompat(route, router)

const loading = ref(false)
const error = ref<string | null>(null)
const data = ref<ReconciliationReport | null>(null)

function updateQuery(patch: QueryPatch) {
  void replaceCleanRouteQuery(route, router, patch)
}

const fromMonth = computed<string>({
  get: () => {
    return normalizeMonthQueryValue(route.query.fromMonth) ?? relativeMonthValue(-1)
  },
  set: (value) => updateQuery({ fromMonth: value }),
})

const toMonth = computed<string>({
  get: () => {
    return normalizeMonthQueryValue(route.query.toMonth) ?? relativeMonthValue(0)
  },
  set: (value) => updateQuery({ toMonth: value }),
})

const mode = computed<ReconciliationMode>({
  get: () => normalizeReconciliationMode(route.query.mode),
  set: (value) => updateQuery({ mode: value }),
})

const statusFilter = computed<ReconciliationStatusFilter>({
  get: () => normalizeReconciliationStatusFilter(route.query.status),
  set: (value) => updateQuery({
    status: encodeReconciliationStatusFilter(value),
    rows: undefined,
  }),
})

const hasInvalidRange = computed(() => fromMonth.value > toMonth.value)

function fmtMoney(v: number): string {
  const n = Math.round((v ?? 0) * 100) / 100
  return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function absMoney(v: number): number {
  return Math.abs(Math.round((v ?? 0) * 100) / 100)
}

function rowKindTone(row: ReconciliationRow): 'success' | 'warn' | 'danger' | 'neutral' {
  switch (row.rowKind) {
    case 'Matched':
      return 'success'
    case 'Mismatch':
      return 'warn'
    case 'GlOnly':
    case 'OpenItemsOnly':
      return 'danger'
    default:
      return 'neutral'
  }
}

function rowKindLabel(row: ReconciliationRow): string {
  switch (row.rowKind) {
    case 'Matched':
      return 'Matched'
    case 'Mismatch':
      return 'Mismatch'
    case 'GlOnly':
      return 'GL only'
    case 'OpenItemsOnly':
      return 'Open Items only'
    default:
      return row.rowKind
  }
}

function matchesStatusFilter(row: ReconciliationRow, filter: ReconciliationStatusFilter): boolean {
  switch (filter) {
    case 'matched':
      return row.rowKind === 'Matched'
    case 'mismatch':
      return row.rowKind === 'Mismatch' || row.rowKind === 'GlOnly' || row.rowKind === 'OpenItemsOnly'
    case 'glOnly':
      return row.rowKind === 'GlOnly'
    case 'openItemsOnly':
      return row.rowKind === 'OpenItemsOnly'
    default:
      return true
  }
}

const modeDescription = computed(() => props.definition.describeMode({
  mode: mode.value,
  fromMonth: fromMonth.value,
  toMonth: toMonth.value,
}))

const calculationNotes = computed(() => {
  return mode.value === 'Balance' ? props.definition.balanceNotes : props.definition.movementNotes
})

const allRows = computed(() => data.value?.rows ?? [])

const counts = computed(() => {
  const summary = {
    all: allRows.value.length,
    matched: 0,
    mismatch: 0,
    glOnly: 0,
    openItemsOnly: 0,
  }

  for (const row of allRows.value) {
    switch (row.rowKind) {
      case 'Matched':
        summary.matched += 1
        break
      case 'Mismatch':
        summary.mismatch += 1
        break
      case 'GlOnly':
        summary.glOnly += 1
        break
      case 'OpenItemsOnly':
        summary.openItemsOnly += 1
        break
    }
  }

  return summary
})

const filteredRows = computed(() => allRows.value.filter((row) => matchesStatusFilter(row, statusFilter.value)))

const visibleRows = computed(() => {
  return [...filteredRows.value].sort((a, b) => {
    if (a.hasDiff !== b.hasDiff) return a.hasDiff ? -1 : 1

    const diffCompare = absMoney(b.diff) - absMoney(a.diff)
    if (diffCompare !== 0) return diffCompare

    const primaryCompare = a.primaryLabel.localeCompare(b.primaryLabel)
    if (primaryCompare !== 0) return primaryCompare

    const secondaryCompare = a.secondaryLabel.localeCompare(b.secondaryLabel)
    if (secondaryCompare !== 0) return secondaryCompare

    return String(a.tertiaryLabel ?? '').localeCompare(String(b.tertiaryLabel ?? ''))
  })
})

const visibleRowCount = computed(() => visibleRows.value.length)
const mismatchCount = computed(() => data.value?.mismatchRowCount ?? 0)
const allRowCount = computed(() => data.value?.rowCount ?? 0)
const visibleDiffCount = computed(() => visibleRows.value.filter((row) => row.hasDiff).length)
const largestVisibleDiff = computed(() => visibleRows.value.reduce((max, row) => Math.max(max, absMoney(row.diff)), 0))

const modeTabs = computed(() => [
  { key: 'Balance', label: 'Balance' },
  { key: 'Movement', label: 'Movement' },
])

const statusTabs = computed(() => [
  { key: 'all', label: `All (${counts.value.all})` },
  { key: 'matched', label: `Matched (${counts.value.matched})` },
  { key: 'mismatch', label: `Mismatches (${mismatchCount.value})` },
  { key: 'glOnly', label: `GL only (${counts.value.glOnly})` },
  { key: 'openItemsOnly', label: `Open Items only (${counts.value.openItemsOnly})` },
])

const hasTertiaryColumn = computed(() => !!props.definition.tertiaryColumnTitle)

function modeButtonClass(isActive: boolean): string {
  return [
    'px-2.5 py-1 text-xs font-medium ngb-focus',
    isActive
      ? 'bg-[var(--ngb-row-selected)] text-ngb-text'
      : 'text-ngb-muted hover:bg-[var(--ngb-row-hover)]',
  ].join(' ')
}

function statusButtonClass(isActive: boolean): string {
  return [
    'px-2.5 py-1 text-xs font-medium ngb-focus',
    isActive
      ? 'bg-[var(--ngb-row-selected)] text-ngb-text'
      : 'text-ngb-muted hover:bg-[var(--ngb-row-hover)]',
  ].join(' ')
}

function canOpenRow(row: ReconciliationRow): boolean {
  return !!row.openTarget
}

async function openRow(row: ReconciliationRow) {
  if (!row.openTarget) return
  await router.push(row.openTarget)
}

async function load() {
  if (hasInvalidRange.value) {
    error.value = 'From month must be earlier than or equal to To month.'
    data.value = null
    return
  }

  loading.value = true
  error.value = null
  try {
    data.value = await props.definition.load({
        fromMonthInclusive: monthValueToDateOnly(fromMonth.value) ?? `${fromMonth.value}-01`,
        toMonthInclusive: monthValueToDateOnly(toMonth.value) ?? `${toMonth.value}-01`,
      mode: mode.value,
    })
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : String(e)
    data.value = null
  } finally {
    loading.value = false
  }
}

watch(() => [fromMonth.value, toMonth.value, mode.value], () => void load(), { immediate: true })
</script>

<template>
  <div data-testid="reconciliation-page" class="h-full min-h-0 flex flex-col">
    <NgbPageHeader
      :title="definition.title"
      can-back
      @back="router.back()"
    >
      <template #secondary>
        <div class="text-xs text-ngb-muted truncate">Reconciliation</div>
      </template>
      <template #actions>
        <div class="flex flex-wrap items-center justify-end gap-2">
          <DocumentPeriodFilter
            :from-month="fromMonth"
            :to-month="toMonth"
            :disabled="loading"
            @update:from-month="fromMonth = $event"
            @update:to-month="toMonth = $event"
          />

          <div class="inline-flex items-center rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card overflow-hidden">
            <button
              v-for="tab in modeTabs"
              :key="tab.key"
              type="button"
              :class="modeButtonClass(mode === tab.key)"
              @click="mode = tab.key as ReconciliationMode"
            >
              {{ tab.label }}
            </button>
          </div>

          <div class="mx-1 h-6 w-px bg-ngb-border" aria-hidden="true" />

          <button class="ngb-iconbtn" :disabled="loading" title="Refresh" @click="load">
            <NgbIcon name="refresh" />
          </button>
        </div>
      </template>
    </NgbPageHeader>

    <div class="flex-1 min-h-0 overflow-auto p-6 space-y-4">
      <div
        v-if="error"
        class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100"
      >
        {{ error }}
      </div>

      <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
        <div class="flex flex-wrap items-center gap-2">
          <NgbBadge :tone="mode === 'Balance' ? 'success' : 'neutral'">Mode: {{ mode }}</NgbBadge>
          <NgbBadge tone="neutral">Visible rows: {{ visibleRowCount }}</NgbBadge>
          <NgbBadge :tone="mismatchCount === 0 ? 'success' : 'warn'">Mismatches: {{ mismatchCount }}</NgbBadge>
          <NgbBadge tone="neutral">Range: {{ fromMonth }} → {{ toMonth }}</NgbBadge>
        </div>
        <div class="mt-2 text-sm text-ngb-muted">
          {{ modeDescription }}
        </div>
      </div>

      <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
        <div class="text-sm font-semibold text-ngb-text">What the numbers mean</div>
        <div class="mt-1 text-sm text-ngb-muted">
          {{ definition.groupedByDescription }}
        </div>
        <ul class="mt-3 space-y-1.5 pl-5 text-sm text-ngb-muted list-disc">
          <li v-for="note in calculationNotes" :key="note">{{ note }}</li>
        </ul>
      </div>

      <div data-testid="reconciliation-kpi-grid" class="grid grid-cols-1 gap-4 md:grid-cols-5">
        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
          <div class="text-xs uppercase tracking-wide text-ngb-muted">{{ definition.ledgerNetLabel }}</div>
          <div class="mt-2 text-2xl font-semibold text-ngb-text">{{ fmtMoney(data?.totalLedgerNet ?? 0) }}</div>
          <div class="mt-1 text-xs text-ngb-muted">{{ definition.ledgerNetSummaryDescription(mode) }}</div>
        </div>
        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
          <div class="text-xs uppercase tracking-wide text-ngb-muted">Open Items Net</div>
          <div class="mt-2 text-2xl font-semibold text-ngb-text">{{ fmtMoney(data?.totalOpenItemsNet ?? 0) }}</div>
          <div class="mt-1 text-xs text-ngb-muted">Operational register {{ mode === 'Balance' ? 'balance' : 'movement' }}</div>
        </div>
        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
          <div class="text-xs uppercase tracking-wide text-ngb-muted">Diff</div>
          <div class="mt-2 text-2xl font-semibold" :class="(data?.totalDiff ?? 0) === 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-amber-600 dark:text-amber-400'">
            {{ fmtMoney(data?.totalDiff ?? 0) }}
          </div>
          <div class="mt-1 text-xs text-ngb-muted">{{ definition.diffSummaryDescription }}</div>
        </div>
        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
          <div class="text-xs uppercase tracking-wide text-ngb-muted">Rows</div>
          <div class="mt-2 text-2xl font-semibold text-ngb-text">{{ allRowCount }}</div>
          <div class="mt-1 text-sm text-ngb-muted">{{ visibleRowCount }} visible</div>
        </div>
        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
          <div class="text-xs uppercase tracking-wide text-ngb-muted">Largest visible diff</div>
          <div class="mt-2 text-2xl font-semibold" :class="largestVisibleDiff === 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-amber-600 dark:text-amber-400'">
            {{ fmtMoney(largestVisibleDiff) }}
          </div>
          <div class="mt-1 text-sm text-ngb-muted">{{ visibleDiffCount }} visible mismatches</div>
        </div>
      </div>

      <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
        <div class="inline-flex items-center rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card overflow-hidden">
          <button
            v-for="tab in statusTabs"
            :key="tab.key"
            type="button"
            :class="statusButtonClass(statusFilter === tab.key)"
            @click="statusFilter = tab.key as ReconciliationStatusFilter"
          >
            {{ tab.label }}
          </button>
        </div>
        <div class="mt-2 text-sm text-ngb-muted">
          Filter rows by reconciliation outcome. “Mismatches” includes direct diffs, GL-only rows, and Open Items-only rows.
        </div>
      </div>

      <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card overflow-hidden">
        <div class="border-b border-ngb-border px-4 py-3">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <div>
              <div class="text-sm font-semibold text-ngb-text">Rows</div>
              <div class="text-sm text-ngb-muted">
                {{ definition.rowsDescription }}
              </div>
            </div>
            <div class="text-sm text-ngb-muted">
              {{ visibleRowCount }} / {{ allRowCount }} rows shown
            </div>
          </div>
        </div>

        <div v-if="loading" class="px-4 py-8 text-sm text-ngb-muted">
          Loading reconciliation…
        </div>

        <div
          v-else-if="!visibleRows.length"
          class="px-4 py-8 text-sm text-ngb-muted"
        >
          {{ definition.noRowsMessage }}
        </div>

        <div v-else data-testid="reconciliation-table-wrap" class="overflow-auto">
          <table class="min-w-full text-sm">
            <thead>
              <tr class="border-b border-ngb-border bg-[var(--ngb-grid-header)] text-left text-ngb-muted">
                <th class="px-4 py-3 font-medium">Status</th>
                <th class="px-4 py-3 font-medium">{{ definition.primaryColumnTitle }}</th>
                <th class="px-4 py-3 font-medium">{{ definition.secondaryColumnTitle }}</th>
                <th v-if="hasTertiaryColumn" class="px-4 py-3 font-medium">{{ definition.tertiaryColumnTitle }}</th>
                <th class="px-4 py-3 font-medium">Why</th>
                <th class="px-4 py-3 text-right font-medium">{{ definition.ledgerNetLabel }}</th>
                <th class="px-4 py-3 text-right font-medium">Open Items Net</th>
                <th class="px-4 py-3 text-right font-medium">Diff</th>
                <th class="px-4 py-3 text-right font-medium">Action</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="row in visibleRows"
                :key="row.key"
                class="border-b border-ngb-border last:border-b-0"
              >
                <td class="px-4 py-3 align-top">
                  <NgbBadge :tone="rowKindTone(row)">{{ rowKindLabel(row) }}</NgbBadge>
                </td>
                <td class="px-4 py-3 align-top text-ngb-text">{{ row.primaryLabel }}</td>
                <td class="px-4 py-3 align-top text-ngb-text">{{ row.secondaryLabel }}</td>
                <td v-if="hasTertiaryColumn" class="px-4 py-3 align-top text-ngb-text">{{ row.tertiaryLabel ?? '—' }}</td>
                <td class="px-4 py-3 align-top text-ngb-muted">{{ definition.explainRow(row) }}</td>
                <td class="px-4 py-3 align-top text-right text-ngb-text">{{ fmtMoney(row.ledgerNet) }}</td>
                <td class="px-4 py-3 align-top text-right text-ngb-text">{{ fmtMoney(row.openItemsNet) }}</td>
                <td class="px-4 py-3 align-top text-right" :class="row.hasDiff ? 'text-amber-600 dark:text-amber-400' : 'text-emerald-600 dark:text-emerald-400'">
                  {{ fmtMoney(row.diff) }}
                </td>
                <td class="px-4 py-3 align-top text-right">
                  <button
                    v-if="canOpenRow(row)"
                    class="ngb-iconbtn"
                    title="Open Items"
                    @click.stop="openRow(row)"
                  >
                    <NgbIcon name="open-in-new" />
                  </button>
                  <span v-else class="text-ngb-muted">—</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>
