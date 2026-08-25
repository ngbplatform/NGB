import { computed, defineComponent, h, nextTick, ref, type PropType } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'

const mocks = vi.hoisted(() => ({
  applyBatch: vi.fn(),
  buildLookupTarget: vi.fn(),
  catalogById: vi.fn(),
  catalogPage: vi.fn(),
  clearQueryKeys: vi.fn(),
  details: vi.fn(),
  markNeedsRefresh: vi.fn(),
  suggestFifo: vi.fn(),
  toastPush: vi.fn(),
  unapply: vi.fn(),
  lookupConfigs: [] as any[],
  navigationArgs: null as any,
  pagePresentationArgs: null as any,
  routeContextArgs: null as any,
  shellProps: null as any,
  workflowArgs: null as any,
  activeTab: null as any,
  focusItemId: null as any,
  openApply: null as any,
  partyId: null as any,
  propertyId: null as any,
  refreshFlag: null as any,
  sourceType: null as any,
  presentation: null as any,
  workflow: null as any,
}))

vi.mock('../../../src/api/clients/payables', () => ({
  applyPayablesBatch: mocks.applyBatch,
  getPayablesOpenItemsDetails: mocks.details,
  suggestPayablesFifoApply: mocks.suggestFifo,
  unapplyPayablesApply: mocks.unapply,
}))

vi.mock('../../../src/features/open-items/pagePresentation', () => ({
  formatOpenItemsDateCell: (value: unknown) => `date:${String(value)}`,
  formatOpenItemsMoneyCell: (value: unknown) => `money:${String(value)}`,
  useOpenItemsPagePresentation: (args: any) => {
    mocks.pagePresentationArgs = args
    return mocks.presentation
  },
}))

