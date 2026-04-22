import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { RouterView, createMemoryHistory, createRouter, useRoute } from 'vue-router'
import { defineComponent, h, nextTick } from 'vue'

import { StubDatePicker, StubIcon, StubVChart } from './stubs'

vi.mock('../../../../src/ngb/primitives/NgbDatePicker.vue', () => ({
  default: StubDatePicker,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

vi.mock('vue-echarts', () => ({
  default: StubVChart,
}))

import NgbDashboardAsOfToolbar from '../../../../src/ngb/site/NgbDashboardAsOfToolbar.vue'
import NgbDashboardStatusBanner from '../../../../src/ngb/site/NgbDashboardStatusBanner.vue'
import NgbTrendChart from '../../../../src/ngb/site/NgbTrendChart.vue'
import { useDashboardPageState } from '../../../../src/ngb/site/useDashboardPageState'

type DashboardDto = {
  warnings?: string[]
  labels: string[]
  series: Array<{
    label: string
    color: string
    values: number[]
  }>
}

const dashboardLoad = vi.fn<(asOf: string) => Promise<DashboardDto>>()

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })

  return { promise, resolve, reject }
}

function readJson(locator: { element(): Element }): Record<string, unknown> {
  return JSON.parse(locator.element().textContent ?? '{}') as Record<string, unknown>
}

async function flushUi() {
  await Promise.resolve()
  await nextTick()
  await Promise.resolve()
}

const DashboardPageHarness = defineComponent({
  setup() {
    const route = useRoute()
    const state = useDashboardPageState<DashboardDto>({
      load: (asOf) => dashboardLoad(asOf),
      fallbackAsOf: () => '2026-04-01',
    })

    return () => h('div', { class: 'grid gap-4 p-4' }, [
      h(NgbDashboardAsOfToolbar, {
        modelValue: state.asOf.value,
        loading: state.loading.value,
        'onUpdate:modelValue': (value: string | null) => {
          state.asOf.value = value ?? ''
        },
        onRefresh: state.refresh,
      }),
      h('div', { 'data-testid': 'dashboard-route' }, route.fullPath),
      h('div', { 'data-testid': 'dashboard-as-of' }, `as-of:${state.asOf.value}`),
      h(NgbDashboardStatusBanner, {
        error: state.error.value,
        warnings: state.warnings.value,
        warningTitle: 'Partial dashboard data',
      }),
      state.dashboard.value
        ? h('div', { class: 'h-[280px] w-[520px]' }, [
            h(NgbTrendChart, {
              labels: state.dashboard.value.labels,
              series: state.dashboard.value.series,
            }),
          ])
        : null,
    ])
  },
})

async function renderDashboard(initialPath = '/dashboard?asOf=2026-04-08') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/dashboard',
        component: DashboardPageHarness,
      },
    ],
  })

  await router.push(initialPath)
  await router.isReady()

  const view = await render(defineComponent({
    setup() {
      return () => h(RouterView)
    },
  }), {
    global: {
      plugins: [router],
    },
  })

  return { view, router }
}

beforeEach(() => {
  vi.clearAllMocks()
  dashboardLoad.mockReset()
})

test('wires the dashboard route query, toolbar, warnings banner, and trend chart together', async () => {
  await page.viewport(1280, 900)

  const first = createDeferred<DashboardDto>()
  const second = createDeferred<DashboardDto>()

  dashboardLoad
    .mockImplementationOnce(() => first.promise)
    .mockImplementationOnce(() => second.promise)

  const { view, router } = await renderDashboard()

  await expect.element(view.getByTestId('dashboard-as-of')).toHaveTextContent('as-of:2026-04-08')

  first.resolve({
    warnings: [' Late data ', 'Stale occupancy cache', 'Late data'],
    labels: ['Jan', 'Feb'],
    series: [
      { label: 'Occupancy', color: '#2563eb', values: [94, 96] },
    ],
  })
  await flushUi()

  await expect.element(view.getByText('Partial dashboard data')).toBeVisible()
  await expect.element(view.getByText('Late data')).toBeVisible()
  await expect.element(view.getByText('Stale occupancy cache')).toBeVisible()

  let option = readJson(view.getByTestId('stub-vchart-option'))
  let series = option.series as Array<Record<string, unknown>>
  expect(series[0]?.data).toEqual([94, 96])

  const dateInput = view.getByTestId('stub-date-picker').element() as HTMLInputElement
  dateInput.value = '2026-04-30'
  dateInput.dispatchEvent(new Event('input', { bubbles: true }))
  dateInput.dispatchEvent(new Event('change', { bubbles: true }))

  await expect.poll(() => router.currentRoute.value.fullPath).toBe('/dashboard?asOf=2026-04-30')
  await expect.element(view.getByTestId('dashboard-route')).toHaveTextContent('/dashboard?asOf=2026-04-30')

  second.resolve({
    warnings: ['Blocked ledger sync'],
    labels: ['Mar', 'Apr'],
    series: [
      { label: 'Occupancy', color: '#2563eb', values: [97, 98] },
    ],
  })
  await flushUi()

  await expect.element(view.getByText('Blocked ledger sync')).toBeVisible()
  option = readJson(view.getByTestId('stub-vchart-option'))
  series = option.series as Array<Record<string, unknown>>
  expect(series[0]?.data).toEqual([97, 98])
  expect(dashboardLoad).toHaveBeenCalledWith('2026-04-08')
  expect(dashboardLoad).toHaveBeenLastCalledWith('2026-04-30')
})

test('ignores stale browser-level dashboard loads and surfaces refresh failures through the banner', async () => {
  await page.viewport(1280, 900)

  const first = createDeferred<DashboardDto>()
  const second = createDeferred<DashboardDto>()

  dashboardLoad
    .mockImplementationOnce(() => first.promise)
    .mockImplementationOnce(() => second.promise)
    .mockRejectedValueOnce(new Error('Dashboard exploded'))

  const { view } = await renderDashboard()

  const dateInput = view.getByTestId('stub-date-picker').element() as HTMLInputElement
  dateInput.value = '2026-04-09'
  dateInput.dispatchEvent(new Event('input', { bubbles: true }))
  dateInput.dispatchEvent(new Event('change', { bubbles: true }))

  second.resolve({
    warnings: ['Fresh warning'],
    labels: ['Jan', 'Feb'],
    series: [
      { label: 'Occupancy', color: '#2563eb', values: [95, 99] },
    ],
  })
  await flushUi()

  first.resolve({
    warnings: ['Stale warning'],
    labels: ['Jan', 'Feb'],
    series: [
      { label: 'Occupancy', color: '#2563eb', values: [10, 20] },
    ],
  })
  await flushUi()

  await expect.element(view.getByText('Fresh warning')).toBeVisible()
  expect(document.body.textContent ?? '').not.toContain('Stale warning')

  const option = readJson(view.getByTestId('stub-vchart-option'))
  const series = option.series as Array<Record<string, unknown>>
  expect(series[0]?.data).toEqual([95, 99])

  await view.getByRole('button', { name: 'Refresh' }).click()
  await flushUi()
  await flushUi()

  await expect.element(view.getByText('Dashboard data failed to load')).toBeVisible()
  await expect.element(view.getByText('Dashboard exploded')).toBeVisible()
  expect(document.querySelector('[data-testid="stub-vchart"]')).toBeNull()
})
