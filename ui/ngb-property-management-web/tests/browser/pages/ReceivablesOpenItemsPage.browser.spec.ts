import { computed, defineComponent, h, nextTick, ref, type PropType } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'

const mocks = vi.hoisted(() => ({
  applyBatch: vi.fn(),
  buildLookupTarget: vi.fn(),
  clearQueryKeys: vi.fn(),
  details: vi.fn(),
  documentById: vi.fn(),
  documentPage: vi.fn(),
  suggestFifo: vi.fn(),
  toastPush: vi.fn(),
  unapply: vi.fn(),
  lookupConfig: null as any,
  pagePresentationArgs: null as any,
  routeContextArgs: null as any,
  shellProps: null as any,
  workflowArgs: null as any,
  focusItemId: null as any,
  leaseId: null as any,
  openApply: null as any,
  sourceType: null as any,
  presentation: null as any,
  workflow: null as any,
}))

vi.mock('../../../src/api/clients/receivables', () => ({
  applyReceivablesBatch: mocks.applyBatch,
  getReceivablesOpenItemsDetails: mocks.details,
  suggestLeaseFifoApply: mocks.suggestFifo,
  unapplyReceivablesApply: mocks.unapply,
}))

vi.mock('../../../src/features/open-items/pagePresentation', () => ({
  formatOpenItemsDateCell: (value: unknown) => `date:${String(value)}`,
  formatOpenItemsMoneyCell: (value: unknown) => `money:${String(value)}`,
  useOpenItemsPagePresentation: (args: any) => {
    mocks.pagePresentationArgs = args
    return mocks.presentation
  },
}))

vi.mock('../../../src/features/open-items/useOpenItemsRouteContext', () => ({
  useOpenItemsRouteContext: (args: any) => {
    mocks.routeContextArgs = args
  },
}))

vi.mock('../../../src/features/open-items/workflow', () => ({
  useOpenItemsWorkflow: (args: any) => {
    mocks.workflowArgs = args
    return mocks.workflow
  },
}))

