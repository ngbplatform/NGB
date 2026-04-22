<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import {
  NgbBadge,
  NgbButton,
  NgbRegisterGrid,
  buildLookupFieldTargetUrl,
  getCatalogById,
  getCatalogPage,
  omitRouteQueryKeys,
  useAllowedQueryValue,
  useBooleanQueryFlag,
  useGuidQueryParam,
  useRouteLookupSelection,
  useToasts,
} from 'ngb-ui-framework'

import {
  applyPayablesBatch,
  getPayablesOpenItemsDetails,
  suggestPayablesFifoApply,
  unapplyPayablesApply,
} from '../api/clients/payables'
import type {
  PayablesApplyBatchResponseDto,
  PayablesOpenItemsDetailsResponseDto,
  PayablesSuggestFifoApplyResponseDto,
} from '../api/types/pmContracts'
import {
  formatOpenItemsDateCell,
  formatOpenItemsMoneyCell,
  useOpenItemsPagePresentation,
} from '../features/open-items/pagePresentation'
import { buildOpenItemsTabs, type OpenItemsTabKey } from '../features/open-items/presentation'
import OpenItemsWorkflowShell from '../features/open-items/OpenItemsWorkflowShell.vue'
import { useOpenItemsNavigationRefresh } from '../features/open-items/useOpenItemsNavigationRefresh'
import { useOpenItemsRouteContext } from '../features/open-items/useOpenItemsRouteContext'
import { docLabel, fmtDateOnly, fmtMoney, formatApplyCount } from '../features/open-items/shared'
import { useOpenItemsWorkflow } from '../features/open-items/workflow'

const route = useRoute()
const router = useRouter()
const toasts = useToasts()

const loading = ref(false)
const error = ref<string | null>(null)

const data = ref<PayablesOpenItemsDetailsResponseDto | null>(null)
const activeTab = ref<OpenItemsTabKey>('charges')

const PAYABLE_CHARGE_SOURCE_TYPES = ['pm.payable_charge'] as const
const PAYABLE_CREDIT_SOURCE_TYPES = ['pm.payable_payment', 'pm.payable_credit_memo'] as const
const PAYABLE_SOURCE_TYPES = [...PAYABLE_CHARGE_SOURCE_TYPES, ...PAYABLE_CREDIT_SOURCE_TYPES] as const

const payableChargeSourceTypeSet = new Set<string>(PAYABLE_CHARGE_SOURCE_TYPES)
const payableCreditSourceTypeSet = new Set<string>(PAYABLE_CREDIT_SOURCE_TYPES)

const focusItemIdFromRoute = useGuidQueryParam(route, 'focusItemId')
const openApplyFromRoute = useBooleanQueryFlag(route, 'openApply')
const refreshFromRoute = useBooleanQueryFlag(route, 'refresh')
const sourceDocumentTypeFromRoute = useAllowedQueryValue(route, 'source', PAYABLE_SOURCE_TYPES)

const {
  selected: vendor,
  items: vendorItems,
  routeId: partyIdFromRoute,
  hydrateSelected: hydrateVendorFromRoute,
  onQuery: onVendorQuery,
  onSelect: onVendorSelect,
  openSelected: openVendor,
} = useRouteLookupSelection({
  route,
  router,
  queryKey: 'partyId',
  lookupById: async (partyId) => (await getCatalogById('pm.party', partyId)).display ?? partyId,
  search: async (query) => {
    const response = await getCatalogPage('pm.party', {
      offset: 0,
      limit: 20,
      search: query,
      filters: {
        deleted: 'active',
        is_vendor: 'true',
      },
    })
    return (response.items ?? []).map((item) => ({ id: item.id, label: item.display ?? item.id }))
  },
  openTarget: async (value) => buildLookupFieldTargetUrl({
    hint: { kind: 'catalog', catalogType: 'pm.party' },
    value,
    route,
  }),
})