vi.mock('../../../src/features/open-items/useOpenItemsNavigationRefresh', () => ({
  useOpenItemsNavigationRefresh: (args: any) => {
    mocks.navigationArgs = args
    return { markNeedsRefresh: mocks.markNeedsRefresh }
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
    props: { tone: { type: String, default: 'neutral' } },
    setup(props, { slots }) {
      return () => h('span', { 'data-testid': `badge-${props.tone}` }, slots.default?.())
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
  const Grid = defineComponent({
    props: {
      rows: { type: Array as PropType<any[]>, default: () => [] },
      columns: { type: Array as PropType<any[]>, default: () => [] },
      storageKey: { type: String, required: true },
    },
    setup(props) {
      return () => h('section', { 'data-testid': `grid-${props.storageKey}` }, [
        h('span', `grid-rows:${props.rows.length}`),
        h('span', `grid-columns:${props.columns.length}`),
      ])
    },
  })

  return {
    NgbBadge: Badge,
    NgbButton: Button,
    NgbRegisterGrid: Grid,
    buildLookupFieldTargetUrl: mocks.buildLookupTarget,
    getCatalogById: mocks.catalogById,
    getCatalogPage: mocks.catalogPage,
    omitRouteQueryKeys: mocks.clearQueryKeys,
    useAllowedQueryValue: () => mocks.sourceType,
    useBooleanQueryFlag: (_route: unknown, key: string) => key === 'openApply' ? mocks.openApply : mocks.refreshFlag,
    useGuidQueryParam: () => mocks.focusItemId,
    useRouteLookupSelection: (config: any) => {
      const index = mocks.lookupConfigs.length
      mocks.lookupConfigs.push(config)
      return {
        selected: ref(null),
        items: ref([]),
        routeId: index === 0 ? mocks.partyId : mocks.propertyId,
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
      lookups: { type: Array as PropType<any[]>, default: () => [] },
      loading: { type: Boolean, default: false },
      error: { type: String, default: null },
      contextReady: { type: Boolean, default: false },
      contextBadges: { type: Array as PropType<string[]>, default: () => [] },
      summary: { type: Object, required: true },
      pageResult: { type: Object, default: null },
      tabs: { type: Array as PropType<any[]>, default: () => [] },
      activeTab: { type: String, required: true },
      chargeGrid: { type: Object, required: true },
      creditGrid: { type: Object, required: true },
      appliedRows: { type: Array as PropType<any[]>, default: () => [] },
      applyWizardOpen: { type: Boolean, default: false },
      applyWizardSubtitle: { type: String, default: '' },
      applyWizardActionTitle: { type: String, default: '' },
      unapplyMessage: { type: String, default: '' },
      resolveChargeTypeLabel: { type: Function as PropType<(value: string) => string>, required: true },
      resolveCreditTypeLabel: { type: Function as PropType<(value: string | null) => string>, required: true },
      isContextAllocation: { type: Function as PropType<(value: any) => boolean>, required: true },
      openAppliedDocument: { type: Function as PropType<(type: string, id: string) => Promise<void>>, required: true },
      openApplyDocument: { type: Function as PropType<(id: string) => Promise<void>>, required: true },
    },
    emits: [
      'back', 'refresh', 'apply', 'dismissPageResult', 'update:activeTab', 'update:applyWizardOpen',
      'applyWizardAction', 'update:unapplyConfirmOpen', 'confirmUnapply',
    ],
    setup(props, { attrs, emit, slots }) {
      mocks.shellProps = props
      return () => h('main', { 'data-testid': 'workflow-shell' }, [
        h('h1', props.title),
        h('span', `context:${String(props.contextReady)}`),
        h('span', `badges:${props.contextBadges.join('|')}`),
        h('span', `tabs:${props.tabs.map((tab: any) => tab.label).join('|')}`),
        h('span', `active:${props.activeTab}`),
        h('span', `subtitle:${props.applyWizardSubtitle}`),
        h('span', `action:${props.applyWizardActionTitle}`),
        h('span', `unapply:${props.unapplyMessage}`),
        h('span', `error:${props.error ?? 'none'}`),
        h('button', { type: 'button', onClick: () => emit('back') }, 'Shell back'),
        h('button', { type: 'button', onClick: () => emit('refresh') }, 'Shell refresh'),
        h('button', { type: 'button', onClick: () => emit('apply') }, 'Shell apply'),
        h('button', { type: 'button', onClick: () => emit('dismissPageResult') }, 'Dismiss result'),
        h('button', { type: 'button', onClick: () => emit('update:activeTab', 'credits') }, 'Select credits'),
        h('button', { type: 'button', onClick: () => emit('update:applyWizardOpen', true) }, 'Open wizard'),
        h('button', { type: 'button', onClick: () => emit('applyWizardAction') }, 'Wizard action'),
        h('button', { type: 'button', onClick: () => emit('update:unapplyConfirmOpen', false) }, 'Close unapply'),
        h('button', { type: 'button', onClick: () => emit('confirmUnapply') }, 'Confirm unapply'),
        h('span', `extra:${Object.keys(attrs).length}`),
        slots.drawer?.(),
        slots.footer?.(),
        slots.unapply?.(),
      ])
    },
  }),
}))

import PayablesOpenItemsPage from '../../../src/pages/PayablesOpenItemsPage.vue'

const AppRoot = defineComponent({ setup: () => () => h(RouterView) })

function openItemsData() {
  return {
    registerId: 'register-1',
    vendorId: 'vendor-1',
    vendorDisplay: 'Vendor One',
    propertyId: 'property-1',
    propertyDisplay: 'Property One',
    totalOutstanding: 150,
    totalCredit: 80,
    charges: [
      {
        chargeDocumentId: 'charge-1', documentType: 'pm.payable_charge', number: 'CH-1', chargeDisplay: 'Charge One', dueOnUtc: '2026-01-10',
        chargeTypeDisplay: 'Invoice', vendorInvoiceNo: 'VIN-1', memo: 'Memo', originalAmount: 100, outstandingAmount: 70,
      },
      {
        chargeDocumentId: 'charge-2', documentType: '', number: null, chargeDisplay: null, dueOnUtc: '2026-01-11',
        chargeTypeDisplay: null, vendorInvoiceNo: null, memo: null, originalAmount: 50, outstandingAmount: 50,
      },
    ],
    credits: [
      {
        creditDocumentId: 'credit-1', documentType: 'pm.payable_credit_memo', number: 'CM-1', creditDocumentDisplay: 'Credit One', creditDocumentDateUtc: '2026-01-05',
        memo: 'Credit memo', originalAmount: 50, availableCredit: 40,
      },
      {
        creditDocumentId: 'credit-2', documentType: '', number: null, creditDocumentDisplay: null, creditDocumentDateUtc: '2026-01-06',
        memo: null, originalAmount: 40, availableCredit: 40,
      },
    ],
    allocations: [],
  }
}

function suggestion(items: any[] = []) {
  return {
    registerId: 'register-1', vendorId: 'vendor-1', vendorDisplay: 'Suggested Vendor', propertyId: 'property-1', propertyDisplay: 'Suggested Property',
    totalOutstanding: 150, totalCredit: 80, totalApplied: 30, remainingOutstanding: 120, remainingCredit: 50,
    suggestedApplies: items,
    warnings: [],
  }
}

function suggestedItem(overrides: Record<string, unknown> = {}) {
  return {
    applyId: null,
    creditDocumentId: 'credit-1', creditDocumentType: 'pm.payable_credit_memo', creditDocumentDisplay: 'CM-1', creditDocumentDateUtc: '2026-01-05',
    creditAmountBefore: 40, creditAmountAfter: 10,
    chargeDocumentId: 'charge-1', chargeDisplay: 'CH-1', chargeDueOnUtc: '2026-01-10', chargeOutstandingBefore: 70, chargeOutstandingAfter: 40,
    amount: 30, applyPayload: { fields: {} },
    ...overrides,
  }
}

function createWorkflowState() {
  return {
    applyWizardOpen: ref(false),
    applyWizardView: ref<'suggest' | 'result'>('suggest'),
    suggestLoading: ref(false),
    suggestError: ref<string | null>(null),
    suggestData: ref<any>(null),
    applyExecLoading: ref(false),
    applyExecError: ref<string | null>(null),
    applyResult: ref<any>(null),
    unapplyLoading: ref(false),
    unapplyError: ref<string | null>(null),
    unapplyConfirmOpen: ref(false),
    pendingUnapplyLine: ref<any>(null),
    highlightedApplyIds: computed(() => []),
    applyResultLines: ref<any[]>([]),
    pageResult: computed(() => null),
    appliedAllocations: computed(() => []),
    canExecuteApply: computed(() => true),
    previewAfterOutstanding: computed(() => 120),
    previewAfterCredit: computed(() => 50),
    suggest: vi.fn(async () => undefined),
    openApplyWizard: vi.fn(),
    requestUnapply: vi.fn(),
    onUnapplyConfirmOpenChanged: vi.fn(),
    confirmUnapply: vi.fn(async () => undefined),
    showApplyPlanAgain: vi.fn(),
    executeApplyBatch: vi.fn(async () => undefined),
    dismissPageApplyResult: vi.fn(),
    syncPreferredTab: vi.fn(),
    syncAfterContextLoad: vi.fn(),
    handleWizardOpenChanged: vi.fn(async () => undefined),
    applyResultActionLabel: computed(() => 'View plan'),
    applyResultTitle: computed(() => 'Applied successfully'),
    applyResultSubtitle: computed(() => 'One allocation posted'),
  }
}

async function flushUi() {
  await nextTick()
  await new Promise((resolvePromise) => window.setTimeout(resolvePromise, 50))
}

async function renderPage(url = '/payables/open-items') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/payables/open-items', component: PayablesOpenItemsPage },
      { path: '/documents/:type/:id', component: defineComponent({ setup: () => () => h('div', { 'data-testid': 'document-target' }, 'Document') }) },
    ],
  })
  await router.push(url)
  await router.isReady()
  const view = await render(AppRoot, { global: { plugins: [router] } })
  await flushUi()
  return { router, view }
}

