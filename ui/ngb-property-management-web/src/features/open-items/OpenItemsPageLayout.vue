<script setup lang="ts">
import { NgbBadge, NgbButton, NgbIcon, NgbLookup, NgbPageHeader, NgbRegisterGrid, NgbTabs } from 'ngb-ui-framework'

import { applyDocumentLabel, docLabel, fmtDateOnly, fmtMoney, type OpenItemsApplyResultLine, type OpenItemsLookupItem } from './shared'
import type { OpenItemsAppliedAllocationView, OpenItemsGridDefinition, OpenItemsPageResultView, OpenItemsTabKey } from './presentation'

type LookupControl = {
  key: string
  value: OpenItemsLookupItem | null
  items: OpenItemsLookupItem[]
  placeholder: string
  widthClass: string
  onQuery: (query: string) => void | Promise<void>
  onSelect: (value: OpenItemsLookupItem | null) => void
  onOpen: () => void | Promise<void>
}

const props = defineProps<{
  title: string
  lookups: LookupControl[]
  loading: boolean
  error?: string | null
  contextReady: boolean
  emptyStateMessage: string
  contextBadges: string[]
  focusedContextBadge?: string | null
  summary: { totalOutstanding: number; totalCredit: number }
  pageResult?: OpenItemsPageResultView | null
  activeTab: OpenItemsTabKey
  tabs: Array<{ key: OpenItemsTabKey; label: string }>
  chargeGrid: OpenItemsGridDefinition
  creditGrid: OpenItemsGridDefinition
  appliedRows: OpenItemsAppliedAllocationView[]
  appliedSubtitle: string
  appliedEmptyMessage: string
  highlightedApplyIds: string[]
  isContextAllocation?: (allocation: OpenItemsAppliedAllocationView) => boolean
  resolveChargeTypeLabel: (documentType: string) => string
  resolveCreditTypeLabel: (documentType?: string | null) => string
  openAppliedDocument: (documentType: string, documentId: string) => void | Promise<void>
  openApplyDocument: (applyId: string) => void | Promise<void>
  requestUnapply: (line: OpenItemsApplyResultLine) => void
  canRefresh: boolean
  canApply: boolean
}>()

const emit = defineEmits<{
  (e: 'back'): void
  (e: 'refresh'): void
  (e: 'apply'): void
  (e: 'dismissPageResult'): void
  (e: 'update:activeTab', value: OpenItemsTabKey): void
}>()

function isHighlightedApplyId(applyId: string): boolean {
  return props.highlightedApplyIds.includes(applyId)
}

function allocationTone(allocation: OpenItemsAppliedAllocationView): string {
  if (isHighlightedApplyId(allocation.applyId)) return 'bg-blue-50/70 dark:bg-blue-950/20'
  if (props.isContextAllocation?.(allocation)) return 'bg-amber-50/60 dark:bg-amber-950/15'
  return 'bg-ngb-card'
}

function buildUnapplyLine(allocation: OpenItemsAppliedAllocationView): OpenItemsApplyResultLine {
  return {
    key: allocation.applyId,
    applyId: allocation.applyId,
    creditDocumentId: allocation.creditDocumentId,
    creditDocumentType: allocation.creditDocumentType,
    creditLabel: docLabel(allocation.creditDocumentNumber, allocation.creditDocumentDisplay, allocation.creditDocumentId),
    chargeDocumentId: allocation.chargeDocumentId,
    chargeLabel: docLabel(allocation.chargeNumber, allocation.chargeDisplay, allocation.chargeDocumentId),
    appliedOnUtc: allocation.appliedOnUtc,
    amount: allocation.amount,
  }
}
</script>

