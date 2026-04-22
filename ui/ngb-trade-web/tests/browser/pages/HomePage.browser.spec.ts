import { ref } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

const mocks = vi.hoisted(() => ({
  routerPush: vi.fn(),
  refresh: vi.fn(),
  state: {
    asOf: null as unknown,
    dashboard: null as unknown,
    error: null as unknown,
    loading: null as unknown,
    warnings: null as unknown,
  },
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: mocks.routerPush,
  }),
}))

vi.mock('../../../src/home/homeData', () => ({
  loadHomeDashboard: vi.fn(),
}))

vi.mock('ngb-ui-framework', async () => {
  const { defineComponent, h } = await import('vue')

  const StubBadge = defineComponent({
    name: 'StubBadge',
    props: {
      tone: { type: String, default: 'neutral' },
    },
    setup(props, { slots }) {
      return () => h('span', { 'data-testid': `badge-${props.tone}` }, slots.default?.())
    },
  })

  const StubDashboardToolbar = defineComponent({
    name: 'StubDashboardToolbar',
    props: {
      modelValue: { type: String, required: true },
      loading: { type: Boolean, default: false },
    },
    emits: ['refresh'],
    setup(props, { emit }) {
      return () => h('div', { 'data-testid': 'toolbar' }, [
        h('span', { 'data-testid': 'toolbar-as-of' }, props.modelValue),
        h('button', { type: 'button', onClick: () => emit('refresh') }, 'Refresh'),
      ])
    },
  })

  const StubStatusBanner = defineComponent({
    name: 'StubStatusBanner',
    props: {
      error: { type: String, default: null },
      warnings: { type: Array as () => string[], default: () => [] },
      errorTitle: { type: String, default: 'Error' },
    },
    setup(props) {
      return () => h('div', { 'data-testid': 'status-banner' }, [
        props.error ? h('div', props.errorTitle) : null,
        ...(props.warnings ?? []).map((warning) => h('div', warning)),
      ])
    },
  })

  const StubIcon = defineComponent({
    name: 'StubIcon',
    props: {
      name: { type: String, required: true },
    },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  })

  const StubPageHeader = defineComponent({
    name: 'StubPageHeader',
    props: {
      title: { type: String, required: true },
    },
    setup(props, { slots }) {
      return () => h('header', { 'data-testid': 'page-header' }, [
        h('h1', props.title),
        h('div', { 'data-testid': 'page-header-secondary' }, slots.secondary?.()),
        h('div', { 'data-testid': 'page-header-actions' }, slots.actions?.()),
      ])
    },
  })

  const StubTrendChart = defineComponent({
    name: 'StubTrendChart',
    props: {
      labels: { type: Array as () => string[], default: () => [] },
      series: { type: Array as () => Array<{ label: string; values: number[] }>, default: () => [] },
      mode: { type: String, default: 'line' },
    },
    setup(props) {
      return () => h('pre', { 'data-testid': 'trend-chart' }, JSON.stringify({
        labels: props.labels,
        series: props.series,
        mode: props.mode,
      }))
    },
  })

  return {
    formatDashboardCount: (value: number) => `${value}`,
    formatDashboardMoneyCompact: (value: number) => `$${value.toFixed(0)}`,
    formatDashboardPercent: (value: number) => `${value.toFixed(1)}%`,
    NgbBadge: StubBadge,
    NgbDashboardAsOfToolbar: StubDashboardToolbar,
    NgbDashboardStatusBanner: StubStatusBanner,
    NgbIcon: StubIcon,
    NgbPageHeader: StubPageHeader,
    NgbTrendChart: StubTrendChart,
    useDashboardPageState: () => ({
      asOf: mocks.state.asOf,
      dashboard: mocks.state.dashboard,
      error: mocks.state.error,
      loading: mocks.state.loading,
      refresh: mocks.refresh,
      warnings: mocks.state.warnings,
    }),
  }
})

import HomePage from '../../../src/pages/HomePage.vue'