vi.mock('@ngbplatform/ui', async () => {
  const Badge = defineComponent({
    setup(_props, { slots }) {
      return () => h('span', { 'data-testid': 'badge' }, slots.default?.())
    },
  })
  const Button = defineComponent({
    props: { disabled: { type: Boolean, default: false }, loading: { type: Boolean, default: false } },
    emits: ['click'],
    setup(props, { emit, slots }) {
      return () => h('button', {
        type: 'button',
        'aria-disabled': String(props.disabled),
        'data-loading': String(props.loading),
        onClick: () => emit('click'),
      }, slots.default?.())
    },
  })
  const Icon = defineComponent({
    props: { name: { type: String, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  })
  const Grid = defineComponent({
    props: {
      rows: { type: Array as PropType<any[]>, default: () => [] },
      columns: { type: Array as PropType<any[]>, default: () => [] },
      groupBy: { type: Array as PropType<string[]>, default: () => [] },
      selectedKeys: { type: Array as PropType<string[]>, default: () => [] },
      storageKey: { type: String, required: true },
    },
    emits: ['update:selectedKeys'],
    setup(props, { emit }) {
      return () => h('section', { 'data-testid': `grid-${props.storageKey}` }, [
        h('span', `grid-rows:${props.rows.length}`),
        h('span', `grid-columns:${props.columns.length}`),
        h('span', `grid-groups:${props.groupBy.join('|') || 'none'}`),
        h('button', {
          type: 'button',
          onClick: () => emit('update:selectedKeys', props.rows.slice(0, 2).map((entry) => entry.key)),
        }, 'Select first suggestions'),
        h('button', {
          type: 'button',
          onClick: () => emit('update:selectedKeys', ['missing-suggestion']),
        }, 'Select missing suggestion'),
      ])
    },
  })

  return {
    NgbBadge: Badge,
    NgbButton: Button,
    NgbIcon: Icon,
    NgbRegisterGrid: Grid,
    buildLookupFieldTargetUrl: mocks.buildLookupTarget,
    getDocumentById: mocks.documentById,
    getDocumentPage: mocks.documentPage,
    omitRouteQueryKeys: mocks.clearQueryKeys,
    useAllowedQueryValue: () => mocks.sourceType,
    useBooleanQueryFlag: () => mocks.openApply,
    useGuidQueryParam: () => mocks.focusItemId,
    useRouteLookupSelection: (config: any) => {
      mocks.lookupConfig = config
      return {
        selected: ref(null),
        items: ref([]),
        routeId: mocks.leaseId,
        hydrateSelected: vi.fn(async () => undefined),
        onQuery: vi.fn(async () => undefined),
        onSelect: vi.fn(async () => undefined),
        openSelected: vi.fn(async () => undefined),
      }
    },
    useToasts: () => ({ push: mocks.toastPush }),
  }
})

vi.mock('../../../src/features/open-items/OpenItemsWorkflowShell.vue', () => ({
  default: defineComponent({
    props: {
      title: { type: String, required: true },
      contextReady: { type: Boolean, default: false },
      contextBadges: { type: Array as PropType<string[]>, default: () => [] },
      tabs: { type: Array as PropType<any[]>, default: () => [] },
      activeTab: { type: String, required: true },
      chargeGrid: { type: Object, required: true },
      creditGrid: { type: Object, required: true },
      chargePage: { type: Object, default: null },
      creditPage: { type: Object, default: null },
      appliedPage: { type: Object, default: null },
      resolveChargeTypeLabel: { type: Function as PropType<(value: string) => string>, required: true },
      resolveCreditTypeLabel: { type: Function as PropType<(value: string | null) => string>, required: true },
      isContextAllocation: { type: Function as PropType<(value: any) => boolean>, required: true },
      openAppliedDocument: { type: Function as PropType<(type: string, id: string) => Promise<void>>, required: true },
      openApplyDocument: { type: Function as PropType<(id: string) => Promise<void>>, required: true },
      error: { type: String, default: null },
      applyWizardSubtitle: { type: String, default: '' },
      applyWizardActionTitle: { type: String, default: '' },
      unapplyMessage: { type: String, default: '' },
    },
    emits: [
      'back', 'refresh', 'apply', 'dismissPageResult', 'update:activeTab', 'update:applyWizardOpen',
      'page', 'applyWizardAction', 'update:unapplyConfirmOpen', 'confirmUnapply',
    ],
    setup(props, { emit, slots }) {
      mocks.shellProps = props
      return () => h('main', { 'data-testid': 'workflow-shell' }, [
        h('h1', props.title),
        h('span', `context:${String(props.contextReady)}`),
        h('span', `badges:${props.contextBadges.join('|')}`),
        h('span', `tabs:${props.tabs.map((entry: any) => entry.label).join('|')}`),
        h('span', `active:${props.activeTab}`),
        h('span', `error:${props.error ?? 'none'}`),
        h('span', `subtitle:${props.applyWizardSubtitle}`),
        h('span', `action:${props.applyWizardActionTitle}`),
        h('span', `unapply:${props.unapplyMessage}`),
        h('button', { type: 'button', onClick: () => emit('back') }, 'Shell back'),
        h('button', { type: 'button', onClick: () => emit('refresh') }, 'Shell refresh'),
        h('button', { type: 'button', onClick: () => emit('apply') }, 'Shell apply'),
        h('button', { type: 'button', onClick: () => emit('dismissPageResult') }, 'Dismiss result'),
        h('button', { type: 'button', onClick: () => emit('update:activeTab', 'credits') }, 'Select credits'),
        h('button', { type: 'button', onClick: () => emit('page', { tab: 'charges', offset: 100 }) }, 'Next charge page'),
        h('button', { type: 'button', onClick: () => emit('update:applyWizardOpen', true) }, 'Open wizard'),
        h('button', { type: 'button', onClick: () => emit('applyWizardAction') }, 'Wizard action'),
        h('button', { type: 'button', onClick: () => emit('update:unapplyConfirmOpen', false) }, 'Close unapply'),
        h('button', { type: 'button', onClick: () => emit('confirmUnapply') }, 'Confirm unapply'),
        slots.drawer?.(),
        slots.footer?.(),
        slots.unapply?.(),
      ])
    },
  }),
}))

import ReceivablesOpenItemsPage from '../../../src/pages/ReceivablesOpenItemsPage.vue'

const AppRoot = defineComponent({ setup: () => () => h(RouterView) })

function detailsData() {
  return {
    registerId: 'register-1', leaseId: 'lease-1', leaseDisplay: 'Lease 101', partyId: 'party-1', partyDisplay: 'Tenant One', propertyId: 'property-1', propertyDisplay: 'Property One',
    totalOutstanding: 130, totalCredit: 80,
    charges: [
      { chargeDocumentId: 'charge-rent', documentType: 'pm.rent_charge', number: 'RC-1', chargeDisplay: 'Rent', dueOnUtc: '2026-01-10', chargeTypeDisplay: 'Rent', memo: 'January', originalAmount: 100, outstandingAmount: 70 },
      { chargeDocumentId: 'charge-late', documentType: 'pm.late_fee_charge', number: null, chargeDisplay: null, dueOnUtc: '2026-01-11', chargeTypeDisplay: null, memo: null, originalAmount: 30, outstandingAmount: 30 },
    ],
    credits: [
      { creditDocumentId: 'credit-payment', documentType: 'pm.receivable_payment', number: 'PAY-1', creditDocumentDisplay: 'Payment', receivedOnUtc: '2026-01-05', memo: 'Cash', originalAmount: 50, availableCredit: 40 },
      { creditDocumentId: 'credit-memo', documentType: 'pm.receivable_credit_memo', number: null, creditDocumentDisplay: null, receivedOnUtc: '2026-01-06', memo: null, originalAmount: 40, availableCredit: 40 },
    ],
    allocations: [
      { applyId: 'apply-1', creditDocumentId: 'credit-allocation', creditDocumentType: 'pm.receivable_credit_memo', chargeDocumentId: 'charge-rent', appliedOnUtc: '2026-01-07', amount: 10, isPosted: true },
    ],
  }
}

function item(overrides: Record<string, unknown> = {}) {
  return {
    applyId: null,
    creditDocumentId: 'credit-payment', creditDocumentType: 'pm.receivable_payment', creditDocumentDisplay: 'PAY-1', creditDocumentDateUtc: '2026-01-05',
    creditAmountBefore: 40, creditAmountAfter: 10,
    chargeDocumentId: 'charge-rent', chargeDisplay: 'RC-1', chargeDueOnUtc: '2026-01-10', chargeOutstandingBefore: 70, chargeOutstandingAfter: 40,
    amount: 30, applyPayload: { fields: {} },
    ...overrides,
  }
}

function suggestion(items: any[] = []) {
  return {
    registerId: 'register-1', leaseId: 'lease-1', leaseDisplay: 'Lease 101', partyId: 'party-1', partyDisplay: 'Suggested Tenant', propertyId: 'property-1', propertyDisplay: 'Suggested Property',
    totalOutstanding: 130, totalCredit: 80, totalApplied: 30, remainingOutstanding: 100, remainingCredit: 50,
    suggestedApplies: items,
    warnings: [],
  }
}

function workflowState() {
  return {
    applyWizardOpen: ref(false), applyWizardView: ref<'suggest' | 'result'>('suggest'), suggestLoading: ref(false), suggestError: ref<string | null>(null), suggestData: ref<any>(null),
    applyExecLoading: ref(false), applyExecError: ref<string | null>(null), applyResult: ref<any>(null), unapplyLoading: ref(false), unapplyError: ref<string | null>(null),
    unapplyConfirmOpen: ref(false), pendingUnapplyLine: ref<any>(null), highlightedApplyIds: ref<string[]>([]), applyResultLines: ref<any[]>([]), pageResult: ref<any>(null), appliedAllocations: ref<any[]>([]),
    canExecuteApply: computed(() => true), previewAfterOutstanding: computed(() => 100), previewAfterCredit: computed(() => 50),
    suggest: vi.fn(), openApplyWizard: vi.fn(), requestUnapply: vi.fn(), onUnapplyConfirmOpenChanged: vi.fn(), confirmUnapply: vi.fn(), showApplyPlanAgain: vi.fn(),
    executeApplyBatch: vi.fn(), dismissPageApplyResult: vi.fn(), showAppliedTab: vi.fn(), syncPreferredTab: vi.fn(), syncAfterContextLoad: vi.fn(), handleWizardOpenChanged: vi.fn(),
    applyResultActionLabel: computed(() => 'Apply more'), applyResultTitle: computed(() => 'Applied successfully'), applyResultSubtitle: computed(() => 'Posted allocations'),
  }
}

async function flushUi() {
  await nextTick()
  await new Promise((resolvePromise) => window.setTimeout(resolvePromise, 50))
}

async function renderPage() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/receivables/open-items', component: ReceivablesOpenItemsPage },
      { path: '/documents/:type/:id', component: defineComponent({ setup: () => () => h('div', { 'data-testid': 'document-target' }, 'Document') }) },
    ],
  })
  await router.push('/receivables/open-items')
  await router.isReady()
  const view = await render(AppRoot, { global: { plugins: [router] } })
  await flushUi()
  return { router, view }
}