beforeEach(() => {
  mocks.applyBatch.mockReset()
  mocks.buildLookupTarget.mockReset()
  mocks.catalogById.mockReset()
  mocks.catalogPage.mockReset()
  mocks.clearQueryKeys.mockReset()
  mocks.details.mockReset()
  mocks.markNeedsRefresh.mockReset()
  mocks.suggestFifo.mockReset()
  mocks.toastPush.mockReset()
  mocks.unapply.mockReset()
  mocks.lookupConfigs.length = 0
  mocks.partyId = ref<string | null>('vendor-1')
  mocks.propertyId = ref<string | null>('property-1')
  mocks.focusItemId = ref<string | null>(null)
  mocks.openApply = ref(false)
  mocks.refreshFlag = ref(false)
  mocks.sourceType = ref<string | null>(null)
  mocks.presentation = {
    summary: computed(() => ({ totalOutstanding: 150, totalCredit: 80, chargesCount: 2, creditsCount: 2, allocationsCount: 0 })),
    focusedCharge: ref<any>(null),
    focusedCredit: ref<any>(null),
    focusedContextBadge: computed(() => null),
    preferredTabFromRoute: computed(() => null),
  }
  mocks.workflow = createWorkflowState()
  mocks.details.mockResolvedValue(openItemsData())
  mocks.catalogById.mockResolvedValue({ display: 'Resolved' })
  mocks.catalogPage.mockResolvedValue({ items: [] })
  mocks.buildLookupTarget.mockResolvedValue('/lookup-target')
  mocks.suggestFifo.mockResolvedValue(suggestion())
  mocks.applyBatch.mockResolvedValue({ registerId: 'register-1', totalApplied: 30, executedApplies: [] })
  mocks.unapply.mockResolvedValue({ applyId: 'apply-1' })
})