function createDashboard(overrides: Record<string, unknown> = {}) {
  return {
    warnings: [],
    asOf: '2026-04-18',
    monthKey: '2026-04',
    monthLabel: 'Apr 2026',
    salesThisMonth: 180,
    purchasesThisMonth: 95,
    inventoryOnHand: 12,
    grossMargin: 55,
    activeSalesItemCount: 6,
    activeCustomerCount: 4,
    activeVendorCount: 3,
    inventoryPositionCount: 9,
    topItems: [
      { item: 'Adapter Kit', soldQuantity: 4, netSales: 90, grossMargin: 10, marginPercent: 11.1, route: '/reports/items/adapter-kit' },
    ],
    topCustomers: [
      { customer: 'Bayview Stores', salesDocumentCount: 3, returnDocumentCount: 1, netSales: 180, grossMargin: 55, marginPercent: 30.6, route: '/reports/customers/bayview' },
    ],
    topVendors: [
      { vendor: 'Northstar Distribution', purchaseDocumentCount: 2, returnDocumentCount: 0, netPurchases: 95, route: '/reports/vendors/northstar' },
    ],
    inventoryPositions: [
      { item: 'Cable Ties', warehouse: 'Alpha DC', quantity: 8, route: '/reports/inventory?item=item-a', itemRoute: '/catalogs/trd.item/item-a', warehouseRoute: '/catalogs/trd.warehouse/alpha' },
    ],
    recentDocuments: [
      { title: 'Sales Invoice SI-2048', amountDisplay: '$80', documentDate: '2026-04-18', notes: 'Posted to the general journal', route: '/documents/trd.sales_invoice/si-2048' },
    ],
    charts: {
      salesMix: {
        title: 'Sales mix by item',
        subtitle: 'Net sales and gross margin for the top-selling items this month',
        labels: ['Adapter Kit'],
        series: [{ label: 'Net sales', color: 'blue', values: [90] }],
        route: '/reports/trd.sales_by_item',
      },
      inventoryFootprint: {
        title: 'Inventory footprint',
        subtitle: 'Largest on-hand positions across item and warehouse combinations',
        labels: ['Cable Ties · Alpha DC'],
        series: [{ label: 'Quantity', color: 'green', values: [8] }],
        route: '/reports/trd.inventory_balances',
      },
    },
    routes: {
      sales: '/reports/trd.sales_by_customer',
      purchases: '/reports/trd.purchases_by_vendor',
      inventory: '/reports/trd.inventory_balances',
      grossMargin: '/reports/trd.sales_by_item',
      currentPrices: '/reports/trd.current_item_prices',
      salesByItem: '/reports/trd.sales_by_item',
      salesByCustomer: '/reports/trd.sales_by_customer',
      purchasesByVendor: '/reports/trd.purchases_by_vendor',
    },
    ...overrides,
  }
}

beforeEach(() => {
  mocks.routerPush.mockReset()
  mocks.refresh.mockReset()
  mocks.state = {
    asOf: ref('2026-04-18'),
    dashboard: ref(createDashboard()),
    error: ref(null),
    loading: ref(false),
    warnings: ref(['Pricing feed is 10 minutes behind']),
  }
})

test('renders dashboard content, warnings, and route-driven actions', async () => {
  const view = await render(HomePage)

  await expect.element(view.getByText('Trading pulse and inventory control')).toBeVisible()
  await expect.element(view.getByText('6 selling items · 4 active customers · 3 active vendors')).toBeVisible()
  await expect.element(view.getByText('Pricing feed is 10 minutes behind')).toBeVisible()
  await expect.element(view.getByText('Sales Invoice SI-2048')).toBeVisible()
  await expect.element(view.getByText('Adapter Kit', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Northstar Distribution')).toBeVisible()

  await view.getByText('New Sales Invoice').click()
  expect(mocks.routerPush).toHaveBeenCalledWith('/documents/trd.sales_invoice/new')

  await view.getByText('Review Price Book').click()
  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/trd.current_item_prices')
})

test('shows empty-state messaging and wires the refresh action', async () => {
  mocks.state.dashboard = ref(createDashboard({
    topItems: [],
    topCustomers: [],
    topVendors: [],
    inventoryPositions: [],
    recentDocuments: [],
    charts: {
      salesMix: { title: 'Sales mix by item', subtitle: '...', labels: [], series: [], route: '/reports/trd.sales_by_item' },
      inventoryFootprint: { title: 'Inventory footprint', subtitle: '...', labels: [], series: [], route: '/reports/trd.inventory_balances' },
    },
  }))
  mocks.state.warnings = ref([])

  const view = await render(HomePage)

  await expect.element(view.getByText('No posted sales activity exists for the selected month yet.')).toBeVisible()
  await expect.element(view.getByText('No item sales have been posted in the current month.')).toBeVisible()
  await expect.element(view.getByText('No vendor purchasing activity is available for this month yet.')).toBeVisible()
  await expect.element(view.getByText('No inventory balance positions are available yet.')).toBeVisible()

  await view.getByTestId('toolbar').getByRole('button', { name: 'Refresh' }).click()
  expect(mocks.refresh).toHaveBeenCalledTimes(1)
})
