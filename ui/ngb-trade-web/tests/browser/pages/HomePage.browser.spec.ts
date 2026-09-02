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

vi.mock('@ngbplatform/ui', async () => {
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
    emits: ['refresh', 'update:modelValue'],
    setup(props, { emit }) {
      return () => h('div', { 'data-testid': 'toolbar' }, [
        h('span', { 'data-testid': 'toolbar-as-of' }, props.modelValue),
        h('button', { type: 'button', onClick: () => emit('refresh') }, 'Refresh'),
        h('button', { type: 'button', onClick: () => emit('update:modelValue', '2026-04-19') }, 'Advance as-of'),
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
    useDashboardPageState: (options: { resolveWarnings: (value: { warnings?: string[] } | null) => string[] }) => {
      options.resolveWarnings(null)
      options.resolveWarnings({ warnings: ['resolved warning'] })
      return {
        asOf: mocks.state.asOf,
        dashboard: mocks.state.dashboard,
        error: mocks.state.error,
        loading: mocks.state.loading,
        refresh: mocks.refresh,
        warnings: mocks.state.warnings,
      }
    },
  }
})

vi.mock('@ngbplatform/ui/lazy', async () => {
  const { defineComponent, h } = await import('vue')
  const TrendChart = defineComponent({
    props: {
      labels: { type: Array, default: () => [] },
      series: { type: Array, default: () => [] },
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
    loadNgbTrendChart: async () => TrendChart,
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

  await view.getByText('Receive Stock').click()
  await view.getByText('Sales This Month').click()
  await view.getByText('Purchases This Month').click()
  await view.getByText('Inventory On Hand').click()
  await view.getByText('Gross Margin', { exact: true }).click()
  await view.getByText('Sales mix by item', { exact: true }).click()
  await view.getByText('Inventory footprint', { exact: true }).click()

  for (const button of Array.from(document.querySelectorAll('button'))) {
    if (button.textContent?.trim() === 'View all' || button.textContent?.trim() === 'View balances') button.click()
  }

  await view.getByText('Adapter Kit', { exact: true }).click()
  await view.getByText('Bayview Stores').click()
  await view.getByText('Northstar Distribution').click()
  await view.getByRole('button', { name: /^Cable Ties Alpha DC/ }).click()
  await view.getByText('Sales Invoice SI-2048').click()

  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/items/adapter-kit')
  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/customers/bayview')
  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/vendors/northstar')
  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/inventory?item=item-a')
  expect(mocks.routerPush).toHaveBeenCalledWith('/documents/trd.sales_invoice/si-2048')
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
  await view.getByTestId('toolbar').getByRole('button', { name: 'Advance as-of' }).click()
  await expect.element(view.getByTestId('toolbar-as-of')).toHaveTextContent('2026-04-19')
})

test('uses page fallbacks while dashboard data is unavailable and shows the loading period state', async () => {
  mocks.state.dashboard = ref(null)
  mocks.state.loading = ref(true)
  mocks.state.warnings = ref([])

  const view = await render(HomePage)

  await expect.element(view.getByText('Refreshing trade workspace…')).toBeVisible()
  await expect.element(view.getByText('Sales mix by item', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Inventory footprint', { exact: true })).toBeVisible()
  await expect.element(view.getByText('No recent trade documents exist yet.')).toBeVisible()
  expect(view.getByTestId('trade-home-kpis').element().querySelectorAll('button')).toHaveLength(0)

  await view.getByText('Review Price Book').click()
  await view.getByText('Sales mix by item', { exact: true }).click()
  await view.getByText('Inventory footprint', { exact: true }).click()

  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/trd.current_item_prices')
  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/trd.sales_by_item')
  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/trd.inventory_balances')

  ;(mocks.state.loading as { value: boolean }).value = false
  await expect.element(view.getByText('Operational focus for the selected period')).toBeVisible()
})

test('handles zero and negative KPIs, invalid quantities, incomplete documents, and blank routes', async () => {
  mocks.state.dashboard = ref(createDashboard({
    monthLabel: '',
    salesThisMonth: 0,
    purchasesThisMonth: 0,
    inventoryOnHand: 0,
    grossMargin: -5,
    routes: undefined,
    topItems: [
      { item: 'Loss Leader', soldQuantity: Number.NaN, netSales: 0, grossMargin: -5, marginPercent: -1, route: '' },
      { item: 'Neutral Item', soldQuantity: 0, netSales: 0, grossMargin: 0, marginPercent: 0, route: null },
    ],
    topCustomers: [
      { customer: 'Neutral Customer', salesDocumentCount: 0, returnDocumentCount: 0, netSales: 0, grossMargin: 0, marginPercent: 0, route: '' },
    ],
    topVendors: [
      { vendor: 'Route-less Vendor', purchaseDocumentCount: 0, returnDocumentCount: 0, netPurchases: 0, route: null },
    ],
    inventoryPositions: [
      { item: 'Unknown Quantity', warehouse: 'Unassigned', quantity: null, route: undefined },
    ],
    recentDocuments: [
      { title: 'Draft Transfer', amountDisplay: null, documentDate: null, notes: 'Draft document', route: '' },
      { title: 'Imported Record', notes: 'Imported from legacy', route: null },
    ],
    charts: {
      salesMix: null,
      inventoryFootprint: null,
    },
  }))

  const view = await render(HomePage)

  await expect.element(view.getByText('Operational focus for the selected period')).toBeVisible()
  expect(document.body.textContent).toContain('n/a')
  expect(document.body.textContent).toContain('Date n/a')
  await expect.element(view.getByText('Draft Transfer')).toBeVisible()
  await expect.element(view.getByText('Imported Record')).toBeVisible()
  expect(document.body.textContent).toContain('0.0% of net sales')

  const callsBeforeBlankRoutes = mocks.routerPush.mock.calls.length
  await view.getByText('Loss Leader').click()
  await view.getByText('Neutral Item').click()
  await view.getByText('Neutral Customer').click()
  await view.getByText('Route-less Vendor').click()
  await view.getByText('Unknown Quantity').click()
  await view.getByText('Draft Transfer').click()
  await view.getByText('Imported Record').click()
  expect(mocks.routerPush).toHaveBeenCalledTimes(callsBeforeBlankRoutes)
})
