import { defineComponent, h, ref } from 'vue'
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
  useRouter: () => ({ push: mocks.routerPush }),
}))

vi.mock('../../../src/home/homeData', () => ({
  loadHomeDashboard: vi.fn(),
}))

vi.mock('@ngbplatform/ui', () => ({
  buildAccountingPeriodClosingPath: ({ year }: { year: number }) => `/accounting/periods?year=${year}`,
  formatDashboardCount: (value: number) => String(value),
  formatDashboardMoneyCompact: (value: number) => `$${value}`,
  formatDashboardPercent: (value: number) => `${value.toFixed(1)}%`,
  NgbBadge: defineComponent({
    name: 'StubBadge',
    props: { tone: { type: String, default: 'neutral' } },
    setup(props, { slots }) {
      return () => h('span', { 'data-testid': `badge-${props.tone}` }, slots.default?.())
    },
  }),
  NgbDashboardAsOfToolbar: defineComponent({
    name: 'StubToolbar',
    props: { modelValue: { type: String, required: true }, loading: { type: Boolean, default: false } },
    emits: ['refresh', 'update:modelValue'],
    setup(props, { emit }) {
      return () => h('div', { 'data-testid': 'toolbar', 'data-loading': String(props.loading) }, [
        h('span', { 'data-testid': 'toolbar-as-of' }, props.modelValue),
        h('button', { type: 'button', onClick: () => emit('refresh') }, 'Refresh'),
        h('button', { type: 'button', onClick: () => emit('update:modelValue', '2027-01-15') }, 'Change as-of'),
      ])
    },
  }),
  NgbDashboardStatusBanner: defineComponent({
    name: 'StubStatusBanner',
    props: { error: { type: String, default: null }, warnings: { type: Array, default: () => [] }, errorTitle: { type: String, required: true } },
    setup(props) {
      return () => h('section', { 'data-testid': 'status-banner' }, [
        props.error ? h('span', `${props.errorTitle}: ${props.error}`) : null,
        ...(props.warnings as string[]).map((warning) => h('span', warning)),
      ])
    },
  }),
  NgbIcon: defineComponent({
    name: 'StubIcon',
    props: { name: { type: String, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  }),
  NgbPageHeader: defineComponent({
    name: 'StubPageHeader',
    props: { title: { type: String, required: true } },
    setup(props, { slots }) {
      return () => h('header', [h('h1', props.title), slots.secondary?.(), slots.actions?.()])
    },
  }),
  NgbTrendChart: defineComponent({
    name: 'StubTrendChart',
    props: { labels: { type: Array, default: () => [] }, series: { type: Array, default: () => [] }, mode: { type: String, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': `chart-${props.mode}` }, `${props.labels.length}:${props.series.length}`)
    },
  }),
  useDashboardPageState: (options: { resolveWarnings: (value: { warnings?: string[] } | null) => string[] }) => {
    options.resolveWarnings(null)
    options.resolveWarnings({ warnings: undefined })
    options.resolveWarnings({ warnings: ['resolved'] })
    return {
      asOf: mocks.state.asOf,
      dashboard: mocks.state.dashboard,
      error: mocks.state.error,
      loading: mocks.state.loading,
      refresh: mocks.refresh,
      warnings: mocks.state.warnings,
    }
  },
}))

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

function chart(title: string, route: string) {
  return {
    title,
    subtitle: `${title} subtitle`,
    labels: ['Jan', 'Feb'],
    series: [{ label: title, color: 'blue', values: [1, 2] }],
    route,
  }
}

function dashboard(overrides: Record<string, unknown> = {}) {
  return {
    warnings: [],
    asOf: '2026-08-23',
    monthKey: '2026-08',
    monthLabel: 'Aug 2026',
    portfolio: {
      buildingCount: 3,
      totalUnits: 20,
      occupiedUnits: 15,
      vacantUnits: 5,
      occupancyPercent: 75,
      futureOccupiedUnits: 17,
      futureOccupancyPercent: 85,
    },
    leases: {
      expiring30Count: 2,
      upcomingMoveInCount: 1,
      upcomingMoveOutCount: 1,
      events: [
        { kind: 'Move-in', date: '2026-08-24', leaseDisplay: 'Lease In', propertyDisplay: 'Building A', route: '/leases/in' },
        { kind: 'Move-out', date: '2026-08-25', leaseDisplay: 'Lease Out', propertyDisplay: 'Building B', route: '/leases/out' },
      ],
    },
    receivables: {
      totalOpenItemsNet: 1000,
      totalDiff: -25,
      rowCount: 4,
      mismatchRowCount: 2,
      currentMonthBilled: 500,
      currentMonthCollected: 400,
      mismatches: [
        { leaseDisplay: 'Lease GL', propertyDisplay: 'Building C', rowKind: 'GLOnly', diff: -20, route: '/mismatches/gl' },
        { leaseDisplay: 'Lease Matched', propertyDisplay: 'Building D', rowKind: 'Matched', diff: 5, route: '/mismatches/matched' },
      ],
    },
    maintenance: {
      openItemCount: 3,
      overdueCount: 1,
      agingBuckets: [],
      items: [
        { queueState: 'Overdue', subject: 'Broken boiler', requestDisplay: 'REQ-1', propertyDisplay: 'Building A', requestedAt: '2026-08-01', dueBy: '2026-08-20', agingDays: 22, assignedTo: null, route: '/maintenance/1' },
        { queueState: 'Requested', subject: 'Paint wall', requestDisplay: 'REQ-2', propertyDisplay: 'Building B', requestedAt: '2026-08-21', dueBy: null, agingDays: 2, assignedTo: null, route: null },
        { queueState: 'Scheduled', subject: 'Inspect roof', requestDisplay: 'REQ-3', propertyDisplay: 'Building C', requestedAt: null, dueBy: null, agingDays: 0, assignedTo: null, route: '/maintenance/3' },
      ],
    },
    periods: { pendingCloseCount: 1, lastClosedPeriod: 'Jul 2026', nextClosablePeriod: 'Aug 2026', firstGapPeriod: null },
    charts: {
      collections: chart('Collections chart', '/charts/collections'),
      occupancy: chart('Occupancy chart', '/charts/occupancy'),
      maintenanceAging: chart('Maintenance chart', '/charts/maintenance'),
    },
    ...overrides,
  }
}

beforeEach(() => {
  mocks.routerPush.mockReset()
  mocks.refresh.mockReset()
  mocks.state = {
    asOf: ref('2026-08-23'),
    dashboard: ref(dashboard()),
    error: ref(null),
    loading: ref(false),
    warnings: ref(['Some sources are delayed']),
  }
})

test('renders the complete dashboard and opens every actionable workflow', async () => {
  const view = await render(HomePage)

  await expect.element(view.getByText('3 buildings · 20 units · 15 occupied')).toBeVisible()
  await expect.element(view.getByText('Some sources are delayed')).toBeVisible()
  await expect.element(view.getByText('80.0%')).toBeVisible()
  await expect.element(view.getByText('No date')).toBeVisible()

  for (const name of [
    'Open receivables',
    'Reconciliation mismatches',
    'Lease expirations in 30 days',
    'Vacant units and turns',
    'Overdue maintenance',
    'Open periods not closed',
    'Collections chart',
    'Occupancy chart',
    'Maintenance chart',
    'Lease In',
    'Lease Out',
    'Broken boiler',
    'Paint wall',
    'Inspect roof',
    'Lease GL',
    'Lease Matched',
  ]) {
    await view.getByRole('button', { name: new RegExp(name) }).click()
  }

  expect(mocks.routerPush).toHaveBeenCalledWith('/receivables/reconciliation?fromMonth=2026-08&toMonth=2026-08&mode=Balance')
  expect(mocks.routerPush).toHaveBeenCalledWith('/receivables/reconciliation?fromMonth=2026-08&toMonth=2026-08&mode=Balance&status=mismatch')
  expect(mocks.routerPush).toHaveBeenCalledWith('/accounting/periods?year=2026')
  expect(mocks.routerPush).toHaveBeenCalledWith('/charts/maintenance')
  expect(mocks.routerPush).not.toHaveBeenCalledWith(null)
})

test('renders all zero and empty states while refreshing an existing dashboard', async () => {
  mocks.state.dashboard = ref(dashboard({
    monthLabel: '',
    portfolio: { buildingCount: 0, totalUnits: 0, occupiedUnits: 0, vacantUnits: 0, occupancyPercent: 0, futureOccupiedUnits: 0, futureOccupancyPercent: 0 },
    leases: { expiring30Count: 0, upcomingMoveInCount: 0, upcomingMoveOutCount: 0, events: [] },
    receivables: { totalOpenItemsNet: 0, totalDiff: 0, rowCount: 0, mismatchRowCount: 0, currentMonthBilled: 0, currentMonthCollected: 10, mismatches: [] },
    maintenance: { openItemCount: 0, overdueCount: 0, agingBuckets: [], items: [] },
    periods: { pendingCloseCount: 0, lastClosedPeriod: null, nextClosablePeriod: null, firstGapPeriod: null },
  }))
  mocks.state.loading = ref(true)
  mocks.state.warnings = ref([])

  const view = await render(HomePage)

  await expect.element(view.getByText('Refreshing live portfolio signals…')).toBeVisible()
  await expect.element(view.getByText('No closed month yet')).toBeVisible()
  await expect.element(view.getByText('No move-ins or move-outs are scheduled in the next 14 days.')).toBeVisible()
  await expect.element(view.getByText('No open maintenance items right now.')).toBeVisible()
  await expect.element(view.getByText('Receivables are currently aligned for the selected month.')).toBeVisible()
  await expect.element(view.getByText('0.0%').first()).toBeVisible()
})

test('renders all skeleton groups when the initial dashboard is loading', async () => {
  mocks.state.dashboard = ref(null)
  mocks.state.loading = ref(true)
  mocks.state.error = ref('Service unavailable')

  const view = await render(HomePage)

  await expect.element(view.getByText('Home data failed to load: Service unavailable')).toBeVisible()
  await expect.element(view.getByText('As of 2026-08-23')).toBeVisible()
  await expect.element(view.getByTestId('home-attention-skeleton')).toBeVisible()
  await expect.element(view.getByTestId('home-kpi-skeleton')).toBeVisible()
  await expect.element(view.getByTestId('home-chart-skeleton')).toBeVisible()
  await expect.element(view.getByTestId('home-snapshot-skeleton')).toBeVisible()
})

test('renders the non-loading no-data boundary and wires toolbar model and refresh', async () => {
  mocks.state.dashboard = ref(null)
  mocks.state.loading = ref(false)

  const view = await render(HomePage)

  await expect.element(view.getByText('Operational focus for the selected period')).toBeVisible()
  expect(document.querySelector('[data-testid="home-attention-grid"]')?.children).toHaveLength(0)
  expect(document.querySelector('[data-testid="home-kpi-grid"]')?.children).toHaveLength(0)

  await view.getByRole('button', { name: 'Refresh' }).click()
  await view.getByRole('button', { name: 'Change as-of' }).click()
  expect(mocks.refresh).toHaveBeenCalledOnce()
  await expect.element(view.getByTestId('toolbar-as-of')).toHaveTextContent('2027-01-15')
})