test('loads context, projects lookup/grid rows, resolves document types, and navigates', async () => {
  const { router, view } = await renderPage()
  await mocks.routeContextArgs.load()
  await flushUi()

  expect(mocks.details).toHaveBeenCalledWith({ partyId: 'vendor-1', propertyId: 'property-1' })
  await expect.element(view.getByText('badges:Vendor: Vendor One|Property: Property One')).toBeVisible()
  expect(mocks.shellProps.chargeGrid.rows).toHaveLength(2)
  expect(mocks.shellProps.chargeGrid.rows[1]).toMatchObject({ chargeType: '—', vendorInvoiceNo: '—', memo: '' })
  expect(mocks.shellProps.creditGrid.rows[1]).toMatchObject({ creditType: 'Payment', memo: '' })
  expect(mocks.shellProps.chargeGrid.columns).toHaveLength(7)
  expect(mocks.shellProps.creditGrid.columns).toHaveLength(6)
  const chargeGrid = mocks.shellProps.chargeGrid
  const creditGrid = mocks.shellProps.creditGrid

  await chargeGrid.onActivate('charge-1')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.payable_charge/charge-1')
  await router.push('/payables/open-items')
  await chargeGrid.onActivate('missing-charge')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.payable_charge/missing-charge')
  await router.push('/payables/open-items')
  await creditGrid.onActivate('credit-1')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.payable_credit_memo/credit-1')
  await router.push('/payables/open-items')
  await creditGrid.onActivate('missing-credit')
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.payable_payment/missing-credit')
  expect(mocks.markNeedsRefresh).toHaveBeenCalledTimes(4)
})

