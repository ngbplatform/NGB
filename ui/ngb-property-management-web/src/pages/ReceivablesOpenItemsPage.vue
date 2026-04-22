<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import {
  NgbBadge,
  NgbButton,
  NgbIcon,
  NgbRegisterGrid,
  buildLookupFieldTargetUrl,
  getDocumentById,
  getDocumentPage,
  omitRouteQueryKeys,
  useAllowedQueryValue,
  useBooleanQueryFlag,
  useGuidQueryParam,
  useRouteLookupSelection,
  useToasts,
} from 'ngb-ui-framework'

import { applyReceivablesBatch, getReceivablesOpenItemsDetails, suggestLeaseFifoApply, unapplyReceivablesApply } from '../api/clients/receivables'
import type {
  ReceivablesApplyBatchResponseDto,
  ReceivablesOpenItemsDetailsResponseDto,
  ReceivablesSuggestFifoApplyResponseDto,
} from '../api/types/pmContracts'
import {
  formatOpenItemsDateCell,
  formatOpenItemsMoneyCell,
  useOpenItemsPagePresentation,
} from '../features/open-items/pagePresentation'
import { buildOpenItemsTabs, type OpenItemsTabKey } from '../features/open-items/presentation'
import OpenItemsWorkflowShell from '../features/open-items/OpenItemsWorkflowShell.vue'
import { useOpenItemsRouteContext } from '../features/open-items/useOpenItemsRouteContext'
import {
  docLabel,
  fmtMoney,
  fmtDateOnly,
  formatApplyCount,
  type OpenItemsLookupItem,
} from '../features/open-items/shared'
import { useOpenItemsWorkflow } from '../features/open-items/workflow'

const route = useRoute()
const router = useRouter()
const toasts = useToasts()

const loading = ref(false)
const error = ref<string | null>(null)

const data = ref<ReceivablesOpenItemsDetailsResponseDto | null>(null)
const activeTab = ref<OpenItemsTabKey>('charges')
const wizardSelectedKeys = ref<string[]>([])

const RECEIVABLE_CHARGE_SOURCE_TYPES = [
  'pm.receivable_charge',
  'pm.rent_charge',
  'pm.late_fee_charge',
] as const

const RECEIVABLE_CREDIT_SOURCE_TYPES = [
  'pm.receivable_payment',
  'pm.receivable_credit_memo',
] as const

const RECEIVABLE_SOURCE_TYPES = [
  ...RECEIVABLE_CHARGE_SOURCE_TYPES,
  ...RECEIVABLE_CREDIT_SOURCE_TYPES,
] as const

const receivableChargeSourceTypeSet = new Set<string>(RECEIVABLE_CHARGE_SOURCE_TYPES)
const receivableCreditSourceTypeSet = new Set<string>(RECEIVABLE_CREDIT_SOURCE_TYPES)

const focusItemIdFromRoute = useGuidQueryParam(route, 'focusItemId')
const openApplyFromRoute = useBooleanQueryFlag(route, 'openApply')
const sourceDocumentTypeFromRoute = useAllowedQueryValue(route, 'source', RECEIVABLE_SOURCE_TYPES)

const {
  selected: lease,
  items: leaseItems,
  routeId: leaseIdFromRoute,
  hydrateSelected: hydrateLeaseFromRoute,
  onQuery: onLeaseQuery,
  onSelect: onLeaseSelect,
  openSelected: openLease,
} = useRouteLookupSelection({
  route,
  router,
  queryKey: 'leaseId',
  lookupById: async (leaseId) => (await getDocumentById('pm.lease', leaseId)).display ?? leaseId,
  search: async (query) => {
    const response = await getDocumentPage('pm.lease', { search: query, offset: 0, limit: 20 })
    return (response.items ?? []).map((item) => ({ id: item.id, label: item.display ?? item.id }))
  },
  openTarget: async (value) => buildLookupFieldTargetUrl({
    hint: { kind: 'document', documentTypes: ['pm.lease'] },
    value,
    route,
  }),
})

function clearAutoOpenApplyInRoute() {
  omitRouteQueryKeys(route, router, ['openApply', 'source'])
}

