import { computed, ref, type ComputedRef, type Ref } from 'vue'

import { buildApplyResultSubtitle, buildApplyResultTitle, type OpenItemsPageResultView, type OpenItemsTabKey } from './presentation'
import { docLabel, fmtMoney, type ApplyWizardView, type OpenItemsApplyResultLine } from './shared'

type ToastApi = {
  push: (value: { title: string; message: string; tone: 'danger' | 'warn' | 'success' | 'neutral' }) => void
}

type OpenItemsSummaryLike = {
  totalOutstanding: number
  totalCredit: number
}

type OpenItemsExecutedApplyLike = {
  applyId: string
  creditDocumentId: string
  chargeDocumentId: string
  appliedOnUtc: string
  amount: number
}

type OpenItemsSuggestedApplyLike = {
  applyId?: string | null
  creditDocumentId: string
  creditDocumentType: string
  creditDocumentDisplay?: string | null
  chargeDocumentId: string
  chargeDisplay?: string | null
  amount: number
}

type OpenItemsAllocationLike = {
  applyId: string
  creditDocumentId: string
  creditDocumentType: string
  creditDocumentDisplay?: string | null
  creditDocumentNumber?: string | null
  chargeDocumentId: string
  chargeDisplay?: string | null
  chargeNumber?: string | null
  appliedOnUtc: string
  amount: number
  isPosted: boolean
}

type OpenItemsSuggestResponseLike<TSuggestedApply extends OpenItemsSuggestedApplyLike> = {
  suggestedApplies?: TSuggestedApply[] | null
  totalApplied?: number | null
  remainingOutstanding?: number | null
}

type OpenItemsApplyBatchResponseLike<TExecutedApply extends OpenItemsExecutedApplyLike> = {
  totalApplied?: number | null
  executedApplies?: TExecutedApply[] | null
}

type OpenItemsDetailsLike<TAllocation extends OpenItemsAllocationLike> = {
  allocations?: TAllocation[] | null
}

type UseOpenItemsWorkflowArgs<
  TAllocation extends OpenItemsAllocationLike,
  TSuggestedApply extends OpenItemsSuggestedApplyLike,
  TSuggestResponse extends OpenItemsSuggestResponseLike<TSuggestedApply>,
  TApplyResult extends OpenItemsApplyBatchResponseLike<OpenItemsExecutedApplyLike>,
> = {
  contextReady: ComputedRef<boolean>
  data: Ref<OpenItemsDetailsLike<TAllocation> | null>
  summary: ComputedRef<OpenItemsSummaryLike>
  activeTab: Ref<OpenItemsTabKey>
  toasts: ToastApi
  suggestFactory: () => Promise<TSuggestResponse>
  executeFactory: (suggestData: TSuggestResponse) => Promise<TApplyResult>
  unapplyFactory: (applyId: string) => Promise<void>
  load: () => Promise<void>
  resolveFallbackCreditDocumentType: (creditDocumentId: string) => string
  allocationMatchesContext: (allocation: TAllocation) => boolean
  buildUnapplySuccessMessage: (line: OpenItemsApplyResultLine) => string
  buildExecuteSuccessMessage: (result: TApplyResult) => string
}

export function useOpenItemsWorkflow<
  TAllocation extends OpenItemsAllocationLike,
  TSuggestedApply extends OpenItemsSuggestedApplyLike,
  TSuggestResponse extends OpenItemsSuggestResponseLike<TSuggestedApply>,
  TApplyResult extends OpenItemsApplyBatchResponseLike<OpenItemsExecutedApplyLike>,