test('executes lookup adapters and every presentation callback boundary', async () => {
  mocks.catalogById
    .mockResolvedValueOnce({ display: 'Vendor Resolved' })
    .mockResolvedValueOnce({ display: null })
    .mockResolvedValueOnce({ display: null })
  mocks.catalogPage
    .mockResolvedValueOnce({ items: [{ id: 'vendor-2', display: 'Vendor Two' }, { id: 'vendor-3', display: null }] })
    .mockResolvedValueOnce({ items: [{ id: 'property-2', display: 'Property Two' }, { id: 'property-3', display: null }] })
    .mockResolvedValueOnce({})
    .mockResolvedValueOnce({})
  await renderPage()

  const [vendorConfig, propertyConfig] = mocks.lookupConfigs
  await expect(vendorConfig.lookupById('vendor-1')).resolves.toBe('Vendor Resolved')
  await expect(propertyConfig.lookupById('property-1')).resolves.toBe('property-1')
  await expect(vendorConfig.lookupById('vendor-fallback')).resolves.toBe('vendor-fallback')
  await expect(vendorConfig.search('ven')).resolves.toEqual([
    { id: 'vendor-2', label: 'Vendor Two' },
    { id: 'vendor-3', label: 'vendor-3' },
  ])
  await expect(propertyConfig.search('prop')).resolves.toEqual([
    { id: 'property-2', label: 'Property Two' },
    { id: 'property-3', label: 'property-3' },
  ])
  await expect(vendorConfig.search('none')).resolves.toEqual([])
  await expect(propertyConfig.search('none')).resolves.toEqual([])
  await expect(vendorConfig.openTarget({ id: 'vendor-1', label: 'Vendor' })).resolves.toBe('/lookup-target')
  await propertyConfig.openTarget({ id: 'property-1', label: 'Property' })
  expect(mocks.catalogPage).toHaveBeenNthCalledWith(1, 'pm.party', expect.objectContaining({ filters: { deleted: 'active', is_vendor: 'true' } }))
  expect(mocks.catalogPage).toHaveBeenNthCalledWith(2, 'pm.property', expect.objectContaining({ filters: { deleted: 'active' } }))

  const presentation = mocks.pagePresentationArgs
  expect(presentation.resolveTabFromSourceType('pm.payable_credit_memo')).toBe('credits')
  expect(presentation.resolveTabFromSourceType('pm.payable_payment')).toBe('credits')
  expect(presentation.resolveTabFromSourceType('pm.payable_charge')).toBe('charges')
  expect(presentation.resolveTabFromSourceType('unknown')).toBeNull()
  expect(presentation.resolveTabFromSourceType(null)).toBeNull()
  expect(presentation.buildFocusedChargeBadge(openItemsData().charges[0])).toContain('Charge')
  expect(presentation.buildFocusedCreditBadge(openItemsData().credits[0])).toContain('Credit Memo')
  expect(presentation.buildFocusedCreditBadge(openItemsData().credits[1])).toContain('Payment')
  expect(mocks.shellProps.resolveChargeTypeLabel('pm.payable_charge')).toBe('Payable Charge')
  expect(mocks.shellProps.resolveChargeTypeLabel('custom.charge')).toBe('Charge')
  expect(mocks.shellProps.resolveCreditTypeLabel(null)).toBe('Payment')
})

test('handles empty context, load failures, refresh success/failure, and allocation focus matching', async () => {
  const { router, view } = await renderPage()
  mocks.partyId.value = null
  await mocks.routeContextArgs.load()
  await flushUi()
  expect(mocks.details).not.toHaveBeenCalled()
  await expect.element(view.getByText('badges:Vendor: —|Property: —')).toBeVisible()
  const pushSpy = vi.spyOn(router, 'push').mockResolvedValue(undefined as never)
  await mocks.shellProps.chargeGrid.onActivate('missing-without-data')
  pushSpy.mockRestore()

  mocks.partyId.value = 'vendor-1'
  mocks.details.mockRejectedValueOnce(new Error('Details unavailable'))
  await mocks.routeContextArgs.load()
  await flushUi()
  await expect.element(view.getByText('error:Details unavailable')).toBeVisible()

  mocks.details.mockRejectedValueOnce('Gateway offline')
  await view.getByRole('button', { name: 'Shell refresh' }).click()
  await flushUi()
  await expect.element(view.getByText('error:Gateway offline')).toBeVisible()
  expect(mocks.toastPush).not.toHaveBeenCalled()

  mocks.details.mockResolvedValueOnce(openItemsData())
  await view.getByRole('button', { name: 'Shell refresh' }).click()
  await flushUi()
  expect(mocks.toastPush).toHaveBeenCalledWith(expect.objectContaining({ title: 'Refreshed' }))

  const allocation = { chargeDocumentId: 'charge-1', creditDocumentId: 'credit-1' }
  mocks.presentation.focusedCharge.value = { chargeDocumentId: 'charge-1' }
  expect(mocks.workflowArgs.allocationMatchesContext(allocation)).toBe(true)
  mocks.presentation.focusedCharge.value = null
  mocks.presentation.focusedCredit.value = { creditDocumentId: 'credit-1' }
  expect(mocks.workflowArgs.allocationMatchesContext(allocation)).toBe(true)
  mocks.presentation.focusedCredit.value = null
  expect(mocks.workflowArgs.allocationMatchesContext(allocation)).toBe(false)
})

