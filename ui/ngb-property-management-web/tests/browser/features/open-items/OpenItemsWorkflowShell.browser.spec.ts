import { defineComponent, h } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

vi.mock('../../../../src/features/open-items/OpenItemsPageLayout.vue', () => ({
  default: defineComponent({
    name: 'StubOpenItemsPageLayout',
    props: {
      title: { type: String, required: true },
      error: { type: String, default: null },
      focusedContextBadge: { type: String, default: null },
      pageResult: { type: Object, default: null },
      isContextAllocation: { type: Function, default: undefined },
      chargePage: { type: Object, default: null },
      creditPage: { type: Object, default: null },
      appliedPage: { type: Object, default: null },
    },
    emits: ['back', 'refresh', 'apply', 'dismissPageResult', 'update:activeTab', 'page'],
    setup(props, { emit }) {
      return () => h('section', { 'data-testid': 'open-items-layout' }, [
        h('span', { 'data-testid': 'layout-title' }, props.title),
        h('span', { 'data-testid': 'layout-error' }, props.error ?? ''),
        h('span', { 'data-testid': 'layout-focused-badge' }, props.focusedContextBadge ?? ''),
        h('span', { 'data-testid': 'layout-page-result' }, props.pageResult ? 'page-result' : ''),
        h('span', { 'data-testid': 'layout-context-resolver' }, props.isContextAllocation ? 'resolver' : ''),
        h('span', { 'data-testid': 'layout-page-states' }, [props.chargePage, props.creditPage, props.appliedPage].filter(Boolean).length),
        h('button', { type: 'button', onClick: () => emit('back') }, 'Layout back'),
        h('button', { type: 'button', onClick: () => emit('refresh') }, 'Layout refresh'),
        h('button', { type: 'button', onClick: () => emit('apply') }, 'Layout apply'),
        h('button', { type: 'button', onClick: () => emit('dismissPageResult') }, 'Dismiss result'),
        h('button', { type: 'button', onClick: () => emit('update:activeTab', 'applied') }, 'Select applied'),
        h('button', { type: 'button', onClick: () => emit('page', { tab: 'credits', offset: 100 }) }, 'Layout next page'),
      ])
    },
  }),
}))