beforeEach(() => {
  for (const fn of [mocks.applyBatch, mocks.buildLookupTarget, mocks.clearQueryKeys, mocks.details, mocks.documentById, mocks.documentPage, mocks.suggestFifo, mocks.toastPush, mocks.unapply]) fn.mockReset()
  mocks.leaseId = ref<string | null>('lease-1')
  mocks.focusItemId = ref<string | null>(null)
  mocks.openApply = ref(false)
  mocks.sourceType = ref<string | null>(null)
  mocks.presentation = {
    summary: computed(() => ({ totalOutstanding: 130, totalCredit: 80, chargesCount: 2, creditsCount: 2, allocationsCount: 1 })),
    focusedCharge: ref<any>(null), focusedCredit: ref<any>(null), focusedContextBadge: computed(() => null), preferredTabFromRoute: computed(() => null),
  }
  mocks.workflow = workflowState()
  mocks.details.mockResolvedValue(detailsData())
  mocks.documentById.mockResolvedValue({ display: 'Lease Resolved' })
  mocks.documentPage.mockResolvedValue({ items: [] })
  mocks.buildLookupTarget.mockResolvedValue('/lookup-target')
  mocks.suggestFifo.mockResolvedValue(suggestion())
  mocks.applyBatch.mockResolvedValue({ registerId: 'register-1', totalApplied: 30, executedApplies: [] })
  mocks.unapply.mockResolvedValue({ applyId: 'apply-1' })
})