const hasLeaseSelected = computed(() => !!leaseIdFromRoute.value)
const lookupControls = computed(() => [
  {
    key: 'lease',
    value: lease.value,
    items: leaseItems.value,
    placeholder: 'Select lease…',
    widthClass: 'w-full sm:w-[360px]',
    onQuery: onLeaseQuery,
    onSelect: onLeaseSelect,
    onOpen: openLease,
  },
])

const partyDisplay = computed(() => data.value?.partyDisplay ?? '—')
const propertyDisplay = computed(() => data.value?.propertyDisplay ?? '—')
const contextBadges = computed(() => {
  const badges = [
    `Tenant: ${partyDisplay.value}`,
    `Property: ${propertyDisplay.value}`,
  ]
  if (data.value?.leaseDisplay) badges.push(`Lease: ${data.value.leaseDisplay}`)
  return badges
})

const {
  summary,
  focusedCharge,
  focusedCredit,
  focusedContextBadge,
  preferredTabFromRoute,
} = useOpenItemsPagePresentation({
  data,
  focusItemId: focusItemIdFromRoute,
  sourceDocumentType: sourceDocumentTypeFromRoute,
  resolveTabFromSourceType: (sourceDocumentType) => {
    if (sourceDocumentType && receivableCreditSourceTypeSet.has(sourceDocumentType)) return 'credits'
    if (sourceDocumentType && receivableChargeSourceTypeSet.has(sourceDocumentType)) return 'charges'
    return null
  },
  buildFocusedChargeBadge: (item) =>
    `Context: Charge ${docLabel(item.number, item.chargeDisplay, item.chargeDocumentId)}`,
  buildFocusedCreditBadge: (item) =>
    `Context: ${creditDocumentTypeLabel(item.documentType)} ${docLabel(item.number, item.creditDocumentDisplay, item.creditDocumentId)}`,
})

const formatDateCell = formatOpenItemsDateCell
const formatMoneyCell = formatOpenItemsMoneyCell

const chargeColumns = computed(() => [
  { key: 'doc', title: 'Charge', width: 160, pinned: 'left' as const },
  { key: 'dueOnUtc', title: 'Due', width: 120, format: formatDateCell },
  { key: 'chargeType', title: 'Type', width: 180 },
  { key: 'memo', title: 'Memo' },
  { key: 'originalAmount', title: 'Original', width: 130, align: 'right' as const, format: formatMoneyCell },
  { key: 'outstandingAmount', title: 'Outstanding', width: 140, align: 'right' as const, format: formatMoneyCell },
])

const chargeRows = computed(() => {
  const items = data.value?.charges ?? []
  return items.map((item) => ({
    key: item.chargeDocumentId,
    __status: 'posted',
    doc: docLabel(item.number, item.chargeDisplay, item.chargeDocumentId),
    dueOnUtc: item.dueOnUtc,
    chargeType: item.chargeTypeDisplay ?? '—',
    memo: item.memo ?? '',
    originalAmount: item.originalAmount,
    outstandingAmount: item.outstandingAmount,
  }))
})

function resolveChargeDocumentType(chargeDocumentId: string): string {
  const row = (data.value?.charges ?? []).find((item) => item.chargeDocumentId === chargeDocumentId)
  return row?.documentType || 'pm.receivable_charge'
}

function chargeDocumentTypeLabel(documentType: string): string {
  if (documentType === 'pm.rent_charge') return 'Rent'
  return documentType === 'pm.late_fee_charge' ? 'Late Fee' : 'Charge'
}

const chargeGrid = computed(() => ({
  columns: chargeColumns.value,
  rows: chargeRows.value,
  storageKey: 'pm:receivables:open-items:charges',
  onActivate: (id: string) => openDocument(resolveChargeDocumentType(id), id),
}))

const creditColumns = computed(() => [
  { key: 'doc', title: 'Credit Source', width: 170, pinned: 'left' as const },
  { key: 'creditType', title: 'Type', width: 140 },
  { key: 'receivedOnUtc', title: 'Credit Date', width: 120, format: formatDateCell },
  { key: 'memo', title: 'Memo' },
  { key: 'originalAmount', title: 'Original', width: 130, align: 'right' as const, format: formatMoneyCell },
  { key: 'availableCredit', title: 'Available', width: 140, align: 'right' as const, format: formatMoneyCell },
])