const {
  selected: property,
  items: propertyItems,
  routeId: propertyIdFromRoute,
  hydrateSelected: hydratePropertyFromRoute,
  onQuery: onPropertyQuery,
  onSelect: onPropertySelect,
  openSelected: openProperty,
} = useRouteLookupSelection({
  route,
  router,
  queryKey: 'propertyId',
  lookupById: async (propertyId) => (await getCatalogById('pm.property', propertyId)).display ?? propertyId,
  search: async (query) => {
    const response = await getCatalogPage('pm.property', {
      offset: 0,
      limit: 20,
      search: query,
      filters: {
        deleted: 'active',
      },
    })
    return (response.items ?? []).map((item) => ({ id: item.id, label: item.display ?? item.id }))
  },
  openTarget: async (value) => buildLookupFieldTargetUrl({
    hint: { kind: 'catalog', catalogType: 'pm.property' },
    value,
    route,
  }),
})

const lookupControls = computed(() => [
  {
    key: 'vendor',
    value: vendor.value,
    items: vendorItems.value,
    placeholder: 'Select vendor…',
    widthClass: 'w-full sm:w-[300px]',
    onQuery: onVendorQuery,
    onSelect: onVendorSelect,
    onOpen: openVendor,
  },
  {
    key: 'property',
    value: property.value,
    items: propertyItems.value,
    placeholder: 'Select property…',
    widthClass: 'w-full sm:w-[300px]',
    onQuery: onPropertyQuery,
    onSelect: onPropertySelect,
    onOpen: openProperty,
  },
])

async function hydrateContextFromRoute(): Promise<void> {
  await Promise.all([hydrateVendorFromRoute(), hydratePropertyFromRoute()])
}

function clearAutoOpenApplyInRoute(): void {
  omitRouteQueryKeys(route, router, ['openApply', 'source'])
}

function clearRefreshFlagInRoute(): void {
  omitRouteQueryKeys(route, router, ['refresh'])
}

const hasContextSelected = computed(() => !!partyIdFromRoute.value && !!propertyIdFromRoute.value)

const vendorDisplay = computed(() => data.value?.vendorDisplay ?? '—')
const propertyDisplay = computed(() => data.value?.propertyDisplay ?? '—')
const contextBadges = computed(() => [
  `Vendor: ${vendorDisplay.value}`,
  `Property: ${propertyDisplay.value}`,
])

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
    if (sourceDocumentType && payableCreditSourceTypeSet.has(sourceDocumentType)) return 'credits'
    if (sourceDocumentType && payableChargeSourceTypeSet.has(sourceDocumentType)) return 'charges'
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
  { key: 'doc', title: 'Charge', width: 170, pinned: 'left' as const },
  { key: 'dueOnUtc', title: 'Due', width: 120, format: formatDateCell },
  { key: 'chargeType', title: 'Type', width: 180 },
  { key: 'vendorInvoiceNo', title: 'Invoice No', width: 150 },
  { key: 'memo', title: 'Memo' },
  { key: 'originalAmount', title: 'Original', width: 130, align: 'right' as const, format: formatMoneyCell },
  { key: 'outstandingAmount', title: 'Outstanding', width: 140, align: 'right' as const, format: formatMoneyCell },
])

const creditColumns = computed(() => [
  { key: 'doc', title: 'Credit Source', width: 170, pinned: 'left' as const },
  { key: 'creditType', title: 'Type', width: 140 },
  { key: 'creditDocumentDateUtc', title: 'Credit Date', width: 120, format: formatDateCell },
  { key: 'memo', title: 'Memo' },
  { key: 'originalAmount', title: 'Original', width: 130, align: 'right' as const, format: formatMoneyCell },
  { key: 'availableCredit', title: 'Available', width: 140, align: 'right' as const, format: formatMoneyCell },
])

const chargeRows = computed(() => {
  const items = data.value?.charges ?? []
  return items.map((item) => ({
    key: item.chargeDocumentId,
    __status: 'posted',
    doc: docLabel(item.number, item.chargeDisplay, item.chargeDocumentId),
    dueOnUtc: item.dueOnUtc,
    chargeType: item.chargeTypeDisplay ?? '—',
    vendorInvoiceNo: item.vendorInvoiceNo ?? '—',
    memo: item.memo ?? '',
    originalAmount: item.originalAmount,
    outstandingAmount: item.outstandingAmount,
  }))
})

const chargeGrid = computed(() => ({
  columns: chargeColumns.value,
  rows: chargeRows.value,
  storageKey: 'pm:payables:open-items:charges',
  onActivate: (id: string) => openDocument(resolveChargeDocumentType(id), id),
}))