test('loads lease context, projects rows, labels all document types, and resolves navigation precedence', async () => {
  const { router, view } = await renderPage()
  const emptyChargeGrid = mocks.shellProps.chargeGrid
  const emptyCreditGrid = mocks.shellProps.creditGrid
  await emptyChargeGrid.onActivate('empty-charge')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.receivable_charge/empty-charge')
  await router.push('/receivables/open-items')
  await emptyCreditGrid.onActivate('empty-credit')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.receivable_payment/empty-credit')
  await router.push('/receivables/open-items')
  await mocks.routeContextArgs.load()
  await flushUi()
  expect(mocks.details).toHaveBeenCalledWith(
    { leaseId: 'lease-1', chargeOffset: 0, creditOffset: 0, allocationOffset: 0, limit: 100 },
    { signal: expect.any(AbortSignal) },
  )
  await expect.element(view.getByText('badges:Tenant: Tenant One|Property: Property One|Lease: Lease 101')).toBeVisible()
  expect(mocks.shellProps.chargeGrid.rows[1]).toMatchObject({ chargeType: '—', memo: '' })
  expect(mocks.shellProps.creditGrid.rows[1]).toMatchObject({ creditType: 'Credit Memo', memo: '' })
  expect(mocks.shellProps.resolveChargeTypeLabel('pm.rent_charge')).toBe('Rent')
  expect(mocks.shellProps.resolveChargeTypeLabel('pm.late_fee_charge')).toBe('Late Fee')
  expect(mocks.shellProps.resolveChargeTypeLabel('other')).toBe('Charge')
  expect(mocks.shellProps.resolveCreditTypeLabel('pm.receivable_payment')).toBe('Payment')
  expect(mocks.shellProps.resolveCreditTypeLabel('pm.receivable_credit_memo')).toBe('Credit Memo')
  expect(mocks.shellProps.resolveCreditTypeLabel(null)).toBe('Credit Source')

  const chargeGrid = mocks.shellProps.chargeGrid
  const creditGrid = mocks.shellProps.creditGrid
  await chargeGrid.onActivate('charge-rent')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.rent_charge/charge-rent')
  await router.push('/receivables/open-items')
  await chargeGrid.onActivate('missing')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.receivable_charge/missing')
  await router.push('/receivables/open-items')
  await creditGrid.onActivate('credit-allocation')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.receivable_credit_memo/credit-allocation')
  await router.push('/receivables/open-items')
  await creditGrid.onActivate('credit-payment')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.receivable_payment/credit-payment')
  await router.push('/receivables/open-items')
  await creditGrid.onActivate('missing')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.receivable_payment/missing')
})

