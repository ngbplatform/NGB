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
  useRouter: () => ({ push: mocks.routerPush }),
}))

vi.mock('../../../src/home/homeData', () => ({
  loadHomeDashboard: vi.fn(),
}))

vi.mock('@ngbplatform/ui', async () => {
  const { defineComponent, h } = await import('vue')

  const Badge = defineComponent({
    props: { tone: { type: String, default: 'neutral' } },
    setup(props, { slots }) {
      return () => h('span', { 'data-testid': `badge-${props.tone}` }, slots.default?.())
    },
  })
  const Toolbar = defineComponent({
    props: {
      modelValue: { type: String, required: true },
      loading: { type: Boolean, default: false },
    },
    emits: ['refresh', 'update:modelValue'],
    setup(props, { emit }) {
      return () => h('div', { 'data-testid': 'toolbar' }, [
        h('span', { 'data-testid': 'toolbar-as-of' }, props.modelValue),
        h('button', { type: 'button', onClick: () => emit('refresh') }, 'Refresh'),
        h('button', { type: 'button', onClick: () => emit('update:modelValue', '2026-05-01') }, 'Advance as-of'),
      ])
    },
  })
  const StatusBanner = defineComponent({
    props: {
      error: { type: String, default: null },
      warnings: { type: Array as () => string[], default: () => [] },
      errorTitle: { type: String, required: true },
    },
    setup(props) {
      return () => h('div', { 'data-testid': 'status-banner' }, [
        props.error ? h('div', props.errorTitle) : null,
        ...props.warnings.map((warning) => h('div', warning)),
      ])
    },
  })
  const Icon = defineComponent({
    props: { name: { type: String, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  })
  const PageHeader = defineComponent({
    props: { title: { type: String, required: true } },
    setup(props, { slots }) {
      return () => h('header', [
        h('h1', props.title),
        h('div', slots.secondary?.()),
        h('div', slots.actions?.()),
      ])
    },
  })

  return {
    formatDashboardCount: (value: number) => `${value}`,
    formatDashboardMoneyCompact: (value: number) => `$${value.toFixed(0)}`,
    formatDashboardPercent: (value: number) => `${value.toFixed(1)}%`,
    NgbBadge: Badge,
    NgbDashboardAsOfToolbar: Toolbar,
    NgbDashboardStatusBanner: StatusBanner,
    NgbIcon: Icon,
    NgbPageHeader: PageHeader,
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

import HomePage from '../../../src/pages/HomePage.vue'

function dashboard(overrides: Record<string, unknown> = {}) {
  return {
    warnings: [],
    asOf: '2026-04-30',
    monthKey: '2026-04',
    monthLabel: 'Apr 2026',
    pipelineAmount: 1000,
    weightedPipelineAmount: 400,
    leadCount: 8,
    qualifiedLeadCount: 5,
    convertedLeadCount: 2,
    quoteAmount: 750,
    quoteCount: 3,
    activityCount: 11,
    openOpportunities: [
      {
        opportunity: 'Expansion deal',
        account: 'Northwind',
        stage: 'Proposal',
        amount: 1000,
        weightedAmount: 400,
        route: '/documents/crm.opportunity_update/opportunity-1',
      },
    ],
    routes: {
      leads: '/documents/crm.lead_intake',
      pipeline: '/reports/crm.sales_pipeline',
      activities: '/reports/crm.activity_summary',
      quotes: '/reports/crm.quote_register',
      funnel: '/reports/crm.lead_conversion_funnel',
    },
    ...overrides,
  }
}

beforeEach(() => {
  mocks.routerPush.mockReset()
  mocks.refresh.mockReset()
  mocks.state = {
    asOf: ref('2026-04-30'),
    dashboard: ref(dashboard()),
    error: ref(null),
    loading: ref(false),
    warnings: ref(['Pipeline refresh is delayed']),
  }
})

test('renders CRM metrics and executes every dashboard navigation action', async () => {
  const view = await render(HomePage)

  await expect.element(view.getByText('8 leads · 3 quotes · 11 activities')).toBeVisible()
  await expect.element(view.getByText('Pipeline refresh is delayed')).toBeVisible()
  await expect.element(view.getByText('40.0% weighted coverage')).toBeVisible()
  await expect.element(view.getByText('Expansion deal')).toBeVisible()

  const labels = [
    'New Lead',
    'Update Opportunity',
    'Prepare Quote',
    'Pipeline',
    'Converted Leads',
    'Quote Amount',
    'Activities',
    'Expansion deal',
    'Leads',
    'Accounts',
    'Contacts',
    'Quotes',
  ]
  for (const label of labels)
    await view.getByRole('button', { name: new RegExp(label) }).first().click()

  expect(mocks.routerPush).toHaveBeenCalledWith('/documents/crm.lead_intake/new')
  expect(mocks.routerPush).toHaveBeenCalledWith('/documents/crm.opportunity_update/new')
  expect(mocks.routerPush).toHaveBeenCalledWith('/documents/crm.quote/new')
  expect(mocks.routerPush).toHaveBeenCalledWith('/documents/crm.opportunity_update/opportunity-1')
  expect(mocks.routerPush).toHaveBeenCalledWith('/catalogs/crm.account')
  expect(mocks.routerPush).toHaveBeenCalledWith('/catalogs/crm.contact')
})

test('renders empty and zero-value states with fallback routes and wires toolbar state', async () => {
  mocks.state.dashboard = ref(dashboard({
    monthLabel: undefined,
    pipelineAmount: 0,
    weightedPipelineAmount: 10,
    leadCount: 0,
    qualifiedLeadCount: 0,
    convertedLeadCount: 0,
    quoteAmount: 0,
    quoteCount: 0,
    activityCount: 0,
    openOpportunities: [],
    routes: undefined,
  }))
  mocks.state.warnings = ref([])

  const view = await render(HomePage)

  await expect.element(view.getByText('No posted opportunities yet.')).toBeVisible()
  await expect.element(view.getByText('Current period touchpoints')).toBeVisible()
  await expect.element(view.getByText('0.0% weighted coverage')).toBeVisible()

  await view.getByRole('button', { name: /^Pipeline/ }).first().click()
  await view.getByRole('button', { name: /^Leads/ }).click()
  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/crm.sales_pipeline')
  expect(mocks.routerPush).toHaveBeenCalledWith('/documents/crm.lead_intake')

  await view.getByTestId('toolbar').getByRole('button', { name: 'Refresh' }).click()
  expect(mocks.refresh).toHaveBeenCalledTimes(1)
  await view.getByTestId('toolbar').getByRole('button', { name: 'Advance as-of' }).click()
  await expect.element(view.getByTestId('toolbar-as-of')).toHaveTextContent('2026-05-01')
})

test('uses loading fallbacks while dashboard data is unavailable', async () => {
  mocks.state.dashboard = ref(null)
  mocks.state.error = ref('CRM endpoint unavailable')
  mocks.state.loading = ref(true)
  mocks.state.warnings = ref([])

  const view = await render(HomePage)

  await expect.element(view.getByText('CRM home data failed to load')).toBeVisible()
  await expect.element(view.getByText('As of 2026-04-30')).toBeVisible()
  expect(view.getByTestId('crm-home-kpis').element().querySelectorAll('button')).toHaveLength(0)
  await expect.element(view.getByText('No posted opportunities yet.')).toBeVisible()

  await view.getByRole('button', { name: /^Pipeline/ }).click()
  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/crm.sales_pipeline')
})

test('guards null and blank routes while falling back from a null opportunity route', async () => {
  mocks.state.dashboard = ref(dashboard({
    routes: {
      leads: '',
      pipeline: null,
      activities: ' ',
      quotes: '',
      funnel: null,
    },
    openOpportunities: [
      {
        opportunity: 'Unroutable deal',
        account: 'Unknown',
        stage: 'Draft',
        amount: 0,
        weightedAmount: 0,
        route: '',
      },
      {
        opportunity: 'Fallback deal',
        account: 'Unknown',
        stage: 'Draft',
        amount: 0,
        weightedAmount: 0,
        route: null,
      },
    ],
  }))

  const view = await render(HomePage)
  const callsBefore = mocks.routerPush.mock.calls.length

  await view.getByRole('button', { name: /Unroutable deal/ }).click()
  await view.getByRole('button', { name: /Fallback deal/ }).click()
  await view.getByRole('button', { name: /^Pipeline/ }).first().click()
  await view.getByRole('button', { name: /^Converted Leads/ }).click()
  await view.getByRole('button', { name: /^Activities/ }).click()
  expect(mocks.routerPush).toHaveBeenCalledTimes(callsBefore)
})
