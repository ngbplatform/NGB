import { computed, effectScope, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useOpenItemsWorkflow } from '../../../../src/features/open-items/workflow'
import type { OpenItemsTabKey } from '../../../../src/features/open-items/presentation'
import type { OpenItemsApplyResultLine } from '../../../../src/features/open-items/shared'

type Allocation = {
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

type Suggestion = {
  applyId?: string | null
  creditDocumentId: string
  creditDocumentType: string
  creditDocumentDisplay?: string | null
  chargeDocumentId: string
  chargeDisplay?: string | null
  amount: number
}

type SuggestResponse = {
  suggestedApplies?: Suggestion[] | null
  totalApplied?: number | null
  remainingOutstanding?: number | null
}

type ExecutedApply = {
  applyId: string
  creditDocumentId: string
  chargeDocumentId: string
  appliedOnUtc: string
  amount: number
}

type ApplyResult = {
  totalApplied?: number | null
  executedApplies?: ExecutedApply[] | null
}

function allocation(applyId: string, options: Partial<Allocation> = {}): Allocation {
  return {
    applyId,
    creditDocumentId: `credit-${applyId}`,
    creditDocumentType: 'pm.receivable_credit_memo',
    creditDocumentDisplay: `Credit ${applyId}`,
    creditDocumentNumber: `CM-${applyId}`,
    chargeDocumentId: `charge-${applyId}`,
    chargeDisplay: `Charge ${applyId}`,
    chargeNumber: `INV-${applyId}`,
    appliedOnUtc: '2026-08-01T00:00:00Z',
    amount: 25,
    isPosted: false,
    ...options,
  }
}

function executed(applyId: string): ExecutedApply {
  return {
    applyId,
    creditDocumentId: `credit-${applyId}`,
    chargeDocumentId: `charge-${applyId}`,
    appliedOnUtc: '2026-08-02T00:00:00Z',
    amount: 25,
  }
}

function resultLine(applyId = 'apply-1'): OpenItemsApplyResultLine {
  return {
    key: applyId,
    applyId,
    creditDocumentId: `credit-${applyId}`,
    creditDocumentType: 'pm.receivable_credit_memo',
    creditLabel: `Credit ${applyId}`,
    chargeDocumentId: `charge-${applyId}`,
    chargeLabel: `Charge ${applyId}`,
    appliedOnUtc: '2026-08-02T00:00:00Z',
    amount: 25,
  }
}

function createHarness() {
  const contextReady = ref(true)
  const data = ref<{ allocations?: Allocation[] | null } | null>({ allocations: [] })
  const summaryValue = ref({ totalOutstanding: 100, totalCredit: 80 })
  const activeTab = ref<OpenItemsTabKey>('charges')
  const toasts = { push: vi.fn() }
  const suggestFactory = vi.fn<(options?: { signal?: AbortSignal }) => Promise<SuggestResponse>>(async () => ({
    suggestedApplies: [],
    totalApplied: 0,
    remainingOutstanding: 100,
  }))
  const executeFactory = vi.fn<(suggestion: SuggestResponse) => Promise<ApplyResult>>(async () => ({
    totalApplied: 0,
    executedApplies: [],
  }))
  const unapplyFactory = vi.fn<(applyId: string) => Promise<void>>(async () => undefined)
  const load = vi.fn(async () => undefined)
  const resolveFallbackCreditDocumentType = vi.fn(() => 'pm.receivable_credit_memo')
  const allocationMatchesContext = vi.fn((item: Allocation) => item.applyId.startsWith('context-'))
  const buildUnapplySuccessMessage = vi.fn((line: OpenItemsApplyResultLine) => `Unapplied ${line.applyId}`)
  const buildExecuteSuccessMessage = vi.fn((result: ApplyResult) => `Applied ${result.totalApplied ?? 0}`)

  const workflow = useOpenItemsWorkflow({
    contextReady: computed(() => contextReady.value),
    data,
    summary: computed(() => summaryValue.value),
    activeTab,
    toasts,
    suggestFactory,
    executeFactory,
    unapplyFactory,
    load,
    resolveFallbackCreditDocumentType,
    allocationMatchesContext,
    buildUnapplySuccessMessage,
    buildExecuteSuccessMessage,
  })

  return {
    workflow,
    contextReady,
    data,
    summaryValue,
    activeTab,
    toasts,
    suggestFactory,
    executeFactory,
    unapplyFactory,
    load,
    resolveFallbackCreditDocumentType,
  }
}

describe('open items workflow', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('exposes safe initial state, action labels, previews, and deterministic applied ordering', () => {
    const harness = createHarness()
    const { workflow } = harness

    expect(workflow.applyResultLines.value).toEqual([])
    expect(workflow.pageApplyResultLines.value).toEqual([])
    expect(workflow.pageResult.value).toMatchObject({ visible: false, lines: [], inconsistent: false })
    expect(workflow.applyResultActionLabel.value).toBe('Suggest remaining')
    expect(workflow.previewAfterOutstanding.value).toBe(100)
    expect(workflow.previewAfterCredit.value).toBe(80)
    expect(workflow.canExecuteApply.value).toBe(false)

    harness.summaryValue.value = { totalOutstanding: 0, totalCredit: 80 }
    expect(workflow.applyResultActionLabel.value).toBe('Review remaining')
    harness.summaryValue.value = { totalOutstanding: 100, totalCredit: 0 }
    expect(workflow.applyResultActionLabel.value).toBe('Review remaining')

    harness.data.value = {
      allocations: [
        allocation('plain-old', { appliedOnUtc: '2026-01-01T00:00:00Z' }),
        allocation('context-posted', { isPosted: true }),
        allocation('highlight-only'),
        allocation('plain-new', { appliedOnUtc: '2026-09-01T00:00:00Z' }),
        allocation('context-highlight', { isPosted: true }),
      ],
    }
    workflow.highlightedApplyIds.value = ['highlight-only', 'context-highlight']
    expect(workflow.appliedAllocations.value.map((item) => item.applyId)).toEqual([
      'context-highlight',
      'highlight-only',
      'context-posted',
      'plain-new',
      'plain-old',
    ])

    harness.data.value = null
    expect(workflow.appliedAllocations.value).toEqual([])
  })

  it('suggests allocations and preserves useful success and failure state', async () => {
    const harness = createHarness()
    const { workflow, suggestFactory } = harness
    suggestFactory.mockResolvedValueOnce({
      suggestedApplies: [{
        applyId: 'apply-1',
        creditDocumentId: 'credit-1',
        creditDocumentType: 'pm.receivable_credit_memo',
        chargeDocumentId: 'charge-1',
        amount: 90,
      }],
      totalApplied: 90,
      remainingOutstanding: -5,
    })

    await workflow.suggest()

    expect(workflow.suggestError.value).toBeNull()
    expect(workflow.canExecuteApply.value).toBe(true)
    expect(workflow.previewAfterOutstanding.value).toBe(0)
    expect(workflow.previewAfterCredit.value).toBe(0)

    workflow.suggestLoading.value = true
    expect(workflow.canExecuteApply.value).toBe(false)
    workflow.suggestLoading.value = false
    workflow.applyExecLoading.value = true
    expect(workflow.canExecuteApply.value).toBe(false)
    workflow.applyExecLoading.value = false

    workflow.suggestData.value = {}
    expect(workflow.canExecuteApply.value).toBe(false)
    expect(workflow.previewAfterOutstanding.value).toBe(0)
    expect(workflow.previewAfterCredit.value).toBe(80)

    suggestFactory.mockRejectedValueOnce(new Error('suggestion failed'))
    await workflow.suggest()
    expect(workflow.suggestError.value).toBe('suggestion failed')
    expect(workflow.suggestData.value).toBeNull()

    suggestFactory.mockRejectedValueOnce('offline')
    await workflow.suggest()
    expect(workflow.suggestError.value).toBe('offline')
    expect(workflow.suggestLoading.value).toBe(false)
  })

  it('aborts and ignores a stale suggestion when a newer request starts', async () => {
    const harness = createHarness()
    let resolveFirst!: (value: SuggestResponse) => void
    harness.suggestFactory
      .mockImplementationOnce(() => new Promise((resolve) => { resolveFirst = resolve }))
      .mockResolvedValueOnce({ suggestedApplies: [], totalApplied: 12, remainingOutstanding: 88 })

    const first = harness.workflow.suggest()
    await vi.waitFor(() => expect(harness.suggestFactory).toHaveBeenCalledOnce())
    const firstSignal = harness.suggestFactory.mock.calls[0]?.[0]?.signal
    const second = harness.workflow.suggest()
    await second

    expect(firstSignal?.aborted).toBe(true)
    resolveFirst({ suggestedApplies: [], totalApplied: 99, remainingOutstanding: 1 })
    await first
    expect(harness.workflow.suggestData.value?.totalApplied).toBe(12)
    expect(harness.workflow.suggestLoading.value).toBe(false)
  })

  it('ignores a stale rejected suggestion after a newer request succeeds', async () => {
    const harness = createHarness()
    let rejectFirst!: (reason: unknown) => void
    harness.suggestFactory
      .mockImplementationOnce(() => new Promise((_resolve, reject) => { rejectFirst = reject }))
      .mockResolvedValueOnce({ suggestedApplies: [], totalApplied: 12, remainingOutstanding: 88 })

    const first = harness.workflow.suggest()
    await vi.waitFor(() => expect(harness.suggestFactory).toHaveBeenCalledOnce())
    await harness.workflow.suggest()
    rejectFirst(new Error('stale failure'))
    await first

    expect(harness.workflow.suggestData.value?.totalApplied).toBe(12)
    expect(harness.workflow.suggestError.value).toBeNull()
  })

  it('cancels an in-flight suggestion when its Vue effect scope is disposed', async () => {
    const scope = effectScope()
    let harness!: ReturnType<typeof createHarness>
    scope.run(() => { harness = createHarness() })
    let resolveSuggestion!: (value: SuggestResponse) => void
    harness.suggestFactory.mockImplementationOnce(() => new Promise((resolve) => {
      resolveSuggestion = resolve
    }))

    const request = harness.workflow.suggest()
    await vi.waitFor(() => expect(harness.suggestFactory).toHaveBeenCalledOnce())
    const signal = harness.suggestFactory.mock.calls[0]?.[0]?.signal
    scope.stop()

    expect(signal?.aborted).toBe(true)
    expect(harness.workflow.suggestLoading.value).toBe(false)
    resolveSuggestion({ suggestedApplies: [], totalApplied: 99, remainingOutstanding: 1 })
    await request
    expect(harness.workflow.suggestData.value).toBeNull()
  })

  it('builds canonical and fallback result lines with titles and subtitles', () => {
    const harness = createHarness()
    const { workflow } = harness
    const applyResult: ApplyResult = { totalApplied: 50, executedApplies: [executed('apply-1')] }

    harness.data.value = { allocations: [allocation('apply-1')] }
    workflow.applyResult.value = applyResult
    expect(workflow.applyResultLines.value[0]).toMatchObject({
      applyId: 'apply-1',
      creditLabel: 'CM-apply-1',
      chargeLabel: 'INV-apply-1',
    })
    expect(workflow.applyResultTitle.value).toContain('1')
    expect(workflow.applyResultSubtitle.value).toContain('50')

    harness.data.value = { allocations: [] }
    workflow.suggestData.value = {
      suggestedApplies: [{
        creditDocumentId: 'credit-1',
        creditDocumentType: 'custom.credit',
        creditDocumentDisplay: 'Credit display',
        chargeDocumentId: 'charge-1',
        chargeDisplay: 'Charge display',
        amount: 25,
      }],
    }
    workflow.applyResult.value = {
      executedApplies: [executed('apply-1'), executed('apply-2')],
    }
    expect(workflow.applyResultLines.value).toHaveLength(2)
    expect(workflow.applyResultLines.value[0]).toMatchObject({
      creditDocumentType: 'custom.credit',
      creditLabel: 'Credit display',
      chargeLabel: 'Charge display',
    })
    expect(workflow.applyResultLines.value[1]?.creditDocumentType).toBe('pm.receivable_credit_memo')
    expect(harness.resolveFallbackCreditDocumentType).toHaveBeenCalledWith('credit-apply-2')

    harness.data.value = { allocations: null }
    workflow.suggestData.value = null
    workflow.applyResult.value = { executedApplies: [executed('apply-3')] }
    expect(workflow.applyResultLines.value[0]?.creditDocumentType).toBe('pm.receivable_credit_memo')

    workflow.pageApplyResult.value = applyResult
    expect(workflow.pageResult.value.visible).toBe(true)
    expect(workflow.pageApplyResultTitle.value).toContain('1')
    expect(workflow.pageApplyResultSubtitle.value).toContain('50')
    workflow.dismissPageApplyResult()
    expect(workflow.pageResult.value.visible).toBe(false)

    workflow.applyResult.value = { executedApplies: null, totalApplied: null }
    expect(workflow.applyResultLines.value).toEqual([])
  })

  it('executes apply batches and reports successful and failed outcomes', async () => {
    const harness = createHarness()
    const { workflow, executeFactory } = harness

    await workflow.executeApplyBatch()
    expect(executeFactory).not.toHaveBeenCalled()

    const suggestion: SuggestResponse = { suggestedApplies: [], totalApplied: 25 }
    const success: ApplyResult = { totalApplied: 25, executedApplies: [executed('apply-1')] }
    workflow.suggestData.value = suggestion
    executeFactory.mockResolvedValueOnce(success)
    await workflow.executeApplyBatch()

    expect(workflow.applyResult.value).toEqual(success)
    expect(workflow.pageApplyResult.value).toEqual(success)
    expect(workflow.highlightedApplyIds.value).toEqual(['apply-1'])
    expect(harness.load).toHaveBeenCalledOnce()
    expect(harness.activeTab.value).toBe('applied')
    expect(workflow.applyWizardOpen.value).toBe(false)
    expect(workflow.applyWizardView.value).toBe('result')
    expect(harness.toasts.push).toHaveBeenCalledWith(expect.objectContaining({ tone: 'success' }))

    executeFactory.mockResolvedValueOnce({ totalApplied: 0, executedApplies: null })
    await workflow.executeApplyBatch()
    expect(workflow.highlightedApplyIds.value).toEqual([])

    executeFactory.mockRejectedValueOnce(new Error('apply failed'))
    await workflow.executeApplyBatch()
    expect(workflow.applyExecError.value).toBe('apply failed')
    expect(harness.toasts.push).toHaveBeenLastCalledWith(expect.objectContaining({ tone: 'danger' }))

    executeFactory.mockRejectedValueOnce('network unavailable')
    await workflow.executeApplyBatch()
    expect(workflow.applyExecError.value).toBe('network unavailable')
    expect(workflow.applyExecLoading.value).toBe(false)
  })

  it('handles unapply confirmation lifecycle, success, and both failure shapes', async () => {
    const harness = createHarness()
    const { workflow, unapplyFactory } = harness
    const line = resultLine()

    await workflow.confirmUnapply()
    expect(unapplyFactory).not.toHaveBeenCalled()

    workflow.requestUnapply(line)
    expect(workflow.unapplyConfirmOpen.value).toBe(true)
    workflow.onUnapplyConfirmOpenChanged(true)
    workflow.unapplyLoading.value = true
    workflow.onUnapplyConfirmOpenChanged(false)
    expect(workflow.pendingUnapplyLine.value).toEqual(line)
    workflow.unapplyLoading.value = false
    workflow.onUnapplyConfirmOpenChanged(false)
    expect(workflow.pendingUnapplyLine.value).toBeNull()

    workflow.highlightedApplyIds.value = ['apply-1', 'apply-2']
    workflow.requestUnapply(line)
    await workflow.confirmUnapply()
    expect(unapplyFactory).toHaveBeenCalledWith('apply-1')
    expect(workflow.highlightedApplyIds.value).toEqual(['apply-2'])
    expect(workflow.unapplyConfirmOpen.value).toBe(false)
    expect(harness.toasts.push).toHaveBeenLastCalledWith(expect.objectContaining({ tone: 'success' }))

    workflow.requestUnapply(line)
    unapplyFactory.mockRejectedValueOnce(new Error('unapply failed'))
    await workflow.confirmUnapply()
    expect(workflow.unapplyError.value).toBe('unapply failed')

    workflow.requestUnapply(line)
    unapplyFactory.mockRejectedValueOnce('offline')
    await workflow.confirmUnapply()
    expect(workflow.unapplyError.value).toBe('offline')
    expect(workflow.unapplyLoading.value).toBe(false)
  })

  it('coordinates wizard opening, route synchronization, and preferred tabs', async () => {
    const harness = createHarness()
    const { workflow, suggestFactory } = harness

    harness.contextReady.value = false
    await workflow.openApplyWizard()
    expect(workflow.applyWizardOpen.value).toBe(false)
    harness.contextReady.value = true
    await workflow.openApplyWizard()
    expect(workflow.applyWizardOpen.value).toBe(true)

    workflow.highlightedApplyIds.value = ['apply-1']
    workflow.syncPreferredTab('credits')
    expect(harness.activeTab.value).toBe('charges')
    workflow.highlightedApplyIds.value = []
    workflow.syncPreferredTab('credits')
    expect(harness.activeTab.value).toBe('credits')
    workflow.syncPreferredTab(null)

    const clearAutoOpen = vi.fn()
    await workflow.syncAfterContextLoad({
      contextChanged: true,
      preferredTab: 'charges',
      autoOpenApply: false,
      clearAutoOpenApplyInRoute: clearAutoOpen,
      currentError: null,
    })
    expect(workflow.applyWizardView.value).toBe('suggest')

    await workflow.syncAfterContextLoad({
      contextChanged: true,
      preferredTab: null,
      autoOpenApply: true,
      clearAutoOpenApplyInRoute: clearAutoOpen,
      currentError: null,
    })
    expect(workflow.applyWizardOpen.value).toBe(true)
    expect(clearAutoOpen).toHaveBeenCalledOnce()

    workflow.applyWizardOpen.value = true
    workflow.applyWizardView.value = 'suggest'
    await workflow.syncAfterContextLoad({
      contextChanged: false,
      preferredTab: null,
      autoOpenApply: false,
      clearAutoOpenApplyInRoute: clearAutoOpen,
      currentError: null,
    })
    expect(suggestFactory).toHaveBeenCalled()

    const callsBeforeBlockedSync = suggestFactory.mock.calls.length
    harness.contextReady.value = false
    await workflow.syncAfterContextLoad({
      contextChanged: false,
      preferredTab: null,
      autoOpenApply: true,
      clearAutoOpenApplyInRoute: clearAutoOpen,
      currentError: 'blocked',
    })
    expect(suggestFactory).toHaveBeenCalledTimes(callsBeforeBlockedSync)

    await workflow.handleWizardOpenChanged(false)
    expect(workflow.applyWizardView.value).toBe('suggest')
    await workflow.handleWizardOpenChanged(true)
    expect(suggestFactory).toHaveBeenCalledTimes(callsBeforeBlockedSync)
    harness.contextReady.value = true
    await workflow.handleWizardOpenChanged(true)
    expect(suggestFactory).toHaveBeenCalledTimes(callsBeforeBlockedSync + 1)

    workflow.applyResult.value = { executedApplies: [] }
    await workflow.showApplyPlanAgain()
    expect(workflow.applyResult.value).toBeNull()
    workflow.showAppliedTab()
    expect(harness.activeTab.value).toBe('applied')
    expect(workflow.applyWizardOpen.value).toBe(false)
  })
})