<template>
  <div class="flex h-full min-h-0 min-w-0 flex-col overflow-x-hidden" data-testid="open-items-page">
    <NgbPageHeader :title="title" can-back @back="emit('back')">
      <template #secondary>
        <div class="text-xs text-ngb-muted truncate">Open Items</div>
      </template>

      <template #actions>
        <div class="flex flex-wrap items-center justify-end gap-2">
          <div
            v-for="lookup in lookups"
            :key="lookup.key"
            :class="lookup.widthClass"
          >
            <NgbLookup
              :model-value="lookup.value"
              :items="lookup.items"
              label=""
              :placeholder="lookup.placeholder"
              variant="compact"
              :show-open="!!lookup.value"
              :show-clear="!!lookup.value"
              @query="lookup.onQuery"
              @update:modelValue="lookup.onSelect"
              @open="void lookup.onOpen()"
            />
          </div>

          <div class="mx-1 hidden h-6 w-px bg-ngb-border sm:block" aria-hidden="true" />

          <button class="ngb-iconbtn" :disabled="loading || !canRefresh" title="Refresh" @click="emit('refresh')">
            <NgbIcon name="refresh" />
          </button>

          <button class="ngb-iconbtn" :disabled="loading || !canApply" title="Apply" @click="emit('apply')">
            <NgbIcon name="file-apply" />
          </button>
        </div>
      </template>
    </NgbPageHeader>

    <div class="flex min-h-0 min-w-0 flex-1 flex-col gap-4 overflow-hidden p-6">
      <div
        v-if="error"
        class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100"
      >
        {{ error }}
      </div>

      <div
        v-if="!contextReady"
        class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-6 text-sm text-ngb-muted shadow-card"
      >
        {{ emptyStateMessage }}
      </div>

      <template v-else>
        <div class="flex flex-wrap items-center gap-2">
          <NgbBadge v-for="badge in contextBadges" :key="badge" tone="neutral">{{ badge }}</NgbBadge>
          <NgbBadge v-if="focusedContextBadge" tone="neutral">{{ focusedContextBadge }}</NgbBadge>

          <div class="grid w-full grid-cols-1 gap-2 sm:ml-auto sm:w-auto sm:grid-cols-2" data-testid="open-items-summary">
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 py-2 text-sm shadow-card">
              <div class="text-xs text-ngb-muted">Outstanding</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(summary.totalOutstanding) }}</div>
            </div>
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 py-2 text-sm shadow-card">
              <div class="text-xs text-ngb-muted">Credit</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(summary.totalCredit) }}</div>
            </div>
          </div>
        </div>

        <div
          v-if="pageResult?.visible"
          data-testid="open-items-page-result"
          class="rounded-[var(--ngb-radius)] border border-green-200 bg-green-50 p-4 dark:border-green-900/50 dark:bg-green-950/20"
        >
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div class="min-w-0">
              <div class="text-sm font-semibold text-green-900 dark:text-green-100">{{ pageResult.title }}</div>
              <div class="mt-1 text-sm text-green-900/85 dark:text-green-100/85">{{ pageResult.subtitle }}</div>
              <div class="mt-2 text-xs text-green-900/80 dark:text-green-100/80">
                Open items were refreshed and the screen switched to Applied.
              </div>
            </div>

            <div class="flex items-center gap-2 shrink-0">
              <NgbButton size="sm" variant="secondary" @click="emit('dismissPageResult')">Dismiss</NgbButton>
            </div>
          </div>

          <div class="mt-3 grid grid-cols-2 gap-2">
            <div class="rounded-[var(--ngb-radius)] border border-green-200/70 bg-white/70 px-3 py-2 text-sm dark:bg-black/10">
              <div class="text-xs text-ngb-muted">Outstanding now</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(pageResult.outstandingNow) }}</div>
            </div>
            <div class="rounded-[var(--ngb-radius)] border border-green-200/70 bg-white/70 px-3 py-2 text-sm dark:bg-black/10">
              <div class="text-xs text-ngb-muted">Credit now</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(pageResult.creditNow) }}</div>
            </div>
          </div>

          <div
            v-if="pageResult.inconsistent"
            class="mt-3 rounded-[var(--ngb-radius)] border border-yellow-200 bg-yellow-50 p-3 text-sm text-yellow-900 dark:border-yellow-900/50 dark:bg-yellow-950/25 dark:text-yellow-100"
          >
            Apply returned success, but the refreshed Applied tab did not return active allocations yet. Use Refresh or verify the database rows directly.
          </div>

          <div class="mt-3 space-y-2">
            <div
              v-for="line in pageResult.lines"
              :key="line.key"
              class="rounded-[var(--ngb-radius)] border border-green-200/70 bg-white/80 px-3 py-3 dark:bg-black/10"
            >
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0">
                  <div class="text-sm font-medium text-ngb-text break-words">{{ line.creditLabel }} → {{ line.chargeLabel }}</div>
                  <div class="mt-1 text-xs text-ngb-muted">Applied {{ fmtDateOnly(line.appliedOnUtc) }} · {{ fmtMoney(line.amount) }}</div>
                </div>
                <div class="flex items-center gap-2 shrink-0">
                  <NgbButton size="sm" variant="secondary" @click="void openApplyDocument(line.applyId)">Open Apply</NgbButton>
                </div>
              </div>
            </div>
          </div>
        </div>

        <NgbTabs
          :model-value="activeTab"
          :tabs="tabs"
          fill
          class="flex-1 min-h-0"
          @update:model-value="emit('update:activeTab', $event)"
        >
          <template #default="{ active }">
            <div class="flex h-full min-h-0 min-w-0 flex-col">
              <NgbRegisterGrid
                v-if="active === 'charges'"
                class="flex-1 min-h-0"
                fill-height
                :show-panel="false"
                :show-totals="false"
                :columns="chargeGrid.columns"
                :rows="chargeGrid.rows"
                :storage-key="chargeGrid.storageKey"
                activate-on-row-click
                @rowActivate="(id) => void chargeGrid.onActivate(String(id))"
              />

              <NgbRegisterGrid
                v-else-if="active === 'credits'"
                class="flex-1 min-h-0"
                fill-height
                :show-panel="false"
                :show-totals="false"
                :columns="creditGrid.columns"
                :rows="creditGrid.rows"
                :storage-key="creditGrid.storageKey"
                activate-on-row-click
                @rowActivate="(id) => void creditGrid.onActivate(String(id))"
              />

              <div
                v-else
                data-testid="open-items-applied-panel"
                class="flex flex-1 min-h-0 flex-col overflow-hidden rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card"
              >
                <div class="border-b border-ngb-border px-4 py-3 flex flex-wrap items-center gap-2">
                  <div>
                    <div class="text-sm font-semibold text-ngb-text">Applied</div>
                    <div class="text-xs text-ngb-muted">{{ appliedSubtitle }}</div>
                  </div>
                  <div class="ml-auto flex items-center gap-2">
                    <NgbBadge tone="neutral">Active only</NgbBadge>
                    <NgbBadge v-if="highlightedApplyIds.length > 0" tone="neutral">Recent: {{ highlightedApplyIds.length }}</NgbBadge>
                  </div>
                </div>

                <div v-if="appliedRows.length === 0" class="p-6 text-sm text-ngb-muted">
                  {{ appliedEmptyMessage }}
                </div>

                <div v-else class="flex-1 min-h-0 overflow-auto">
                  <div class="min-w-[980px]">
                    <div class="grid grid-cols-[120px_1.3fr_1.3fr_120px_120px_220px] bg-[var(--ngb-grid-header)] border-b border-ngb-border text-sm font-semibold text-ngb-text">
                      <div class="px-3 py-2">Applied On</div>
                      <div class="px-3 py-2 border-l border-ngb-border">Credit Source</div>
                      <div class="px-3 py-2 border-l border-ngb-border">Charge</div>
                      <div class="px-3 py-2 border-l border-ngb-border text-right">Amount</div>
                      <div class="px-3 py-2 border-l border-ngb-border">Apply</div>
                      <div class="px-3 py-2 border-l border-ngb-border">Actions</div>
                    </div>

                    <div
                      v-for="allocation in appliedRows"
                      :key="allocation.applyId"
                      class="grid grid-cols-[120px_1.3fr_1.3fr_120px_120px_220px] border-b border-ngb-border text-sm"
                      :class="allocationTone(allocation)"
                    >
                      <div class="px-3 py-3">{{ fmtDateOnly(allocation.appliedOnUtc) }}</div>
                      <div class="px-3 py-3 border-l border-ngb-border min-w-0">
                        <button class="text-left w-full ngb-focus rounded-[var(--ngb-radius)]" @click="void openAppliedDocument(allocation.creditDocumentType, allocation.creditDocumentId)">
                          <div class="font-medium text-ngb-text truncate">{{ docLabel(allocation.creditDocumentNumber, allocation.creditDocumentDisplay, allocation.creditDocumentId) }}</div>
                          <div class="mt-1 text-xs text-ngb-muted truncate">{{ resolveCreditTypeLabel(allocation.creditDocumentType) }}</div>
                        </button>
                      </div>
                      <div class="px-3 py-3 border-l border-ngb-border min-w-0">
                        <button class="text-left w-full ngb-focus rounded-[var(--ngb-radius)]" @click="void openAppliedDocument(allocation.chargeDocumentType, allocation.chargeDocumentId)">
                          <div class="font-medium text-ngb-text truncate">{{ docLabel(allocation.chargeNumber, allocation.chargeDisplay, allocation.chargeDocumentId) }}</div>
                          <div class="mt-1 text-xs text-ngb-muted truncate">{{ resolveChargeTypeLabel(allocation.chargeDocumentType) }}</div>
                        </button>
                      </div>
                      <div class="px-3 py-3 border-l border-ngb-border text-right font-medium tabular-nums">{{ fmtMoney(allocation.amount) }}</div>
                      <div class="px-3 py-3 border-l border-ngb-border min-w-0">
                        <button class="text-left w-full ngb-focus rounded-[var(--ngb-radius)]" @click="void openApplyDocument(allocation.applyId)">
                          <div class="font-medium text-ngb-text truncate">{{ applyDocumentLabel(allocation.applyNumber, allocation.applyDisplay, allocation.applyId) }}</div>
                          <div class="mt-1 text-xs text-ngb-muted truncate">Apply document</div>
                        </button>
                      </div>
                      <div class="px-3 py-3 border-l border-ngb-border">
                        <div class="flex items-center justify-end gap-2">
                          <button class="ngb-iconbtn" title="Open Apply" @click="void openApplyDocument(allocation.applyId)">
                            <NgbIcon name="open-in-new" />
                          </button>
                          <button class="ngb-iconbtn" title="Unapply" @click="requestUnapply(buildUnapplyLine(allocation))">
                            <NgbIcon name="undo" />
                          </button>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </template>
        </NgbTabs>
      </template>
    </div>
  </div>
</template>