test('executes workflow factories, route synchronization callbacks, and shell event wiring', async () => {
  const { router, view } = await renderPage()
  const args = mocks.workflowArgs

  mocks.partyId.value = null
  await expect(args.suggestFactory()).rejects.toThrow('Select a vendor and property first.')
  mocks.partyId.value = 'vendor-1'
  await args.suggestFactory()
  expect(mocks.suggestFifo).toHaveBeenCalledWith({ partyId: 'vendor-1', propertyId: 'property-1', createDrafts: false, limit: 500 })
  await args.executeFactory(suggestion([suggestedItem({ applyId: 'apply-1' }), suggestedItem({ applyId: undefined })]))
  expect(mocks.applyBatch).toHaveBeenCalledWith({ applies: [
    { applyId: 'apply-1', applyPayload: { fields: {} } },
    { applyId: null, applyPayload: { fields: {} } },
  ] })
  await args.executeFactory({ ...suggestion(), suggestedApplies: undefined } as never)
  expect(mocks.applyBatch).toHaveBeenLastCalledWith({ applies: [] })
  await args.unapplyFactory('apply-1')
  expect(mocks.unapply).toHaveBeenCalledWith('apply-1')
  expect(args.resolveFallbackCreditDocumentType('missing')).toBe('pm.payable_payment')
  expect(args.buildUnapplySuccessMessage({ creditDocumentType: 'pm.payable_credit_memo', creditLabel: 'CM', chargeLabel: 'CH', amount: 12.5 })).toContain('Credit Memo CM')
  expect(args.buildExecuteSuccessMessage({ totalApplied: null })).toContain('0.00')

  mocks.openApply.value = true
  mocks.refreshFlag.value = true
  mocks.routeContextArgs.clearAutoOpenApplyInRoute([null, null, true, true])
  expect(mocks.clearQueryKeys).toHaveBeenCalledWith(expect.anything(), expect.anything(), ['openApply', 'source'])
  expect(mocks.clearQueryKeys).toHaveBeenCalledWith(expect.anything(), expect.anything(), ['refresh'])
  mocks.routeContextArgs.clearAutoOpenApplyInRoute([null, null, true, false])
  expect(mocks.routeContextArgs.source()).toEqual(['vendor-1', 'property-1', true, true])
  expect(mocks.routeContextArgs.autoOpenApply(['vendor-1', 'property-1', true, false])).toBe(true)
  expect(mocks.routeContextArgs.shouldSkip(['a', 'b', false, false], ['a', 'b', true, false])).toBe(true)
  expect(mocks.routeContextArgs.shouldSkip(['a', 'b', false, false], ['a', 'b', false, true])).toBe(true)
  expect(mocks.routeContextArgs.shouldSkip(['a', 'c', false, false], ['a', 'b', true, false])).toBe(false)
  expect(mocks.routeContextArgs.shouldSkip(['a', 'b', true, false], null)).toBe(false)
  await mocks.routeContextArgs.afterSync([null, null, false, true])
  await mocks.routeContextArgs.afterSync([null, null, true, true])

  const backSpy = vi.spyOn(router, 'back').mockImplementation(() => {})
  await view.getByRole('button', { name: 'Shell back' }).click()
  await view.getByRole('button', { name: 'Shell apply' }).click()
  await view.getByRole('button', { name: 'Dismiss result' }).click()
  await view.getByRole('button', { name: 'Select credits' }).click()
  await view.getByRole('button', { name: 'Open wizard' }).click()
  await view.getByRole('button', { name: 'Wizard action' }).click()
  await view.getByRole('button', { name: 'Close unapply' }).click()
  await view.getByRole('button', { name: 'Confirm unapply' }).click()
  await view.getByRole('button', { name: 'Close', exact: true }).click()
  expect(backSpy).toHaveBeenCalledOnce()
  expect(mocks.workflow.openApplyWizard).toHaveBeenCalledOnce()
  expect(mocks.workflow.dismissPageApplyResult).toHaveBeenCalledOnce()
  expect(mocks.workflow.showApplyPlanAgain).not.toHaveBeenCalled()
  expect(mocks.workflow.suggest).toHaveBeenCalledOnce()
  expect(mocks.workflow.onUnapplyConfirmOpenChanged).toHaveBeenCalledWith(false)
  expect(mocks.workflow.confirmUnapply).toHaveBeenCalledOnce()
})