test('covers lookup and presentation adapters plus context badges without a lease display', async () => {
  mocks.documentById.mockResolvedValueOnce({ display: 'Lease Resolved' }).mockResolvedValueOnce({ display: null })
  mocks.documentPage
    .mockResolvedValueOnce({ items: [{ id: 'lease-2', display: 'Lease Two' }, { id: 'lease-3', display: null }] })
    .mockResolvedValueOnce({})
  const { view } = await renderPage()
  await expect(mocks.lookupConfig.lookupById('lease-1')).resolves.toBe('Lease Resolved')
  await expect(mocks.lookupConfig.lookupById('lease-fallback')).resolves.toBe('lease-fallback')
  await expect(mocks.lookupConfig.search('lease')).resolves.toEqual([{ id: 'lease-2', label: 'Lease Two' }, { id: 'lease-3', label: 'lease-3' }])
  await expect(mocks.lookupConfig.search('none')).resolves.toEqual([])
  await expect(mocks.lookupConfig.openTarget({ id: 'lease-1', label: 'Lease' })).resolves.toBe('/lookup-target')

  const presentation = mocks.pagePresentationArgs
  expect(presentation.resolveTabFromSourceType('pm.receivable_payment')).toBe('credits')
  expect(presentation.resolveTabFromSourceType('pm.receivable_credit_memo')).toBe('credits')
  expect(presentation.resolveTabFromSourceType('pm.rent_charge')).toBe('charges')
  expect(presentation.resolveTabFromSourceType('unknown')).toBeNull()
  expect(presentation.resolveTabFromSourceType(null)).toBeNull()
  expect(presentation.buildFocusedChargeBadge(detailsData().charges[0])).toContain('Charge')
  expect(presentation.buildFocusedCreditBadge(detailsData().credits[0])).toContain('Payment')

  mocks.details.mockResolvedValueOnce({ ...detailsData(), leaseDisplay: null, partyDisplay: null, propertyDisplay: null })
  await mocks.routeContextArgs.load()
  await flushUi()
  await expect.element(view.getByText('badges:Tenant: —|Property: —')).toBeVisible()
})

test('handles missing context, load/refresh failures, workflow factories, and allocation matching', async () => {
  const { view } = await renderPage()
  mocks.leaseId.value = null
  await mocks.routeContextArgs.load()
  expect(mocks.details).not.toHaveBeenCalled()
  await expect(mocks.workflowArgs.suggestFactory()).rejects.toThrow('Select a lease first.')

  mocks.leaseId.value = 'lease-1'
  mocks.details.mockRejectedValueOnce(new Error('Details unavailable'))
  await mocks.routeContextArgs.load()
  await flushUi()
  await expect.element(view.getByText('error:Details unavailable')).toBeVisible()
  mocks.details.mockRejectedValueOnce('Gateway offline')
  await view.getByRole('button', { name: 'Shell refresh' }).click()
  await flushUi()
  await expect.element(view.getByText('error:Gateway offline')).toBeVisible()
  mocks.details.mockResolvedValueOnce(detailsData())
  await view.getByRole('button', { name: 'Shell refresh' }).click()
  await flushUi()
  expect(mocks.toastPush).toHaveBeenCalledWith(expect.objectContaining({ title: 'Refreshed' }))

  await mocks.workflowArgs.suggestFactory()
  expect(mocks.suggestFifo).toHaveBeenCalledWith({ leaseId: 'lease-1', createDrafts: false, limit: 500 })
  await mocks.workflowArgs.executeFactory(suggestion([item({ applyId: 'apply-1' }), item({ applyId: undefined })]))
  expect(mocks.applyBatch).toHaveBeenCalledWith({ applies: [{ applyId: 'apply-1', applyPayload: { fields: {} } }, { applyId: null, applyPayload: { fields: {} } }] })
  await mocks.workflowArgs.executeFactory({ ...suggestion(), suggestedApplies: undefined } as never)
  expect(mocks.applyBatch).toHaveBeenLastCalledWith({ applies: [] })
  await mocks.workflowArgs.unapplyFactory('apply-1')
  expect(mocks.workflowArgs.buildUnapplySuccessMessage({ creditLabel: 'PAY', chargeLabel: 'RC', amount: 12.5 })).toContain('PAY was unapplied')
  expect(mocks.workflowArgs.buildExecuteSuccessMessage({ totalApplied: null })).toContain('0.00')

  const allocation = { chargeDocumentId: 'charge-rent', creditDocumentId: 'credit-payment' }
  mocks.presentation.focusedCharge.value = { chargeDocumentId: 'charge-rent' }
  expect(mocks.workflowArgs.allocationMatchesContext(allocation)).toBe(true)
  mocks.presentation.focusedCharge.value = null
  mocks.presentation.focusedCredit.value = { creditDocumentId: 'credit-payment' }
  expect(mocks.workflowArgs.allocationMatchesContext(allocation)).toBe(true)
  mocks.presentation.focusedCredit.value = null
  expect(mocks.workflowArgs.allocationMatchesContext(allocation)).toBe(false)
})