const creditRows = computed(() => {
  const items = data.value?.credits ?? []
  return items.map((item) => ({
    key: item.creditDocumentId,
    __status: 'posted',
    doc: docLabel(item.number, item.creditDocumentDisplay, item.creditDocumentId),
    creditType: creditDocumentTypeLabel(item.documentType),
    creditDocumentDateUtc: item.creditDocumentDateUtc,
    memo: item.memo ?? '',
    originalAmount: item.originalAmount,
    availableCredit: item.availableCredit,
  }))
})

const creditGrid = computed(() => ({
  columns: creditColumns.value,
  rows: creditRows.value,
  storageKey: 'pm:payables:open-items:credits',
  onActivate: (id: string) => openDocument(resolveCreditDocumentType(id), id),
}))

const { markNeedsRefresh } = useOpenItemsNavigationRefresh({
  enabled: hasContextSelected,
  load,
  refreshFromRoute,
  clearRefreshFlagInRoute,
  sessionStorageKey: 'ngb:pm:payables-open-items:refresh',
})

function openDocument(documentType: string, id: string): Promise<void> {
  markNeedsRefresh()
  return router.push(`/documents/${documentType}/${id}`)
}

function resolveChargeDocumentType(chargeDocumentId: string): string {
  const row = (data.value?.charges ?? []).find((item) => item.chargeDocumentId === chargeDocumentId)
  return row?.documentType || 'pm.payable_charge'
}

function resolveCreditDocumentType(creditDocumentId: string): string {
  const row = (data.value?.credits ?? []).find((item) => item.creditDocumentId === creditDocumentId)
  return row?.documentType || 'pm.payable_payment'
}

function chargeDocumentTypeLabel(documentType: string): string {
  return documentType === 'pm.payable_charge' ? 'Payable Charge' : 'Charge'
}

function creditDocumentTypeLabel(documentType: string | null | undefined): string {
  if (documentType === 'pm.payable_credit_memo') return 'Credit Memo'
  return 'Payment'
}

async function load(): Promise<void> {
  const partyId = partyIdFromRoute.value
  const propId = propertyIdFromRoute.value
  if (!partyId || !propId) {
    data.value = null
    error.value = null
    return
  }

  loading.value = true
  error.value = null
  try {
    data.value = await getPayablesOpenItemsDetails({ partyId, propertyId: propId })
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : String(cause)
    data.value = null
  } finally {
    loading.value = false
  }
}

async function refresh(): Promise<void> {
  await hydrateContextFromRoute()
  await load()
  if (!error.value) {
    toasts.push({
      title: 'Refreshed',
      message: 'Open items updated.',
      tone: 'success',
    })
  }
}

function allocationMatchesContext(allocation: PayablesOpenItemsDetailsResponseDto['allocations'][number]): boolean {
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
  syncPreferredTab,
  syncAfterContextLoad,
  handleWizardOpenChanged,
  applyResultActionLabel,
  applyResultTitle,
  applyResultSubtitle,
} = useOpenItemsWorkflow({
  contextReady: hasContextSelected,
  data,
  summary,
  activeTab,
  toasts,
  suggestFactory: async (): Promise<PayablesSuggestFifoApplyResponseDto> => {
    const partyId = partyIdFromRoute.value
    const propId = propertyIdFromRoute.value
    if (!partyId || !propId) throw new Error('Select a vendor and property first.')

    return suggestPayablesFifoApply({
      partyId,
      propertyId: propId,
      createDrafts: false,
      limit: 500,
    })
  },
  executeFactory: (suggestion): Promise<PayablesApplyBatchResponseDto> =>
    applyPayablesBatch({
      applies: (suggestion.suggestedApplies ?? []).map((item) => ({
        applyId: item.applyId ?? null,
        applyPayload: item.applyPayload,
      })),
    }),
  unapplyFactory: (applyId) => unapplyPayablesApply(applyId),
  load,
  resolveFallbackCreditDocumentType: resolveCreditDocumentType,
  allocationMatchesContext,
  buildUnapplySuccessMessage: (line) =>
    `${creditDocumentTypeLabel(line.creditDocumentType)} ${line.creditLabel} was unapplied from ${line.chargeLabel} for ${fmtMoney(line.amount)}.`,
  buildExecuteSuccessMessage: (result) => `Open items refreshed. Total applied: ${fmtMoney(result.totalApplied ?? 0)}.`,
})