const creditRows = computed(() => {
  const items = data.value?.credits ?? []
  return items.map((item) => ({
    key: item.creditDocumentId,
    __status: 'posted',
    doc: docLabel(item.number, item.creditDocumentDisplay, item.creditDocumentId),
    creditType: creditDocumentTypeLabel(item.documentType),
    receivedOnUtc: item.receivedOnUtc,
    memo: item.memo ?? '',
    originalAmount: item.originalAmount,
    availableCredit: item.availableCredit,
  }))
})

function creditDocumentTypeLabel(documentType: string | null | undefined): string {
  if (documentType === 'pm.receivable_credit_memo') return 'Credit Memo'
  return documentType === 'pm.receivable_payment' ? 'Payment' : 'Credit Source'
}

function resolveCreditDocumentType(creditDocumentId: string): string {
  const allocation = (data.value?.allocations ?? []).find((item) => item.creditDocumentId === creditDocumentId)
  if (allocation?.creditDocumentType) return allocation.creditDocumentType
  const row = (data.value?.credits ?? []).find((item) => item.creditDocumentId === creditDocumentId)
  return row?.documentType || 'pm.receivable_payment'
}

const creditGrid = computed(() => ({
  columns: creditColumns.value,
  rows: creditRows.value,
  storageKey: 'pm:receivables:open-items:credits',
  onActivate: (id: string) => openDocument(resolveCreditDocumentType(id), id),
}))

function openDocument(documentType: string, id: string): Promise<void> {
  return router.push(`/documents/${documentType}/${id}`)
}

async function load(): Promise<void> {
  const leaseId = leaseIdFromRoute.value
  if (!leaseId) {
    data.value = null
    error.value = null
    return
  }

  loading.value = true
  error.value = null
  try {
    data.value = await getReceivablesOpenItemsDetails({ leaseId })
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : String(cause)
    data.value = null
  } finally {
    loading.value = false
  }
}

async function refresh() {
  await hydrateLeaseFromRoute()
  await load()
  if (!error.value) {
    toasts.push({
      title: 'Refreshed',
      message: 'Open items updated.',
      tone: 'success',
    })
  }
}

function allocationMatchesContext(allocation: ReceivablesOpenItemsDetailsResponseDto['allocations'][number]): boolean {
  if (focusedCharge.value?.chargeDocumentId && allocation.chargeDocumentId === focusedCharge.value.chargeDocumentId) return true
  if (focusedCredit.value?.creditDocumentId && allocation.creditDocumentId === focusedCredit.value.creditDocumentId) return true
  return false
}

const {
  applyWizardOpen,
  applyWizardView,
  suggestLoading,
  suggestError,
  suggestData,
  applyExecLoading,
  applyExecError,
  applyResult,
  unapplyLoading,
  unapplyError,
  unapplyConfirmOpen,
  pendingUnapplyLine,
  highlightedApplyIds,
  applyResultLines,
  pageResult,
  appliedAllocations,
  canExecuteApply,
  previewAfterOutstanding,
  previewAfterCredit,
  suggest,
  openApplyWizard,
  requestUnapply,
  onUnapplyConfirmOpenChanged,
  confirmUnapply,
  showApplyPlanAgain,
  executeApplyBatch,
  dismissPageApplyResult,
  showAppliedTab,
  syncPreferredTab,
  syncAfterContextLoad,
  handleWizardOpenChanged,
  applyResultActionLabel,
  applyResultTitle,
  applyResultSubtitle,
} = useOpenItemsWorkflow({
  contextReady: hasLeaseSelected,
  data,
  summary,
  activeTab,
  toasts,
  suggestFactory: async () => {
    const leaseId = leaseIdFromRoute.value
    if (!leaseId) throw new Error('Select a lease first.')
    return suggestLeaseFifoApply({
      leaseId,
      createDrafts: false,
      limit: 500,
    })
  },
  executeFactory: (suggestion) =>
    applyReceivablesBatch({
      applies: (suggestion.suggestedApplies ?? []).map((item) => ({
        applyId: item.applyId ?? null,
        applyPayload: item.applyPayload,
      })),
    }),
  unapplyFactory: (applyId) => unapplyReceivablesApply(applyId),
  load,
  resolveFallbackCreditDocumentType: resolveCreditDocumentType,
  allocationMatchesContext,
  buildUnapplySuccessMessage: (line) => `${line.creditLabel} was unapplied from ${line.chargeLabel} for ${fmtMoney(line.amount)}.`,
  buildExecuteSuccessMessage: (result) => `Open items refreshed. Total applied: ${fmtMoney(result.totalApplied ?? 0)}.`,
})