test('synchronizes route callbacks, watchers, and every shell event', async () => {
  const { router, view } = await renderPage()
  mocks.openApply.value = true
  expect(mocks.routeContextArgs.source()).toEqual(['lease-1', true])
  expect(mocks.routeContextArgs.autoOpenApply(['lease-1', true])).toBe(true)
  mocks.routeContextArgs.clearAutoOpenApplyInRoute()
  expect(mocks.clearQueryKeys).toHaveBeenCalledWith(expect.anything(), expect.anything(), ['openApply', 'source'])

  const backSpy = vi.spyOn(router, 'back').mockImplementation(() => {})
  await view.getByRole('button', { name: 'Shell back' }).click()
  await view.getByRole('button', { name: 'Shell apply' }).click()
  await view.getByRole('button', { name: 'Dismiss result' }).click()
  await view.getByRole('button', { name: 'Select credits' }).click()
  await view.getByRole('button', { name: 'Next charge page' }).click()
  await flushUi()
  await view.getByRole('button', { name: 'Open wizard' }).click()
  await flushUi()
  expect(mocks.workflow.handleWizardOpenChanged).toHaveBeenCalledWith(true)
  await view.getByRole('button', { name: 'Wizard action' }).click()
  await view.getByRole('button', { name: 'Close unapply' }).click()
  await view.getByRole('button', { name: 'Confirm unapply' }).click()
  expect(backSpy).toHaveBeenCalledOnce()
  expect(mocks.workflow.openApplyWizard).toHaveBeenCalledOnce()
  expect(mocks.workflow.dismissPageApplyResult).toHaveBeenCalledOnce()
  expect(mocks.workflow.suggest).toHaveBeenCalledOnce()
  expect(mocks.workflow.onUnapplyConfirmOpenChanged).toHaveBeenCalledWith(false)
  expect(mocks.workflow.confirmUnapply).toHaveBeenCalledOnce()
  expect(mocks.details).toHaveBeenLastCalledWith(
    { leaseId: 'lease-1', chargeOffset: 100, creditOffset: 0, allocationOffset: 0, limit: 100 },
    { signal: expect.any(AbortSignal) },
  )
  mocks.focusItemId.value = 'focus'
  mocks.sourceType.value = 'pm.receivable_payment'
  await flushUi()
  expect(mocks.workflow.syncPreferredTab).toHaveBeenCalled()
})

