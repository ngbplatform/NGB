<script setup lang="ts">
import { NgbConfirmDialog, NgbDrawer, NgbIcon } from 'ngb-ui-framework'

import OpenItemsPageLayout from './OpenItemsPageLayout.vue'
import type { OpenItemsAppliedAllocationView, OpenItemsGridDefinition, OpenItemsPageResultView, OpenItemsTabKey } from './presentation'
import type { OpenItemsApplyResultLine, OpenItemsLookupItem } from './shared'

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

const props = withDefaults(defineProps<{
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
  applyWizardOpen: boolean
  applyWizardSubtitle: string
  applyWizardActionDisabled: boolean
  applyWizardActionTitle: string
  emptyWizardMessage: string
  unapplyConfirmOpen: boolean
  unapplyTitle: string
  unapplyMessage: string
  unapplyConfirmText: string
  unapplyCancelText: string
  unapplyDanger?: boolean
  unapplyConfirmLoading?: boolean
}>(), {
  error: null,
  focusedContextBadge: null,
  pageResult: null,
  isContextAllocation: undefined,
  unapplyDanger: false,
  unapplyConfirmLoading: false,
})

const emit = defineEmits<{
  (e: 'back'): void
  (e: 'refresh'): void
  (e: 'apply'): void
  (e: 'dismissPageResult'): void
  (e: 'update:activeTab', value: OpenItemsTabKey): void
  (e: 'update:applyWizardOpen', value: boolean): void
  (e: 'applyWizardAction'): void
  (e: 'update:unapplyConfirmOpen', value: boolean): void
  (e: 'confirmUnapply'): void
}>()
</script>

<template>
  <OpenItemsPageLayout
    :title="title"
    :lookups="lookups"
    :loading="loading"
    :error="error"
    :context-ready="contextReady"
    :empty-state-message="emptyStateMessage"
    :context-badges="contextBadges"
    :focused-context-badge="focusedContextBadge"
    :summary="summary"
    :page-result="pageResult"
    :tabs="tabs"
    :active-tab="activeTab"
    :charge-grid="chargeGrid"
    :credit-grid="creditGrid"
    :applied-rows="appliedRows"
    :applied-subtitle="appliedSubtitle"
    :applied-empty-message="appliedEmptyMessage"
    :highlighted-apply-ids="highlightedApplyIds"
    :is-context-allocation="isContextAllocation"
    :resolve-charge-type-label="resolveChargeTypeLabel"
    :resolve-credit-type-label="resolveCreditTypeLabel"
    :open-applied-document="openAppliedDocument"
    :open-apply-document="openApplyDocument"
    :request-unapply="requestUnapply"
    :can-refresh="canRefresh"
    :can-apply="canApply"
    @back="emit('back')"
    @refresh="emit('refresh')"
    @apply="emit('apply')"
    @dismissPageResult="emit('dismissPageResult')"
    @update:activeTab="emit('update:activeTab', $event)"
  />

  <NgbDrawer
    :open="applyWizardOpen"
    title="Apply Wizard"
    :subtitle="applyWizardSubtitle"
    @update:open="emit('update:applyWizardOpen', $event)"
  >
    <template #actions>
      <button
        class="ngb-iconbtn"
        :disabled="applyWizardActionDisabled"
        :title="applyWizardActionTitle"
        @click="emit('applyWizardAction')"
      >
        <NgbIcon name="refresh" />
      </button>
    </template>

    <div v-if="!contextReady" class="text-sm text-ngb-muted">{{ emptyWizardMessage }}</div>

    <template v-else>
      <slot name="drawer" />
    </template>

    <template #footer>
      <slot name="footer" />
    </template>
  </NgbDrawer>

  <NgbConfirmDialog
    :open="unapplyConfirmOpen"
    :title="unapplyTitle"
    :message="unapplyMessage"
    :confirm-text="unapplyConfirmText"
    :cancel-text="unapplyCancelText"
    :danger="unapplyDanger"
    :confirm-loading="unapplyConfirmLoading"
    @update:open="emit('update:unapplyConfirmOpen', $event)"
    @confirm="emit('confirmUnapply')"
  >
    <slot name="unapply" />
  </NgbConfirmDialog>
</template>