async function openApplyDocument(applyId: string): Promise<void> {
  applyWizardOpen.value = false
  await router.push(`/documents/pm.receivable_apply/${applyId}`)
}

function suggestRowKey(item: NonNullable<ReceivablesSuggestFifoApplyResponseDto['suggestedApplies']>[number], index: number): string {
  return item.applyId ?? `${item.creditDocumentId}:${item.chargeDocumentId}:${index}`
}

const suggestedApplyItems = computed(() => {
  return (suggestData.value?.suggestedApplies ?? []).map((item, index) => ({
    ...item,
    __key: suggestRowKey(item, index),
  }))
})

const applyWizardUniquePaymentCount = computed(() =>
  new Set(suggestedApplyItems.value.map((item) => item.creditDocumentId)).size,
)

const applyWizardShouldGroup = computed(() => {
  const total = suggestedApplyItems.value.length
  const paymentCount = applyWizardUniquePaymentCount.value
  return paymentCount > 1 && total > paymentCount
})

const applyWizardGroupBy = computed(() => (applyWizardShouldGroup.value ? ['creditSource'] : []))

const selectedSuggestedApplies = computed(() => {
  const items = suggestedApplyItems.value
  if (items.length === 0) return []

  const selected = new Set(wizardSelectedKeys.value)
  const matches = items.filter((item) => selected.has(item.__key))
  return matches.length > 0 ? matches : [items[0]]
})

const selectedSuggestedSummary = computed(() => {
  const items = selectedSuggestedApplies.value
  const first = items[0]
  return {
    count: items.length,
    paymentCount: new Set(items.map((item) => item.creditDocumentId)).size,
    chargeCount: new Set(items.map((item) => item.chargeDocumentId)).size,
    creditLabel: first ? docLabel(null, first.creditDocumentDisplay, first.creditDocumentId) : '—',
    chargeLabel: first ? docLabel(null, first.chargeDisplay, first.chargeDocumentId) : '—',
    chargeOutstandingBefore: items.reduce((sum, item) => sum + Number(item.chargeOutstandingBefore ?? 0), 0),
    chargeOutstandingAfter: items.reduce((sum, item) => sum + Number(item.chargeOutstandingAfter ?? 0), 0),
    creditAmountBefore: items.reduce((sum, item) => sum + Number(item.creditAmountBefore ?? 0), 0),
    creditAmountAfter: items.reduce((sum, item) => sum + Number(item.creditAmountAfter ?? 0), 0),
    applyAmount: items.reduce((sum, item) => sum + Number(item.amount ?? 0), 0),
  }
})

const wizardSelectionTitle = computed(() => {
  const selected = selectedSuggestedSummary.value
  if (selected.count <= 1) return `${selected.creditLabel} → ${selected.chargeLabel}`
  return `Selected ${selected.count} suggested applies`
})

const formattedSuggestWarnings = computed(() => {
  return (suggestData.value?.warnings ?? []).map((warning) => {
    switch (String(warning.code ?? '').trim()) {
      case 'no_charges':
        return { title: 'No open charges', message: 'There are no outstanding charges to apply right now for this lease.' }
      case 'no_credits':
        return { title: 'No available credits', message: 'There is no posted credit source available to apply right now for this lease.' }
      case 'limit_reached':
        return { title: 'Suggestion limit reached', message: 'The wizard stopped early because the current suggestion limit was reached. Review the remaining items before continuing.' }
      case 'outstanding_remaining':
        return { title: 'Some charges will remain open', message: String(warning.message ?? '').replace('Outstanding charges remain', 'Open charge balance will remain after this apply') }
      case 'credit_remaining':
        return { title: 'Some credit will remain', message: String(warning.message ?? '').replace('Unapplied credits remain', 'Available credit will remain after this apply') }
      default:
        return { title: 'Review before posting', message: warning.message }
    }
  })
})