test('covers selection fallback, grouping, all warnings, loading/errors, and result actions', async () => {
  const { router, view } = await renderPage()
  const items = [
    item({ applyId: 'apply-a', creditDocumentId: 'payment-a', chargeDocumentId: 'charge-a' }),
    item({ applyId: null, creditDocumentId: 'payment-b', chargeDocumentId: 'charge-b' }),
    item({ applyId: null, creditDocumentId: 'payment-a', chargeDocumentId: 'charge-c' }),
  ]
  mocks.workflow.suggestData.value = suggestion(items)
  mocks.workflow.suggestData.value.warnings = [
    { code: 'no_charges', message: 'raw' }, { code: 'no_credits', message: 'raw' }, { code: 'limit_reached', message: 'raw' },
    { code: 'outstanding_remaining', message: 'Outstanding charges remain 20' }, { code: 'credit_remaining', message: 'Unapplied credits remain 10' },
    { code: 'custom', message: 'Custom warning' },
  ]
  await flushUi()
  await expect.element(view.getByText('grid-groups:creditSource')).toBeVisible()
  await expect.element(view.getByText('No open charges')).toBeVisible()
  await expect.element(view.getByText('Custom warning')).toBeVisible()
  await view.getByRole('button', { name: 'Select first suggestions' }).click()
  await flushUi()
  await expect.element(view.getByText('Selected 2 suggested applies')).toBeVisible()
  await view.getByRole('button', { name: 'Select missing suggestion' }).click()
  await flushUi()
  await expect.element(view.getByText('PAY-1 → RC-1')).toBeVisible()

  mocks.workflow.suggestError.value = 'Suggest failed'
  mocks.workflow.applyExecError.value = 'Apply failed'
  mocks.workflow.suggestLoading.value = true
  await flushUi()
  await expect.element(view.getByText('Suggesting FIFO allocations…')).toBeVisible()
  mocks.workflow.suggestLoading.value = false
  mocks.workflow.suggestData.value = suggestion([])
  await flushUi()
  await expect.element(view.getByText('Nothing to apply. There are no matching credit sources and outstanding charges.')).toBeVisible()
  await view.getByRole('button', { name: 'Close', exact: true }).click()
  expect(mocks.workflow.applyWizardOpen.value).toBe(false)

  await mocks.routeContextArgs.load()
  mocks.workflow.applyWizardView.value = 'result'
  mocks.workflow.applyResult.value = { totalApplied: 30, executedApplies: [{ applyId: 'apply-1' }] }
  mocks.workflow.applyResultLines.value = [{ key: 'line-1', applyId: 'apply-1', creditLabel: 'PAY-1', chargeLabel: 'RC-1', appliedOnUtc: '2026-01-15', amount: 30 }]
  mocks.workflow.unapplyError.value = 'Unapply failed'
  mocks.workflow.pendingUnapplyLine.value = { creditDocumentType: 'pm.receivable_payment', creditLabel: 'PAY-1', chargeLabel: 'RC-1', amount: 30 }
  await flushUi()
  await expect.element(view.getByText('Applied successfully')).toBeVisible()
  await expect.element(view.getByText('Lease: Lease 101', { exact: true })).toBeVisible()
  await expect.element(view.getByText('unapply:Unapply payment PAY-1 from charge RC-1 for 30.00?')).toBeVisible()
  await expect.element(view.getByText('Unapply failed', { exact: true })).toBeVisible()
  await view.getByRole('button', { name: 'Wizard action' }).click()
  expect(mocks.workflow.showApplyPlanAgain).toHaveBeenCalledOnce()
  await view.getByRole('button', { name: 'Close', exact: true }).click()
  expect(mocks.workflow.applyWizardOpen.value).toBe(false)
  await view.getByTitle('Unapply').click()
  expect(mocks.workflow.requestUnapply).toHaveBeenCalledOnce()
  await view.getByRole('button', { name: 'Open Apply' }).click()
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.receivable_apply/apply-1')
  await router.push('/receivables/open-items')
  mocks.workflow.applyWizardView.value = 'result'
  mocks.workflow.applyResult.value = { totalApplied: null, executedApplies: undefined }
  mocks.workflow.applyResultLines.value = []
  await flushUi()
  await expect.element(view.getByText('There are no active applied allocations left in this result set.')).toBeVisible()
  await view.getByRole('button', { name: 'Show Applied' }).click()
  await view.getByRole('button', { name: 'Apply more' }).click()
  expect(mocks.workflow.showAppliedTab).toHaveBeenCalledOnce()
  expect(mocks.workflow.showApplyPlanAgain).toHaveBeenCalledTimes(2)
})