>(args: UseOpenItemsWorkflowArgs<TAllocation, TSuggestedApply, TSuggestResponse, TApplyResult>) {
  const applyWizardOpen = ref(false)
  const applyWizardView = ref<ApplyWizardView>('suggest')
  const suggestLoading = ref(false)
  const suggestError = ref<string | null>(null)
  const suggestData = ref<TSuggestResponse | null>(null)
  const applyExecLoading = ref(false)
  const applyExecError = ref<string | null>(null)
  const applyResult = ref<TApplyResult | null>(null)
  const pageApplyResult = ref<TApplyResult | null>(null)
  const pageApplyResultDismissed = ref(false)
  const unapplyLoading = ref(false)
  const unapplyError = ref<string | null>(null)
  const unapplyConfirmOpen = ref(false)
  const pendingUnapplyLine = ref<OpenItemsApplyResultLine | null>(null)
  const highlightedApplyIds = ref<string[]>([])

  function resetApplyFlowState() {
    applyWizardView.value = 'suggest'
    suggestData.value = null
    suggestError.value = null
    applyExecError.value = null
    applyResult.value = null
    unapplyLoading.value = false
    unapplyError.value = null
    unapplyConfirmOpen.value = false
    pendingUnapplyLine.value = null
  }

  function resolveApplyResultLines(result: TApplyResult | null): OpenItemsApplyResultLine[] {
    if (!result) return []

    const applyIds = new Set((result.executedApplies ?? []).map((item) => item.applyId))
    const canonical = (args.data.value?.allocations ?? []).filter((item) => applyIds.has(item.applyId))
    if (canonical.length > 0) {
      return canonical.map((item) => ({
        key: item.applyId,
        applyId: item.applyId,
        creditDocumentId: item.creditDocumentId,
        creditDocumentType: item.creditDocumentType,
        creditLabel: docLabel(item.creditDocumentNumber, item.creditDocumentDisplay, item.creditDocumentId),
        chargeDocumentId: item.chargeDocumentId,
        chargeLabel: docLabel(item.chargeNumber, item.chargeDisplay, item.chargeDocumentId),
        appliedOnUtc: item.appliedOnUtc,
        amount: item.amount,
      }))
    }

    const suggested = suggestData.value?.suggestedApplies ?? []
    return (result.executedApplies ?? []).map((item, index) => {
      const hint = suggested[index]
      return {
        key: item.applyId,
        applyId: item.applyId,
        creditDocumentId: item.creditDocumentId,
        creditDocumentType: hint?.creditDocumentType ?? args.resolveFallbackCreditDocumentType(item.creditDocumentId),
        creditLabel: docLabel(null, hint?.creditDocumentDisplay, item.creditDocumentId),
        chargeDocumentId: item.chargeDocumentId,
        chargeLabel: docLabel(null, hint?.chargeDisplay, item.chargeDocumentId),
        appliedOnUtc: item.appliedOnUtc,
        amount: item.amount,
      }
    })
  }

  const applyResultLines = computed<OpenItemsApplyResultLine[]>(() => resolveApplyResultLines(applyResult.value))
  const pageApplyResultLines = computed<OpenItemsApplyResultLine[]>(() => resolveApplyResultLines(pageApplyResult.value))

  function applyResultTitleFor(result: TApplyResult | null): string {
    const count = result?.executedApplies?.length ?? 0
    return buildApplyResultTitle(count)
  }

  function applyResultSubtitleFor(result: TApplyResult | null): string {
    const totalApplied = result?.totalApplied ?? 0
    return buildApplyResultSubtitle(result?.executedApplies?.length ?? 0, totalApplied, fmtMoney)
  }

  const applyResultTitle = computed(() => applyResultTitleFor(applyResult.value))
  const applyResultSubtitle = computed(() => applyResultSubtitleFor(applyResult.value))
  const pageApplyResultTitle = computed(() => applyResultTitleFor(pageApplyResult.value))
  const pageApplyResultSubtitle = computed(() => applyResultSubtitleFor(pageApplyResult.value))

  const applyResultActionLabel = computed(() => {
    if ((args.summary.value.totalOutstanding ?? 0) > 0 && (args.summary.value.totalCredit ?? 0) > 0) return 'Suggest remaining'
    return 'Review remaining'
  })

  const pageApplyResultVisible = computed(() => !!pageApplyResult.value && !pageApplyResultDismissed.value)
  const pageApplyResultInconsistent = computed(() => {
    const result = pageApplyResult.value
    if (!result) return false
    const executedCount = result.executedApplies?.length ?? 0
    if (executedCount === 0) return false
    return pageApplyResultLines.value.length === 0
  })

  const pageResult = computed<OpenItemsPageResultView>(() => ({
    visible: pageApplyResultVisible.value,
    title: pageApplyResultTitle.value,
    subtitle: pageApplyResultSubtitle.value,
    lines: pageApplyResultLines.value,
    outstandingNow: args.summary.value.totalOutstanding,
    creditNow: args.summary.value.totalCredit,
    inconsistent: pageApplyResultInconsistent.value,
  }))

  const appliedAllocations = computed(() => {
    const items = [...(args.data.value?.allocations ?? [])]
    items.sort((left, right) => {
      const leftPriority = (highlightedApplyIds.value.includes(left.applyId) ? 4 : 0) + (args.allocationMatchesContext(left) ? 2 : 0) + (left.isPosted ? 1 : 0)
      const rightPriority = (highlightedApplyIds.value.includes(right.applyId) ? 4 : 0) + (args.allocationMatchesContext(right) ? 2 : 0) + (right.isPosted ? 1 : 0)
      if (leftPriority !== rightPriority) return rightPriority - leftPriority
      return String(right.appliedOnUtc ?? '').localeCompare(String(left.appliedOnUtc ?? ''))
    })
    return items
  })

  const canExecuteApply = computed(() => {
    const count = suggestData.value?.suggestedApplies?.length ?? 0
    return count > 0 && !suggestLoading.value && !applyExecLoading.value
  })

  const previewAfterOutstanding = computed(() => {
    if (!suggestData.value) return args.summary.value.totalOutstanding
    return Math.max(0, suggestData.value.remainingOutstanding ?? 0)
  })

  const previewAfterCredit = computed(() => {
    if (!suggestData.value) return args.summary.value.totalCredit
    return Math.max(0, (args.summary.value.totalCredit ?? 0) - (suggestData.value.totalApplied ?? 0))
  })

  async function suggest(): Promise<void> {
    suggestLoading.value = true
    suggestError.value = null
    applyExecError.value = null

    try {
      suggestData.value = await args.suggestFactory()
    } catch (cause) {
      suggestData.value = null
      suggestError.value = cause instanceof Error ? cause.message : String(cause)
    } finally {
      suggestLoading.value = false
    }
  }

  async function openApplyWizard() {
    if (!args.contextReady.value) return
    resetApplyFlowState()
    highlightedApplyIds.value = []
    applyWizardOpen.value = true
  }

  function requestUnapply(line: OpenItemsApplyResultLine): void {
    pendingUnapplyLine.value = line
    unapplyError.value = null
    unapplyConfirmOpen.value = true
  }

  function onUnapplyConfirmOpenChanged(value: boolean): void {
    unapplyConfirmOpen.value = value
    if (!value && !unapplyLoading.value) pendingUnapplyLine.value = null
  }

  async function confirmUnapply(): Promise<void> {
    const line = pendingUnapplyLine.value
    if (!line) return

    unapplyLoading.value = true
    unapplyError.value = null
    try {
      await args.unapplyFactory(line.applyId)
      unapplyConfirmOpen.value = false
      pendingUnapplyLine.value = null
      highlightedApplyIds.value = highlightedApplyIds.value.filter((item) => item !== line.applyId)
      await args.load()
      args.toasts.push({
        title: 'Unapplied',
        message: args.buildUnapplySuccessMessage(line),
        tone: 'success',
      })
    } catch (cause) {
      unapplyError.value = cause instanceof Error ? cause.message : String(cause)
      args.toasts.push({
        title: 'Unapply failed',
        message: unapplyError.value,
        tone: 'danger',
      })
    } finally {
      unapplyLoading.value = false
    }
  }

  async function showApplyPlanAgain(): Promise<void> {
    applyWizardView.value = 'suggest'
    applyResult.value = null
    await suggest()
  }

  async function executeApplyBatch(): Promise<void> {
    if (!suggestData.value) return

    applyExecLoading.value = true
    applyExecError.value = null
    try {
      const result = await args.executeFactory(suggestData.value)
      applyResult.value = result
      pageApplyResult.value = result
      pageApplyResultDismissed.value = false
      highlightedApplyIds.value = (result.executedApplies ?? []).map((item) => item.applyId)
      await args.load()
      args.activeTab.value = 'applied'
      applyWizardOpen.value = false
      applyWizardView.value = 'result'

      args.toasts.push({
        title: applyResultTitleFor(result),
        message: args.buildExecuteSuccessMessage(result),
        tone: 'success',
      })
    } catch (cause) {
      applyExecError.value = cause instanceof Error ? cause.message : String(cause)
      args.toasts.push({
        title: 'Apply failed',
        message: applyExecError.value,
        tone: 'danger',
      })
    } finally {
      applyExecLoading.value = false
    }
  }

  function dismissPageApplyResult(): void {
    pageApplyResultDismissed.value = true
  }

  function showAppliedTab(): void {
    args.activeTab.value = 'applied'
    applyWizardOpen.value = false
  }

  function syncPreferredTab(preferredTab: OpenItemsTabKey | null): void {
    if (preferredTab && highlightedApplyIds.value.length === 0) {
      args.activeTab.value = preferredTab
    }
  }

  async function syncAfterContextLoad(options: {
    contextChanged: boolean
    preferredTab: OpenItemsTabKey | null
    autoOpenApply: boolean
    clearAutoOpenApplyInRoute: () => void
    currentError: string | null
  }): Promise<void> {
    if (options.contextChanged) {
      highlightedApplyIds.value = []
      applyResult.value = null
      pageApplyResult.value = null
      pageApplyResultDismissed.value = false
      if (!options.autoOpenApply) applyWizardView.value = 'suggest'
    }

    syncPreferredTab(options.preferredTab)

    if (options.autoOpenApply && args.contextReady.value && !options.currentError) {
      resetApplyFlowState()
      applyWizardOpen.value = true
      options.clearAutoOpenApplyInRoute()
      return
    }

    if (applyWizardOpen.value && args.contextReady.value && applyWizardView.value === 'suggest') {
      await suggest()
    }
  }

  async function handleWizardOpenChanged(open: boolean): Promise<void> {
    if (!open) {
      resetApplyFlowState()
      return
    }

    applyWizardView.value = 'suggest'
    applyResult.value = null

    if (args.contextReady.value) await suggest()
  }

  return {
    applyWizardOpen,
    applyWizardView,
    suggestLoading,
    suggestError,
    suggestData,
    applyExecLoading,
    applyExecError,
    applyResult,
    pageApplyResult,
    pageApplyResultDismissed,
    unapplyLoading,
    unapplyError,
    unapplyConfirmOpen,
    pendingUnapplyLine,
    highlightedApplyIds,
    applyResultLines,
    pageApplyResultLines,
    applyResultTitle,
    applyResultSubtitle,
    pageApplyResultTitle,
    pageApplyResultSubtitle,
    applyResultActionLabel,
    pageResult,
    appliedAllocations,
    canExecuteApply,
    previewAfterOutstanding,
    previewAfterCredit,
    resetApplyFlowState,
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
  }
}