const applyWizardColumns = computed(() => [
  { key: 'creditSource', title: 'Credit Source', width: 180, pinned: 'left' as const },
  { key: 'creditType', title: 'Type', width: 130 },
  { key: 'creditDocumentDateUtc', title: 'Credit Date', width: 120, format: formatDateCell },
  { key: 'charge', title: 'Charge', width: 170 },
  { key: 'chargeDueOnUtc', title: 'Due', width: 120, format: formatDateCell },
  { key: 'amount', title: 'Amount', width: 130, align: 'right' as const, format: formatMoneyCell },
])

const applyWizardRows = computed(() => {
  return suggestedApplyItems.value.map((item) => ({
    key: item.__key,
    __status: 'posted',
    creditSource: docLabel(null, item.creditDocumentDisplay, item.creditDocumentId),
    creditType: creditDocumentTypeLabel(item.creditDocumentType),
    creditDocumentDateUtc: item.creditDocumentDateUtc,
    charge: docLabel(null, item.chargeDisplay, item.chargeDocumentId),
    chargeDueOnUtc: item.chargeDueOnUtc,
    amount: item.amount,
  }))
})

useOpenItemsRouteContext({
  source: () => [leaseIdFromRoute.value, openApplyFromRoute.value] as const,
  contextKeyCount: 1,
  hydrateContext: hydrateLeaseFromRoute,
  load,
  preferredTab: preferredTabFromRoute,
  currentError: error,
  syncAfterContextLoad,
  autoOpenApply: (current) => current[1],
  clearAutoOpenApplyInRoute: () => clearAutoOpenApplyInRoute(),
})

watch(
  () => applyWizardOpen.value,
  (value) => void handleWizardOpenChanged(value),
)

watch(
  () => [focusItemIdFromRoute.value, sourceDocumentTypeFromRoute.value, focusedCharge.value?.chargeDocumentId, focusedCredit.value?.creditDocumentId],
  () => {
    syncPreferredTab(preferredTabFromRoute.value)
  },
  { immediate: true },
)

watch(
  () => suggestedApplyItems.value.map((item) => item.__key),
  (keys) => {
    const valid = new Set(keys)
    wizardSelectedKeys.value = wizardSelectedKeys.value.filter((key) => valid.has(key))
    if (wizardSelectedKeys.value.length === 0 && keys.length > 0) wizardSelectedKeys.value = [keys[0]]
  },
  { immediate: true },
)

const tabs = computed(() => buildOpenItemsTabs(summary.value))
</script>