test('renders every wizard warning, summary cardinality, result branch, and apply document action', async () => {
  const { router, view } = await renderPage()
  mocks.workflow.suggestData.value = suggestion([
    suggestedItem(),
    suggestedItem({ creditDocumentId: 'credit-2', chargeDocumentId: 'charge-2', creditDocumentType: 'pm.payable_payment' }),
  ])
  mocks.workflow.suggestData.value.warnings = [
    { code: 'no_charges', message: 'raw' },
    { code: 'no_credits', message: 'raw' },
    { code: 'limit_reached', message: 'raw' },
    { code: 'outstanding_remaining', message: 'Outstanding charges remain 20' },
    { code: 'credit_remaining', message: 'Unapplied credits remain 10' },
    { code: 'custom', message: 'Custom warning' },
  ]
  await flushUi()
  await expect.element(view.getByText('Selected 2 suggested applies')).toBeVisible()
  await expect.element(view.getByText('No open charges')).toBeVisible()
  await expect.element(view.getByText('Some credit will remain')).toBeVisible()
  await expect.element(view.getByText('Custom warning')).toBeVisible()
  await expect.element(view.getByText('grid-rows:2')).toBeVisible()

  mocks.workflow.suggestError.value = 'Suggestion failed'
  mocks.workflow.applyExecError.value = 'Apply failed'
  mocks.workflow.unapplyError.value = 'Unapply failed'
  mocks.workflow.pendingUnapplyLine.value = {
    creditDocumentType: 'pm.payable_credit_memo', creditLabel: 'CM-1', chargeLabel: 'CH-1', amount: 30,
  }
  await flushUi()
  await expect.element(view.getByText('Suggestion failed')).toBeVisible()
  await expect.element(view.getByText('Apply failed', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Unapply failed', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Unapply credit memo CM-1 from CH-1 for 30.00?', { exact: true })).toBeVisible()

  mocks.workflow.suggestLoading.value = true
  await flushUi()
  await expect.element(view.getByText('Building FIFO suggestion…')).toBeVisible()
  mocks.workflow.suggestLoading.value = false

  mocks.workflow.suggestData.value = { ...suggestion([suggestedItem()]), totalApplied: undefined }
  await flushUi()
  await expect.element(view.getByText(/CM-1.*→.*CH-1/)).toBeVisible()
  mocks.workflow.suggestData.value = suggestion([])
  await flushUi()
  await expect.element(view.getByText('No suggested applies right now.')).toBeVisible()

  mocks.workflow.applyWizardView.value = 'result'
  mocks.workflow.applyResultLines.value = [{
    key: 'line-1', applyId: 'apply-1', creditLabel: 'CM-1', chargeLabel: 'CH-1', appliedOnUtc: '2026-01-15', amount: 30,
  }]
  await flushUi()
  await expect.element(view.getByText('Applied successfully')).toBeVisible()
  await view.getByRole('button', { name: 'Wizard action' }).click()
  expect(mocks.workflow.showApplyPlanAgain).toHaveBeenCalledOnce()
  await view.getByRole('button', { name: 'Open Apply' }).click()
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.payable_apply/apply-1')
})
