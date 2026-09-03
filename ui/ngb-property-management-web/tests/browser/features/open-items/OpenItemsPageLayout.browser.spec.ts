import { defineComponent, h } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

vi.mock('@ngbplatform/ui', () => ({
  NgbBadge: defineComponent({
    name: 'StubBadge',
    setup(_props, { slots }) {
      return () => h('span', { 'data-testid': 'badge' }, slots.default?.())
    },
  }),
  NgbButton: defineComponent({
    name: 'StubButton',
    emits: ['click'],
    setup(_props, { emit, slots }) {
      return () => h('button', { type: 'button', onClick: () => emit('click') }, slots.default?.())
    },
  }),
  NgbIcon: defineComponent({
    name: 'StubIcon',
    props: { name: { type: String, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  }),
  NgbLookup: defineComponent({
    name: 'StubLookup',
    props: {
      modelValue: { type: Object, default: null },
      placeholder: { type: String, required: true },
      showOpen: { type: Boolean, default: false },
      showClear: { type: Boolean, default: false },
    },
    emits: ['query', 'update:modelValue', 'open'],
    setup(props, { emit }) {
      return () => h('section', { 'data-testid': `lookup-${props.placeholder}` }, [
        h('span', { 'data-testid': `lookup-state-${props.placeholder}` }, `${props.showOpen}/${props.showClear}`),
        h('button', { type: 'button', onClick: () => emit('query', 'needle') }, `Query ${props.placeholder}`),
        h('button', { type: 'button', onClick: () => emit('update:modelValue', { id: 'selected', label: 'Selected' }) }, `Select ${props.placeholder}`),
        h('button', { type: 'button', onClick: () => emit('update:modelValue', null) }, `Clear ${props.placeholder}`),
        h('button', { type: 'button', onClick: () => emit('open') }, `Open ${props.placeholder}`),
      ])
    },
  }),
  NgbPageHeader: defineComponent({
    name: 'StubPageHeader',
    props: { title: { type: String, required: true } },
    emits: ['back'],
    setup(props, { emit, slots }) {
      return () => h('header', [
        h('h1', props.title),
        h('button', { type: 'button', onClick: () => emit('back') }, 'Back'),
        slots.secondary?.(),
        slots.actions?.(),
      ])
    },
  }),
  NgbRegisterGrid: defineComponent({
    name: 'StubRegisterGrid',
    props: { storageKey: { type: String, required: true } },
    emits: ['rowActivate'],
    setup(props, { emit }) {
      return () => h('section', { 'data-testid': `grid-${props.storageKey}` }, [
        h('button', { type: 'button', onClick: () => emit('rowActivate', 42) }, `Activate ${props.storageKey}`),
      ])
    },
  }),
  NgbTabs: defineComponent({
    name: 'StubTabs',
    props: { modelValue: { type: String, required: true } },
    emits: ['update:modelValue'],
    setup(props, { emit, slots }) {
      return () => h('section', [
        h('button', { type: 'button', onClick: () => emit('update:modelValue', 'charges') }, 'Tab charges'),
        h('button', { type: 'button', onClick: () => emit('update:modelValue', 'credits') }, 'Tab credits'),
        h('button', { type: 'button', onClick: () => emit('update:modelValue', 'applied') }, 'Tab applied'),
        h('button', { type: 'button', onClick: () => emit('update:modelValue', 'invalid') }, 'Tab invalid'),
        slots.default?.({ active: props.modelValue }),
      ])
    },
  }),
}))

import OpenItemsPageLayout from '../../../../src/features/open-items/OpenItemsPageLayout.vue'

const actions = {
  back: vi.fn(),
  refresh: vi.fn(),
  apply: vi.fn(),
  dismiss: vi.fn(),
  activeTab: vi.fn(),
  query: vi.fn(),
  select: vi.fn(),
  openLookup: vi.fn(),
  activateCharge: vi.fn(),
  activateCredit: vi.fn(),
  openAppliedDocument: vi.fn(),
  openApplyDocument: vi.fn(),
  requestUnapply: vi.fn(),
}

function requiredProps(overrides: Record<string, unknown> = {}) {
  return {
    title: 'Receivables',
    lookups: [
      {
        key: 'customer',
        value: null,
        items: [],
        placeholder: 'Customer',
        widthClass: 'w-64',
        onQuery: actions.query,
        onSelect: actions.select,
        onOpen: actions.openLookup,
      },
    ],
    loading: false,
    error: null,
    contextReady: true,
    emptyStateMessage: 'Choose a customer',
    contextBadges: ['Customer A'],
    focusedContextBadge: null,
    summary: { totalOutstanding: 1250.5, totalCredit: 250.25 },
    pageResult: null,
    activeTab: 'charges',
    tabs: [
      { key: 'charges', label: 'Charges' },
      { key: 'credits', label: 'Credits' },
      { key: 'applied', label: 'Applied' },
    ],
    chargeGrid: { columns: [], rows: [], storageKey: 'charges-grid', onActivate: actions.activateCharge },
    creditGrid: { columns: [], rows: [], storageKey: 'credits-grid', onActivate: actions.activateCredit },
    appliedRows: [],
    appliedSubtitle: 'Active allocations',
    appliedEmptyMessage: 'No active allocations',
    highlightedApplyIds: [],
    resolveChargeTypeLabel: (value: string) => `Charge type ${value}`,
    resolveCreditTypeLabel: (value?: string | null) => `Credit type ${value ?? 'unknown'}`,
    openAppliedDocument: actions.openAppliedDocument,
    openApplyDocument: actions.openApplyDocument,
    requestUnapply: actions.requestUnapply,
    canRefresh: true,
    canApply: true,
    onBack: actions.back,
    onRefresh: actions.refresh,
    onApply: actions.apply,
    onDismissPageResult: actions.dismiss,
    'onUpdate:activeTab': actions.activeTab,
    ...overrides,
  }
}

beforeEach(() => {
  Object.values(actions).forEach((action) => action.mockReset())
})

test('renders the incomplete context and forwards header, lookup, and enabled toolbar actions', async () => {
  const view = await render(OpenItemsPageLayout, {
    props: requiredProps({
      contextReady: false,
      error: 'Unable to load open items',
      focusedContextBadge: undefined,
      pageResult: undefined,
    }) as never,
  })

  await expect.element(view.getByText('Unable to load open items')).toBeVisible()
  await expect.element(view.getByText('Choose a customer')).toBeVisible()
  await expect.element(view.getByTestId('lookup-state-Customer')).toHaveTextContent('false/false')

  await view.getByRole('button', { name: 'Back' }).click()
  await view.getByRole('button', { name: 'Query Customer' }).click()
  await view.getByRole('button', { name: 'Select Customer' }).click()
  await view.getByRole('button', { name: 'Clear Customer' }).click()
  await view.getByRole('button', { name: 'Open Customer' }).click()
  await view.getByTitle('Refresh').click()
  await view.getByTitle('Apply').click()

  expect(actions.back).toHaveBeenCalledOnce()
  expect(actions.query).toHaveBeenCalledWith('needle')
  expect(actions.select).toHaveBeenNthCalledWith(1, { id: 'selected', label: 'Selected' })
  expect(actions.select).toHaveBeenNthCalledWith(2, null)
  expect(actions.openLookup).toHaveBeenCalledOnce()
  expect(actions.refresh).toHaveBeenCalledOnce()
  expect(actions.apply).toHaveBeenCalledOnce()
})

test('renders result details, a selected lookup, and guards invalid tab values', async () => {
  const line = {
    key: 'line-1',
    applyId: 'apply-result',
    creditDocumentId: 'credit-result',
    creditDocumentType: 'Payment',
    creditLabel: 'PAY-1',
    chargeDocumentId: 'charge-result',
    chargeLabel: 'INV-1',
    appliedOnUtc: '2026-08-23',
    amount: 75.5,
  }
  const view = await render(OpenItemsPageLayout, {
    props: requiredProps({
      lookups: [{
        key: 'customer',
        value: { id: 'customer-a', label: 'Customer A' },
        items: [{ id: 'customer-a', label: 'Customer A' }],
        placeholder: 'Customer',
        widthClass: 'w-64',
        onQuery: actions.query,
        onSelect: actions.select,
        onOpen: actions.openLookup,
      }],
      focusedContextBadge: 'Property One',
      pageResult: {
        visible: true,
        title: 'Created 1 apply',
        subtitle: 'Success',
        lines: [line],
        outstandingNow: 1175,
        creditNow: 174.75,
        inconsistent: true,
      },
    }) as never,
  })

  await expect.element(view.getByTestId('lookup-state-Customer')).toHaveTextContent('true/true')
  await expect.element(view.getByText('Customer A')).toBeVisible()
  await expect.element(view.getByText('Property One')).toBeVisible()
  await expect.element(view.getByText('Created 1 apply')).toBeVisible()
  await expect.element(view.getByText(/did not return active allocations/)).toBeVisible()
  await view.getByRole('button', { name: 'Dismiss' }).click()
  await view.getByRole('button', { name: 'Open Apply' }).click()
  await view.getByRole('button', { name: 'Activate charges-grid' }).click()
  await view.getByRole('button', { name: 'Tab charges' }).click()
  await view.getByRole('button', { name: 'Tab credits' }).click()
  await view.getByRole('button', { name: 'Tab applied' }).click()
  await view.getByRole('button', { name: 'Tab invalid' }).click()

  expect(actions.dismiss).toHaveBeenCalledOnce()
  expect(actions.openApplyDocument).toHaveBeenCalledWith('apply-result')
  expect(actions.activateCharge).toHaveBeenCalledWith('42')
  expect(actions.activeTab.mock.calls).toEqual([['charges'], ['credits'], ['applied']])
  view.unmount()

  const consistent = await render(OpenItemsPageLayout, {
    props: requiredProps({
      pageResult: {
        visible: true,
        title: 'Apply complete',
        subtitle: 'Consistent result',
        lines: [],
        outstandingNow: 0,
        creditNow: 0,
        inconsistent: false,
      },
    }) as never,
  })
  await expect.element(consistent.getByText('Apply complete')).toBeVisible()
  expect(document.body.textContent).not.toContain('did not return active allocations')
})

test('renders the credits grid and disabled toolbar combinations', async () => {
  const loading = await render(OpenItemsPageLayout, {
    props: requiredProps({ activeTab: 'credits', loading: true, canRefresh: true, canApply: true }) as never,
  })

  await expect.element(loading.getByTitle('Refresh')).toBeDisabled()
  await expect.element(loading.getByTitle('Apply')).toBeDisabled()
  await loading.getByRole('button', { name: 'Activate credits-grid' }).click()
  expect(actions.activateCredit).toHaveBeenCalledWith('42')
  loading.unmount()

  const forbidden = await render(OpenItemsPageLayout, {
    props: requiredProps({ activeTab: 'credits', loading: false, canRefresh: false, canApply: false }) as never,
  })
  await expect.element(forbidden.getByTitle('Refresh')).toBeDisabled()
  await expect.element(forbidden.getByTitle('Apply')).toBeDisabled()
})

test('renders highlighted, contextual, and neutral allocations and forwards all document actions', async () => {
  const allocations = [
    {
      applyId: 'apply-highlighted',
      applyNumber: 'APP-1',
      creditDocumentId: 'credit-1',
      creditDocumentType: 'Payment',
      creditDocumentNumber: 'PAY-1',
      chargeDocumentId: 'charge-1',
      chargeDocumentType: 'Invoice',
      chargeNumber: 'INV-1',
      appliedOnUtc: '2026-08-20',
      amount: 10,
      isPosted: true,
    },
    {
      applyId: 'apply-context',
      applyDisplay: 'Context Apply',
      creditDocumentId: 'credit-2',
      creditDocumentType: 'CreditMemo',
      creditDocumentDisplay: 'Credit Two',
      chargeDocumentId: 'charge-2',
      chargeDocumentType: 'Fee',
      chargeDisplay: 'Charge Two',
      appliedOnUtc: 'not-a-date',
      amount: 20.25,
      isPosted: true,
    },
    {
      applyId: 'apply-neutral',
      creditDocumentId: 'credit-3',
      creditDocumentType: 'Payment',
      chargeDocumentId: 'charge-3',
      chargeDocumentType: 'Invoice',
      appliedOnUtc: '',
      amount: 30.5,
      isPosted: false,
    },
  ]
  const isContextAllocation = vi.fn((allocation: { applyId: string }) => allocation.applyId === 'apply-context')
  const view = await render(OpenItemsPageLayout, {
    props: requiredProps({
      activeTab: 'applied',
      appliedRows: allocations,
      highlightedApplyIds: ['apply-highlighted'],
      isContextAllocation,
    }) as never,
  })

  await expect.element(view.getByText('Recent: 1')).toBeVisible()
  expect(view.getByRole('button', { name: /PAY-1/ }).element().closest('.grid')?.classList.contains('bg-blue-50/70')).toBe(true)
  expect(view.getByRole('button', { name: /Credit Two/ }).element().closest('.grid')?.classList.contains('bg-amber-50/60')).toBe(true)
  expect(view.getByRole('button', { name: /credit-3/ }).element().closest('.grid')?.classList.contains('bg-ngb-card')).toBe(true)

  await view.getByRole('button', { name: /PAY-1/ }).click()
  await view.getByRole('button', { name: /INV-1/ }).click()
  await view.getByRole('button', { name: /APP-1/ }).click()
  await view.getByTitle('Open Apply').first().click()
  await view.getByTitle('Unapply').first().click()

  expect(actions.openAppliedDocument).toHaveBeenNthCalledWith(1, 'Payment', 'credit-1')
  expect(actions.openAppliedDocument).toHaveBeenNthCalledWith(2, 'Invoice', 'charge-1')
  expect(actions.openApplyDocument).toHaveBeenNthCalledWith(1, 'apply-highlighted')
  expect(actions.openApplyDocument).toHaveBeenNthCalledWith(2, 'apply-highlighted')
  expect(actions.requestUnapply).toHaveBeenCalledWith({
    key: 'apply-highlighted',
    applyId: 'apply-highlighted',
    creditDocumentId: 'credit-1',
    creditDocumentType: 'Payment',
    creditLabel: 'PAY-1',
    chargeDocumentId: 'charge-1',
    chargeLabel: 'INV-1',
    appliedOnUtc: '2026-08-20',
    amount: 10,
  })
  expect(isContextAllocation.mock.calls.map(([allocation]) => allocation.applyId)).toEqual(['apply-context', 'apply-neutral'])
})

test('renders an empty applied tab and a hidden, consistent page result without a context resolver', async () => {
  const view = await render(OpenItemsPageLayout, {
    props: requiredProps({
      activeTab: 'applied',
      appliedRows: [],
      highlightedApplyIds: [],
      isContextAllocation: undefined,
      pageResult: {
        visible: false,
        title: 'Hidden result',
        subtitle: '',
        lines: [],
        outstandingNow: 0,
        creditNow: 0,
        inconsistent: false,
      },
    }) as never,
  })

  await expect.element(view.getByText('No active allocations')).toBeVisible()
  expect(document.body.textContent).not.toContain('Recent:')
  expect(document.body.textContent).not.toContain('Hidden result')
})

test('bounds large applied-allocation lists to one DOM page', async () => {
  const allocations = Array.from({ length: 101 }, (_, index) => ({
    applyId: `apply-${index + 1}`,
    applyNumber: `APP-${index + 1}`,
    creditDocumentId: `credit-${index + 1}`,
    creditDocumentType: 'Payment',
    creditDocumentNumber: `PAY-${index + 1}`,
    chargeDocumentId: `charge-${index + 1}`,
    chargeDocumentType: 'Invoice',
    chargeNumber: `INV-${index + 1}`,
    appliedOnUtc: '2026-08-20',
    amount: index + 1,
    isPosted: true,
  }))
  const view = await render(OpenItemsPageLayout, {
    props: requiredProps({ activeTab: 'applied', appliedRows: allocations }) as never,
  })

  await expect.element(view.getByText('Rows 1–100 of 101')).toBeVisible()
  expect(document.querySelectorAll('[data-testid="open-items-applied-panel"] button[title="Open Apply"]')).toHaveLength(100)
  expect(document.body.textContent).toContain('APP-1')
  expect(document.body.textContent).not.toContain('APP-101')

  await view.getByRole('button', { name: 'Next' }).click()
  await expect.element(view.getByText('Rows 101–101 of 101')).toBeVisible()
  expect(document.querySelectorAll('[data-testid="open-items-applied-panel"] button[title="Open Apply"]')).toHaveLength(1)
  expect(document.body.textContent).toContain('APP-101')
  expect(document.body.textContent).not.toContain('APP-1Payment')
})
