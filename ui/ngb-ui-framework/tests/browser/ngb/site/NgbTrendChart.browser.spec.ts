import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import { StubVChart } from './stubs'

vi.mock('vue-echarts', () => ({
  default: StubVChart,
}))

import NgbTrendChart from '../../../../src/ngb/site/NgbTrendChart.vue'

function readJson(locator: { element(): Element }): Record<string, unknown> {
  return JSON.parse(locator.element().textContent ?? '{}') as Record<string, unknown>
}

async function flushUi() {
  await Promise.resolve()
  await Promise.resolve()
}

const TrendLineHarness = defineComponent({
  setup() {
    return () => h('div', { class: 'h-[280px] w-[520px]' }, [
      h(NgbTrendChart, {
        labels: ['Jan', 'Feb', 'Mar'],
        series: [
          { label: 'Revenue', color: 'var(--accent-color)', values: [1200, Number.NaN] },
          { label: 'Expenses', color: '#f97316', values: [300, 450, 500] },
        ],
      }),
    ])
  },
})

const TrendBarHarness = defineComponent({
  setup() {
    return () => h('div', { class: 'h-[280px] w-[520px]' }, [
      h(NgbTrendChart, {
        labels: ['Jan', 'Feb'],
        mode: 'bar',
        series: [
          { label: 'Vacancy', color: '#2563eb', values: [4, 2] },
        ],
      }),
    ])
  },
})

beforeEach(() => {
  document.documentElement.style.setProperty('--accent-color', '#0f766e')
  document.documentElement.style.setProperty('--ngb-text', '#102a43')
  document.documentElement.style.setProperty('--ngb-muted', '#486581')
  document.documentElement.style.setProperty('--ngb-border', '#d9e2ec')
  document.documentElement.style.setProperty('--ngb-card', '#ffffff')
  document.documentElement.style.setProperty('--ngb-bg', '#f8fafc')
})

afterEach(() => {
  document.documentElement.removeAttribute('style')
  document.documentElement.classList.remove('dark')
})

test('normalizes series data and resolves CSS variable colors for line charts', async () => {
  const view = await render(TrendLineHarness)

  await expect.element(view.getByTestId('stub-vchart')).toBeVisible()

  const option = readJson(view.getByTestId('stub-vchart-option'))
  const colors = option.color as string[]
  const series = option.series as Array<Record<string, unknown>>
  const legend = option.legend as Record<string, unknown>

  expect(colors).toEqual(['#0f766e', '#f97316'])
  expect(legend.show).toBe(true)
  expect(series[0]?.type).toBe('line')
  expect(series[0]?.data).toEqual([1200, 0, 0])
  expect(series[1]?.data).toEqual([300, 450, 500])
  expect(readJson(view.getByTestId('stub-vchart-init-options'))).toEqual({ renderer: 'canvas' })
  await expect.element(view.getByTestId('stub-vchart-autoresize')).toHaveTextContent('true')
})

test('switches to bar semantics and refreshes palette when theme variables change', async () => {
  const view = await render(TrendBarHarness)

  await expect.element(view.getByTestId('stub-vchart')).toBeVisible()

  let option = readJson(view.getByTestId('stub-vchart-option'))
  let tooltip = option.tooltip as Record<string, unknown>
  let axisPointer = tooltip.axisPointer as Record<string, unknown>
  let xAxis = option.xAxis as Record<string, unknown>
  let series = option.series as Array<Record<string, unknown>>
  let legend = option.legend as Record<string, unknown>
  let textStyle = legend.textStyle as Record<string, unknown>

  expect(axisPointer.type).toBe('shadow')
  expect(xAxis.boundaryGap).toBe(true)
  expect(series[0]?.type).toBe('bar')
  expect(textStyle.color).toBe('#102a43')

  document.documentElement.style.setProperty('--ngb-text', '#f8fafc')
  document.documentElement.classList.add('dark')
  await flushUi()

  option = readJson(view.getByTestId('stub-vchart-option'))
  const nextLegend = option.legend as Record<string, unknown>
  const nextTextStyle = nextLegend.textStyle as Record<string, unknown>
  expect(nextTextStyle.color).toBe('#f8fafc')
})