vi.mock('@ngbplatform/ui', () => ({
  NgbIcon: defineComponent({
    name: 'StubIcon',
    props: { name: { type: String, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  }),
  NgbDrawer: defineComponent({
    name: 'StubDrawer',
    props: {
      open: { type: Boolean, default: false },
      title: { type: String, required: true },
      subtitle: { type: String, default: '' },
    },
    emits: ['update:open'],
    setup(props, { emit, slots }) {
      return () => props.open
        ? h('aside', { 'data-testid': 'apply-drawer' }, [
            h('h2', props.title),
            h('span', { 'data-testid': 'drawer-subtitle' }, props.subtitle),
            slots.actions?.(),
            slots.default?.(),
            slots.footer?.(),
            h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Close drawer'),
          ])
        : h('div', { hidden: true }, [slots.actions?.(), slots.default?.(), slots.footer?.()])
    },
  }),
  NgbConfirmDialog: defineComponent({
    name: 'StubConfirmDialog',
    props: {
      open: { type: Boolean, default: false },
      danger: { type: Boolean, default: false },
      confirmLoading: { type: Boolean, default: false },
    },
    emits: ['update:open', 'confirm'],
    setup(props, { emit, slots }) {
      return () => h('section', {
        'data-testid': 'unapply-dialog',
        'data-open': String(props.open),
        'data-danger': String(props.danger),
        'data-loading': String(props.confirmLoading),
      }, [
        slots.default?.(),
        h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Close confirmation'),
        h('button', { type: 'button', onClick: () => emit('confirm') }, 'Confirm unapply'),
      ])
    },
  }),
}))

import OpenItemsWorkflowShell from '../../../../src/features/open-items/OpenItemsWorkflowShell.vue'

const listeners = {
  back: vi.fn(),
  refresh: vi.fn(),
  apply: vi.fn(),
  dismiss: vi.fn(),
  activeTab: vi.fn(),
  page: vi.fn(),
  wizardOpen: vi.fn(),
  wizardAction: vi.fn(),
  confirmOpen: vi.fn(),
  confirmUnapply: vi.fn(),
}

function requiredProps() {
  return {
    title: 'Receivables',
    lookups: [],
    loading: false,
    contextReady: false,
    emptyStateMessage: 'Choose a customer',
    contextBadges: [],
    summary: { totalOutstanding: 0, totalCredit: 0 },
    activeTab: 'charges',
    tabs: [{ key: 'charges', label: 'Charges' }],
    chargeGrid: { columns: [], rows: [] },
    creditGrid: { columns: [], rows: [] },
    chargePage: { offset: 0, limit: 100, total: 101, hasMore: true },
    creditPage: { offset: 0, limit: 100, total: 0, hasMore: false },
    appliedPage: { offset: 0, limit: 100, total: 0, hasMore: false },
    appliedRows: [],
    appliedSubtitle: 'Applied allocations',
    appliedEmptyMessage: 'No allocations',
    highlightedApplyIds: [],
    resolveChargeTypeLabel: (value: string) => value,
    resolveCreditTypeLabel: (value?: string | null) => value ?? '',
    openAppliedDocument: vi.fn(),
    openApplyDocument: vi.fn(),
    requestUnapply: vi.fn(),
    canRefresh: true,
    canApply: true,
    applyWizardOpen: true,
    applyWizardSubtitle: 'Apply credits',
    applyWizardActionDisabled: false,
    applyWizardActionTitle: 'Refresh candidates',
    emptyWizardMessage: 'Choose context first',
    unapplyConfirmOpen: true,
    unapplyTitle: 'Unapply allocation',
    unapplyMessage: 'Continue?',
    unapplyConfirmText: 'Unapply',
    unapplyCancelText: 'Cancel',
    onBack: listeners.back,
    onRefresh: listeners.refresh,
    onApply: listeners.apply,
    onDismissPageResult: listeners.dismiss,
    'onUpdate:activeTab': listeners.activeTab,
    onPage: listeners.page,
    'onUpdate:applyWizardOpen': listeners.wizardOpen,
    onApplyWizardAction: listeners.wizardAction,
    'onUpdate:unapplyConfirmOpen': listeners.confirmOpen,
    onConfirmUnapply: listeners.confirmUnapply,
  }
}

beforeEach(() => {
  Object.values(listeners).forEach((listener) => listener.mockReset())
})

test('forwards page, drawer, and confirmation actions with default optional props', async () => {
  const view = await render(OpenItemsWorkflowShell, {
    props: requiredProps() as never,
    slots: {
      drawer: () => h('div', { 'data-testid': 'drawer-content' }, 'Drawer content'),
      footer: () => h('div', { 'data-testid': 'drawer-footer' }, 'Footer content'),
      unapply: () => h('div', { 'data-testid': 'unapply-content' }, 'Allocation details'),
    },
  })

  await expect.element(view.getByText('Choose context first')).toBeVisible()
  expect(document.querySelector('[data-testid="drawer-content"]')).toBeNull()
  await expect.element(view.getByTestId('drawer-footer')).toBeVisible()
  await expect.element(view.getByTestId('unapply-content')).toBeVisible()
  await expect.element(view.getByTestId('unapply-dialog')).toHaveAttribute('data-danger', 'false')
  await expect.element(view.getByTestId('unapply-dialog')).toHaveAttribute('data-loading', 'false')

  await view.getByRole('button', { name: 'Layout back' }).click()
  await view.getByRole('button', { name: 'Layout refresh' }).click()
  await view.getByRole('button', { name: 'Layout apply' }).click()
  await view.getByRole('button', { name: 'Dismiss result' }).click()
  await view.getByRole('button', { name: 'Select applied' }).click()
  await view.getByRole('button', { name: 'Layout next page' }).click()
  await view.getByRole('button', { name: 'Close drawer' }).click()
  await view.getByTitle('Refresh candidates').click()
  await view.getByRole('button', { name: 'Close confirmation' }).click()
  await view.getByRole('button', { name: 'Confirm unapply' }).click()

  expect(listeners.back).toHaveBeenCalledOnce()
  expect(listeners.refresh).toHaveBeenCalledOnce()
  expect(listeners.apply).toHaveBeenCalledOnce()
  expect(listeners.dismiss).toHaveBeenCalledOnce()
  expect(listeners.activeTab).toHaveBeenCalledWith('applied')
  expect(listeners.page).toHaveBeenCalledWith({ tab: 'credits', offset: 100 })
  await expect.element(view.getByTestId('layout-page-states')).toHaveTextContent('3')
  expect(listeners.wizardOpen).toHaveBeenCalledWith(false)
  expect(listeners.wizardAction).toHaveBeenCalledOnce()
  expect(listeners.confirmOpen).toHaveBeenCalledWith(false)
  expect(listeners.confirmUnapply).toHaveBeenCalledOnce()
})

test('renders ready drawer content and forwards supplied optional state', async () => {
  const props = {
    ...requiredProps(),
    error: 'Partial data',
    focusedContextBadge: 'Customer A',
    pageResult: { tone: 'success', title: 'Applied', message: 'Done' },
    contextReady: true,
    isContextAllocation: () => true,
    applyWizardOpen: false,
    applyWizardActionDisabled: true,
    unapplyDanger: true,
    unapplyConfirmLoading: true,
  }

  const view = await render(OpenItemsWorkflowShell, {
    props: props as never,
    slots: {
      drawer: () => h('div', { 'data-testid': 'drawer-content' }, 'Drawer content'),
      footer: () => h('div', { 'data-testid': 'drawer-footer' }, 'Footer content'),
      unapply: () => h('div', { 'data-testid': 'unapply-content' }, 'Allocation details'),
    },
  })

  expect(view.getByTestId('drawer-content').element()).not.toBeNull()
  expect(document.body.textContent).not.toContain('Choose context first')
  await expect.element(view.getByTestId('layout-error')).toHaveTextContent('Partial data')
  await expect.element(view.getByTestId('layout-focused-badge')).toHaveTextContent('Customer A')
  await expect.element(view.getByTestId('layout-page-result')).toHaveTextContent('page-result')
  await expect.element(view.getByTestId('layout-context-resolver')).toHaveTextContent('resolver')
  await expect.element(view.getByTestId('unapply-dialog')).toHaveAttribute('data-danger', 'true')
  await expect.element(view.getByTestId('unapply-dialog')).toHaveAttribute('data-loading', 'true')
  await expect.element(view.getByTitle('Refresh candidates')).toBeDisabled()
})