<template>
  <OpenItemsWorkflowShell
    title="Receivables"
    :lookups="lookupControls"
    :loading="loading"
    :error="error"
    :context-ready="hasLeaseSelected"
    empty-state-message="Select a lease to view open receivables items (charges and available credits)."
    :context-badges="contextBadges"
    :focused-context-badge="focusedContextBadge"
    :summary="summary"
    :page-result="pageResult"
    :tabs="tabs"
    :active-tab="activeTab"
    :charge-grid="chargeGrid"
    :credit-grid="creditGrid"
    :applied-rows="appliedAllocations"
    applied-subtitle="Current active allocations for this lease. Reversed applies are hidden."
    applied-empty-message="No applied allocations yet for this lease. Once a credit source is applied to a charge, it will appear here."
    :highlighted-apply-ids="highlightedApplyIds"
    :is-context-allocation="allocationMatchesContext"
    :resolve-charge-type-label="chargeDocumentTypeLabel"
    :resolve-credit-type-label="creditDocumentTypeLabel"
    :open-applied-document="openDocument"
    :open-apply-document="openApplyDocument"
    :request-unapply="requestUnapply"
    :can-refresh="hasLeaseSelected"
    :can-apply="hasLeaseSelected"
    :apply-wizard-open="applyWizardOpen"
    :apply-wizard-subtitle="applyWizardView === 'result' ? 'Applied allocations and refreshed open items' : 'Suggest FIFO allocations and post them'"
    :apply-wizard-action-disabled="suggestLoading || applyExecLoading || !hasLeaseSelected"
    :apply-wizard-action-title="applyWizardView === 'result' ? applyResultActionLabel : 'Resuggest'"
    empty-wizard-message="Select a lease first."
    :unapply-confirm-open="unapplyConfirmOpen"
    unapply-title="Unapply this allocation?"
    :unapply-message="pendingUnapplyLine ? `Unapply ${creditDocumentTypeLabel(pendingUnapplyLine.creditDocumentType).toLowerCase()} ${pendingUnapplyLine.creditLabel} from charge ${pendingUnapplyLine.chargeLabel} for ${fmtMoney(pendingUnapplyLine.amount)}?` : 'Unapply this allocation?'"
    unapply-confirm-text="Unapply"
    unapply-cancel-text="Cancel"
    @back="router.back()"
    @refresh="refresh"
    @apply="openApplyWizard"
    @dismissPageResult="dismissPageApplyResult"
    @update:activeTab="activeTab = $event"
    @update:applyWizardOpen="applyWizardOpen = $event"
    @applyWizardAction="applyWizardView === 'result' ? showApplyPlanAgain() : suggest()"
    @update:unapplyConfirmOpen="onUnapplyConfirmOpenChanged"
    @confirmUnapply="confirmUnapply"
  >
    <template #drawer>
      <div
        v-if="suggestError && applyWizardView === 'suggest'"
        class="mb-3 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 dark:border-red-900/50 dark:bg-red-950/30 p-3 text-sm text-red-900 dark:text-red-100"
      >
        {{ suggestError }}
      </div>

      <div
        v-if="applyExecError"
        class="mb-3 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 dark:border-red-900/50 dark:bg-red-950/30 p-3 text-sm text-red-900 dark:text-red-100"
      >
        {{ applyExecError }}
      </div>

      <div
        v-if="unapplyError && applyWizardView === 'result'"
        class="mb-3 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 dark:border-red-900/50 dark:bg-red-950/30 p-3 text-sm text-red-900 dark:text-red-100"
      >
        {{ unapplyError }}
      </div>

      <template v-if="applyWizardView === 'result' && applyResult">
        <div class="space-y-4">
          <div class="flex flex-wrap items-center gap-2">
            <NgbBadge tone="neutral">Tenant: {{ partyDisplay }}</NgbBadge>
            <NgbBadge tone="neutral">Property: {{ propertyDisplay }}</NgbBadge>
            <NgbBadge tone="neutral" v-if="data?.leaseDisplay">Lease: {{ data.leaseDisplay }}</NgbBadge>
          </div>

          <div class="rounded-[var(--ngb-radius)] border border-green-200 bg-green-50 dark:border-green-900/50 dark:bg-green-950/20 p-4">
            <div class="text-sm font-semibold text-green-900 dark:text-green-100">{{ applyResultTitle }}</div>
            <div class="mt-1 text-sm text-green-900/85 dark:text-green-100/85">{{ applyResultSubtitle }}</div>
          </div>

          <div class="grid grid-cols-2 gap-2">
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">Created applies</div>
              <div class="font-semibold tabular-nums">{{ formatApplyCount(applyResult.executedApplies?.length ?? 0) }}</div>
            </div>
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">Applied total</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(applyResult.totalApplied ?? 0) }}</div>
            </div>
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">Outstanding now</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(summary.totalOutstanding) }}</div>
            </div>
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">Credit now</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(summary.totalCredit) }}</div>
            </div>
          </div>

          <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card p-3">
            <div class="flex items-center justify-between gap-2">
              <div>
                <div class="text-sm font-semibold text-ngb-text">Applied</div>
                <div class="text-xs text-ngb-muted">Only the applies created by this action are shown here.</div>
              </div>
            </div>

            <div class="mt-3 space-y-2">
              <div
                v-for="line in applyResultLines"
                :key="line.key"
                class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-3 py-3"
              >
                <div class="flex items-start justify-between gap-3">
                  <div class="min-w-0">
                    <div class="text-sm font-medium text-ngb-text break-words">
                      {{ line.creditLabel }} → {{ line.chargeLabel }}
                    </div>
                    <div class="mt-1 text-xs text-ngb-muted">
                      Applied {{ fmtDateOnly(line.appliedOnUtc) }} · {{ fmtMoney(line.amount) }}
                    </div>
                  </div>

                  <div class="flex items-center gap-2 shrink-0">
                    <button
                      class="ngb-iconbtn"
                      :disabled="unapplyLoading"
                      title="Unapply"
                      @click="requestUnapply(line)"
                    >
                      <NgbIcon name="undo" />
                    </button>
                    <NgbButton size="sm" variant="secondary" :disabled="unapplyLoading" @click="openApplyDocument(line.applyId)">Open Apply</NgbButton>
                  </div>
                </div>
              </div>
            </div>

            <div v-if="applyResultLines.length === 0" class="mt-3 text-sm text-ngb-muted">
              There are no active applied allocations left in this result set. They may already be fully unapplied.
            </div>
          </div>
        </div>
      </template>

      <template v-else>
        <div v-if="suggestLoading" class="text-sm text-ngb-muted">Suggesting FIFO allocations…</div>

        <template v-else>
          <div class="flex flex-wrap items-center gap-2 mb-3">
            <NgbBadge tone="neutral">Tenant: {{ suggestData?.partyDisplay ?? partyDisplay }}</NgbBadge>
            <NgbBadge tone="neutral">Property: {{ suggestData?.propertyDisplay ?? propertyDisplay }}</NgbBadge>
            <NgbBadge tone="neutral" v-if="suggestData?.leaseDisplay || data?.leaseDisplay">Lease: {{ suggestData?.leaseDisplay ?? data?.leaseDisplay }}</NgbBadge>
          </div>

          <div class="mb-4 rounded-[var(--ngb-radius)] border border-blue-200 bg-blue-50 dark:border-blue-900/50 dark:bg-blue-950/20 p-3">
            <div class="text-sm font-semibold text-blue-900 dark:text-blue-100">Preview only</div>
            <div class="mt-1 text-sm text-blue-900/85 dark:text-blue-100/85">
              Nothing has been applied yet. These numbers show what the open-items state will look like after you confirm.
            </div>
          </div>

          <div class="grid grid-cols-2 gap-2 mb-4">
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">Current outstanding</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(summary.totalOutstanding) }}</div>
            </div>
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">Current credit</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(summary.totalCredit) }}</div>
            </div>
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">After apply outstanding</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(previewAfterOutstanding) }}</div>
            </div>
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">After apply credit</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(previewAfterCredit) }}</div>
            </div>
          </div>

          <div class="mb-4 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
            <div class="text-xs text-ngb-muted">Will create</div>
            <div class="font-semibold">{{ formatApplyCount(suggestData?.suggestedApplies?.length ?? 0) }} totaling {{ fmtMoney(suggestData?.totalApplied ?? 0) }}</div>
          </div>

          <div
            v-if="selectedSuggestedApplies.length > 0"
            class="mb-4 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card p-3"
          >
            <div class="flex flex-wrap items-start justify-between gap-3">
              <div class="min-w-0">
                <div class="text-sm font-semibold text-ngb-text">Selection preview</div>
                <div class="mt-1 text-sm text-ngb-text break-words">{{ wizardSelectionTitle }}</div>
                <div class="mt-1 text-xs text-ngb-muted">
                  {{ selectedSuggestedSummary.count }} line<span v-if="selectedSuggestedSummary.count !== 1">s</span> ·
                  {{ selectedSuggestedSummary.paymentCount }} credit source<span v-if="selectedSuggestedSummary.paymentCount !== 1">s</span> ·
                  {{ selectedSuggestedSummary.chargeCount }} charge<span v-if="selectedSuggestedSummary.chargeCount !== 1">s</span>
                </div>
              </div>
              <NgbBadge tone="neutral">Will post</NgbBadge>
            </div>

            <div class="mt-3 grid grid-cols-3 gap-2">
              <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-3 py-2 text-sm">
                <div class="text-xs text-ngb-muted">Charge outstanding before</div>
                <div class="font-semibold tabular-nums">{{ fmtMoney(selectedSuggestedSummary.chargeOutstandingBefore) }}</div>
              </div>
              <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-3 py-2 text-sm">
                <div class="text-xs text-ngb-muted">Apply amount</div>
                <div class="font-semibold tabular-nums">{{ fmtMoney(selectedSuggestedSummary.applyAmount) }}</div>
              </div>
              <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-3 py-2 text-sm">
                <div class="text-xs text-ngb-muted">Charge outstanding after</div>
                <div class="font-semibold tabular-nums">{{ fmtMoney(selectedSuggestedSummary.chargeOutstandingAfter) }}</div>
              </div>
            </div>

            <div class="mt-2 grid grid-cols-2 gap-2">
              <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-3 py-2 text-sm">
                <div class="text-xs text-ngb-muted">Credit before</div>
                <div class="font-semibold tabular-nums">{{ fmtMoney(selectedSuggestedSummary.creditAmountBefore) }}</div>
              </div>
              <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-3 py-2 text-sm">
                <div class="text-xs text-ngb-muted">Credit after</div>
                <div class="font-semibold tabular-nums">{{ fmtMoney(selectedSuggestedSummary.creditAmountAfter) }}</div>
              </div>
            </div>
          </div>

          <div
            v-if="formattedSuggestWarnings.length > 0"
            class="mb-4 rounded-[var(--ngb-radius)] border border-yellow-200 bg-yellow-50 dark:border-yellow-900/50 dark:bg-yellow-950/25 p-3"
          >
            <div class="text-sm font-semibold text-yellow-900 dark:text-yellow-100">What still needs attention</div>
            <div class="mt-2 space-y-2">
              <div
                v-for="(w, idx) in formattedSuggestWarnings"
                :key="idx"
                class="rounded-[var(--ngb-radius)] border border-yellow-200/70 bg-white/70 dark:bg-black/10 px-3 py-2"
              >
                <div class="text-sm font-medium text-yellow-900 dark:text-yellow-100">{{ w.title }}</div>
                <div class="mt-1 text-sm text-yellow-900/90 dark:text-yellow-100/90">{{ w.message }}</div>
              </div>
            </div>
          </div>

          <div class="h-[420px]">
            <NgbRegisterGrid
              v-model:selectedKeys="wizardSelectedKeys"
              class="h-full"
              fill-height
              :show-panel="false"
              :show-totals="false"
              :columns="applyWizardColumns"
              :rows="applyWizardRows"
              :group-by="applyWizardGroupBy"
              :default-expanded="false"
              storage-key="pm:receivables:apply-wizard"
            />
          </div>

          <div v-if="(suggestData?.suggestedApplies?.length ?? 0) === 0" class="mt-3 text-sm text-ngb-muted">
            Nothing to apply. There are no matching credit sources and outstanding charges.
          </div>
        </template>
      </template>

    </template>

    <template #footer>
      <div class="flex items-center justify-end gap-2">
        <template v-if="applyWizardView === 'result' && applyResult">
          <NgbButton variant="secondary" :disabled="applyExecLoading || suggestLoading || unapplyLoading" @click="applyWizardOpen = false">Close</NgbButton>
          <NgbButton variant="secondary" :disabled="applyExecLoading || suggestLoading || unapplyLoading" @click="showAppliedTab">Show Applied</NgbButton>
          <NgbButton variant="primary" :disabled="applyExecLoading || suggestLoading || unapplyLoading || !hasLeaseSelected" @click="showApplyPlanAgain">
            {{ applyResultActionLabel }}
          </NgbButton>
        </template>

        <template v-else>
          <NgbButton variant="secondary" :disabled="applyExecLoading" @click="applyWizardOpen = false">Close</NgbButton>
          <NgbButton variant="primary" :disabled="!canExecuteApply" :loading="applyExecLoading" @click="executeApplyBatch">
            Confirm & Apply
          </NgbButton>
        </template>
      </div>
    </template>

    <template #unapply />
  </OpenItemsWorkflowShell>
</template>