async function openApplyDocument(applyId: string): Promise<void> {
  applyWizardOpen.value = false
  markNeedsRefresh()
  await router.push(`/documents/pm.payable_apply/${applyId}`)
}

const suggestedApplyItems = computed(() => {
  return (suggestData.value?.suggestedApplies ?? []).map((item, index) => ({
    ...item,
    __key: `${item.creditDocumentId}:${item.chargeDocumentId}:${index}`,
  }))
})

const suggestedSummary = computed(() => {
  const items = suggestedApplyItems.value
  const count = items.length
  if (count === 0) return { count: 0, creditLabel: 'credit sources', chargeLabel: 'charges' }

  const first = items[0]
  return {
    count,
    creditLabel: docLabel(null, first?.creditDocumentDisplay, first?.creditDocumentId),
    chargeLabel: docLabel(null, first?.chargeDisplay, first?.chargeDocumentId),
  }
})

const applyWizardTitle = computed(() => {
  const current = suggestedSummary.value
  if (current.count <= 1) return `${current.creditLabel} → ${current.chargeLabel}`
  return `Selected ${current.count} suggested applies`
})

const formattedSuggestWarnings = computed(() => {
  return (suggestData.value?.warnings ?? []).map((warning) => {
    switch (String(warning.code ?? '').trim()) {
      case 'no_charges':
        return { title: 'No open charges', message: 'There are no outstanding payable charges to apply right now for this vendor/property.' }
      case 'no_credits':
        return { title: 'No available credits', message: 'There is no posted credit source available to apply right now for this vendor/property.' }
      case 'limit_reached':
        return { title: 'Suggestion limit reached', message: 'The wizard stopped early because the current suggestion limit was reached. Review the remaining items before continuing.' }
      case 'outstanding_remaining':
        return { title: 'Some charges will remain open', message: String(warning.message ?? '').replace('Outstanding charges remain', 'Open payable balance will remain after this apply') }
      case 'credit_remaining':
        return { title: 'Some credit will remain', message: String(warning.message ?? '').replace('Unapplied credits remain', 'Available credit source balance will remain after this apply') }
      default:
        return { title: 'Review before posting', message: warning.message }
    }
  })
})

const applyWizardColumns = computed(() => [
  { key: 'credit', title: 'Credit Source', width: 170, pinned: 'left' as const },
  { key: 'creditType', title: 'Type', width: 140 },
  { key: 'creditDocumentDateUtc', title: 'Credit Date', width: 120, format: formatDateCell },
  { key: 'charge', title: 'Charge', width: 170 },
  { key: 'chargeDueOnUtc', title: 'Due', width: 120, format: formatDateCell },
  { key: 'amount', title: 'Amount', width: 130, align: 'right' as const, format: formatMoneyCell },
])

const applyWizardRows = computed(() => {
  return suggestedApplyItems.value.map((item) => ({
    key: item.__key,
    __status: 'posted',
    credit: docLabel(null, item.creditDocumentDisplay, item.creditDocumentId),
    creditType: creditDocumentTypeLabel(item.creditDocumentType),
    creditDocumentDateUtc: item.creditDocumentDateUtc,
    charge: docLabel(null, item.chargeDisplay, item.chargeDocumentId),
    chargeDueOnUtc: item.chargeDueOnUtc,
    amount: item.amount,
  }))
})

useOpenItemsRouteContext({
  source: () => [partyIdFromRoute.value, propertyIdFromRoute.value, openApplyFromRoute.value, refreshFromRoute.value] as const,
  contextKeyCount: 2,
  hydrateContext: hydrateContextFromRoute,
  load,
  preferredTab: preferredTabFromRoute,
  currentError: error,
  syncAfterContextLoad,
  autoOpenApply: (current) => current[2],
  clearAutoOpenApplyInRoute: (current) => {
    clearAutoOpenApplyInRoute()
    if (current[3]) clearRefreshFlagInRoute()
  },
  shouldSkip: (current, previous) => {
    const [partyId, propertyId, shouldOpenApply, shouldRefresh] = current
    const [prevPartyId, prevPropertyId, prevShouldOpenApply, prevShouldRefresh] = previous ?? [null, null, false, false]
    const contextChanged = partyId !== prevPartyId || propertyId !== prevPropertyId
    return (
      !contextChanged
      && !shouldOpenApply
      && !shouldRefresh
      && (prevShouldOpenApply === true || prevShouldRefresh === true)
    )
  },
  afterSync: async (current) => {
    if (current[3] && !current[2]) clearRefreshFlagInRoute()
  },
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

const tabs = computed(() => buildOpenItemsTabs(summary.value))
</script>

<template>
  <OpenItemsWorkflowShell
    title="Payables"
    :lookups="lookupControls"
    :loading="loading"
    :error="error"
    :context-ready="hasContextSelected"
    empty-state-message="Select a vendor and property to view open payable items (charges and available credits)."
    :context-badges="contextBadges"
    :focused-context-badge="focusedContextBadge"
    :summary="summary"
    :page-result="pageResult"
    :tabs="tabs"
    :active-tab="activeTab"
    :charge-grid="chargeGrid"
    :credit-grid="creditGrid"
    :applied-rows="appliedAllocations"
    applied-subtitle="Current active allocations for this vendor/property. Reversed applies are hidden."
    applied-empty-message="No applied allocations yet for this vendor/property. Once a credit source is applied to a charge, it will appear here."
    :highlighted-apply-ids="highlightedApplyIds"
    :is-context-allocation="allocationMatchesContext"
    :resolve-charge-type-label="chargeDocumentTypeLabel"
    :resolve-credit-type-label="creditDocumentTypeLabel"
    :open-applied-document="openDocument"
    :open-apply-document="openApplyDocument"
    :request-unapply="requestUnapply"
    :can-refresh="hasContextSelected"
    :can-apply="hasContextSelected"
    :apply-wizard-open="applyWizardOpen"
    :apply-wizard-subtitle="applyWizardView === 'result' ? 'Applied allocations and refreshed open items' : 'Suggest FIFO allocations and post them'"
    :apply-wizard-action-disabled="suggestLoading || applyExecLoading || !hasContextSelected"
    :apply-wizard-action-title="applyWizardView === 'result' ? applyResultActionLabel : 'Resuggest'"
    empty-wizard-message="Select a vendor and property first."
    :unapply-confirm-open="unapplyConfirmOpen"
    unapply-title="Unapply this allocation?"
    :unapply-message="pendingUnapplyLine ? `Unapply ${creditDocumentTypeLabel(pendingUnapplyLine.creditDocumentType).toLowerCase()} ${pendingUnapplyLine.creditLabel} from ${pendingUnapplyLine.chargeLabel} for ${fmtMoney(pendingUnapplyLine.amount)}?` : 'Unapply this allocation?'"
    unapply-confirm-text="Unapply"
    unapply-cancel-text="Cancel"
    unapply-danger
    :unapply-confirm-loading="unapplyLoading"
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
      <div class="space-y-4">
        <div
          v-if="suggestError && applyWizardView === 'suggest'"
          class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 dark:border-red-900/50 dark:bg-red-950/30 p-3 text-sm text-red-900 dark:text-red-100"
        >
          {{ suggestError }}
        </div>

        <div
          v-if="applyExecError"
          class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 dark:border-red-900/50 dark:bg-red-950/30 p-3 text-sm text-red-900 dark:text-red-100"
        >
          {{ applyExecError }}
        </div>

        <div class="flex flex-wrap items-center gap-2">
          <NgbBadge tone="neutral">Vendor: {{ suggestData?.vendorDisplay ?? data?.vendorDisplay ?? vendorDisplay }}</NgbBadge>
          <NgbBadge tone="neutral">Property: {{ suggestData?.propertyDisplay ?? data?.propertyDisplay ?? propertyDisplay }}</NgbBadge>
          <div class="ml-auto grid grid-cols-2 gap-2 min-w-[260px]">
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">Outstanding after</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(previewAfterOutstanding) }}</div>
            </div>
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">Credit after</div>
              <div class="font-semibold tabular-nums">{{ fmtMoney(previewAfterCredit) }}</div>
            </div>
          </div>
        </div>

        <template v-if="applyWizardView === 'suggest'">
          <div v-if="suggestLoading" class="text-sm text-ngb-muted">Building FIFO suggestion…</div>

          <template v-else>
            <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card px-3 py-2 text-sm">
              <div class="text-xs text-ngb-muted">Will create</div>
              <div class="font-semibold">{{ formatApplyCount(suggestedApplyItems.length) }} totaling {{ fmtMoney(suggestData?.totalApplied ?? 0) }}</div>
            </div>

            <div v-if="formattedSuggestWarnings.length > 0" class="space-y-2">
              <div
                v-for="warning in formattedSuggestWarnings"
                :key="`${warning.title}:${warning.message}`"
                class="rounded-[var(--ngb-radius)] border border-amber-200 bg-amber-50 dark:border-amber-900/50 dark:bg-amber-950/20 p-3"
              >
                <div class="text-sm font-semibold text-amber-900 dark:text-amber-100">{{ warning.title }}</div>
                <div class="mt-1 text-sm text-amber-900/85 dark:text-amber-100/85">{{ warning.message }}</div>
              </div>
            </div>

            <div
              v-if="suggestedApplyItems.length > 0"
              class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card p-4"
            >
              <div class="text-sm font-semibold text-ngb-text">{{ applyWizardTitle }}</div>
              <div class="mt-1 text-sm text-ngb-muted">
                FIFO suggestion will create {{ formatApplyCount(suggestedApplyItems.length) }} totaling {{ fmtMoney(suggestData?.totalApplied ?? 0) }}.
              </div>
            </div>

            <div class="h-[420px]">
              <NgbRegisterGrid
                fill-height
                :show-panel="false"
                :show-totals="false"
                :columns="applyWizardColumns"
                :rows="applyWizardRows"
                storage-key="pm:payables:apply-wizard"
                class="h-full min-h-0"
              />
            </div>

            <div v-if="suggestedApplyItems.length === 0" class="text-sm text-ngb-muted">
              No suggested applies right now.
            </div>
          </template>
        </template>

        <template v-else>
          <div class="rounded-[var(--ngb-radius)] border border-green-200 bg-green-50 dark:border-green-900/50 dark:bg-green-950/20 p-4">
            <div class="text-sm font-semibold text-green-900 dark:text-green-100">{{ applyResultTitle }}</div>
            <div class="mt-1 text-sm text-green-900/85 dark:text-green-100/85">{{ applyResultSubtitle }}</div>
          </div>

          <div class="space-y-2">
            <div
              v-for="line in applyResultLines"
              :key="line.key"
              class="rounded-[var(--ngb-radius)] border border-green-200/70 bg-white/80 dark:bg-black/10 px-3 py-3"
            >
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0">
                  <div class="text-sm font-medium text-ngb-text break-words">{{ line.creditLabel }} → {{ line.chargeLabel }}</div>
                  <div class="mt-1 text-xs text-ngb-muted">Applied {{ fmtDateOnly(line.appliedOnUtc) }} · {{ fmtMoney(line.amount) }}</div>
                </div>
                <div class="flex items-center gap-2 shrink-0">
                  <NgbButton size="sm" variant="secondary" @click="openApplyDocument(line.applyId)">Open Apply</NgbButton>
                </div>
              </div>
            </div>
          </div>
        </template>
      </div>
    </template>

    <template #footer>
      <div class="flex items-center justify-end gap-2">
        <NgbButton variant="secondary" :disabled="applyExecLoading || suggestLoading || unapplyLoading" @click="applyWizardOpen = false">Close</NgbButton>
        <NgbButton
          v-if="applyWizardView === 'suggest'"
          variant="primary"
          :disabled="!canExecuteApply"
          :loading="applyExecLoading"
          @click="executeApplyBatch"
        >
          Execute Apply
        </NgbButton>
      </div>
    </template>

    <template #unapply>
    <div class="space-y-3 text-sm">
      <div v-if="pendingUnapplyLine">
        Unapply <span class="font-medium">{{ creditDocumentTypeLabel(pendingUnapplyLine.creditDocumentType).toLowerCase() }} {{ pendingUnapplyLine.creditLabel }}</span>
        from <span class="font-medium">{{ pendingUnapplyLine.chargeLabel }}</span>
        for <span class="font-medium">{{ fmtMoney(pendingUnapplyLine.amount) }}</span>?
      </div>

      <div
        v-if="unapplyError"
        class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 dark:border-red-900/50 dark:bg-red-950/30 p-3 text-red-900 dark:text-red-100"
      >
        {{ unapplyError }}
      </div>
    </div>
    </template>
  </OpenItemsWorkflowShell>
</template>
