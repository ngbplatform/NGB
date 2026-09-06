import { afterEach, beforeEach, expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbTrendChart from '../../../../src/ngb/site/NgbTrendChart.vue'

const TrendLineHarness = defineComponent({
  setup() {
    return () => h('div', { class: 'h-[280px] w-[520px]' }, [
      h(NgbTrendChart, {
        labels: ['Jan', 'Feb', 'Mar'],
        series: [
          { label: 'Revenue', color: 'var(--accent-color)', values: [1200, Number.NaN] },
          { label: 'Expenses', color: '#f97316', values: [300, 450, 500] },
          { label: 'Fallback', color: 'var(--missing-color)', values: [] },
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
          { label: 'Vacancy', color: '#2563eb', values: [4, -2] },
        ],
      }),
    ])
  },
})

const EmptyTrendHarness = defineComponent({
  setup() {
    return () => h('div', { class: 'h-[280px] w-[520px]' }, [
      h(NgbTrendChart, {
        labels: [],
        series: [],
      }),
    ])
  },
})

const LongTrendHarness = defineComponent({
  setup() {
    const fifteenLabels = Array.from({ length: 15 }, (_, index) => `L${index + 1}`)
    const sixteenLabels = Array.from({ length: 16 }, (_, index) => `M${index + 1}`)

    return () => h('div', { class: 'grid h-[560px] w-[520px] grid-rows-2' }, [
      h(NgbTrendChart, {
        labels: fifteenLabels,
        series: [
          {
            label: 'Large values',
            color: '',
            values: [1_500_000, -1_500_000, -1_200, 12.34],
          },
        ],
      }),
      h(NgbTrendChart, {
        labels: sixteenLabels,
        series: [
          {
            label: 'Runtime fallback color',
            color: null as never,
            values: [100],
          },
        ],
      }),
    ])
  },
})

beforeEach(() => {
  document.documentElement.style.setProperty('--accent-color', '#0f766e')
  document.documentElement.style.setProperty('--ngb-muted', '#486581')
})

afterEach(() => {
  document.documentElement.removeAttribute('style')
})

test('normalizes line data, resolves CSS variables, and exposes accessible point values', async () => {
  const view = await render(TrendLineHarness)

  await expect.element(view.getByRole('img', { name: 'Line chart: Revenue, Expenses, Fallback' })).toBeVisible()
  const series = view.container.querySelectorAll('[data-testid="ngb-trend-series"]')
  expect(series).toHaveLength(3)
  expect(series[0]?.getAttribute('data-series-values')).toBe('[1200,0,0]')
  expect(series[1]?.getAttribute('data-series-values')).toBe('[300,450,500]')
  expect(series[2]?.getAttribute('data-series-values')).toBe('[0,0,0]')
  expect(series[0]?.getAttribute('data-series-color')).toBe('var(--accent-color, #2563eb)')
  expect(series[2]?.getAttribute('data-series-color')).toBe('var(--missing-color, #2563eb)')
  expect(view.container.querySelectorAll('polyline')).toHaveLength(3)
  expect(view.container.querySelectorAll('circle')).toHaveLength(9)
  expect(view.container.querySelector('title')?.textContent).toContain('Jan — Revenue: 1.2K')
})

test('renders grouped bars across positive and negative values and follows theme variables', async () => {
  const view = await render(TrendBarHarness)

  await expect.element(view.getByRole('img', { name: 'Bar chart: Vacancy' })).toBeVisible()
  const bars = view.container.querySelectorAll('rect')
  expect(bars).toHaveLength(2)
  expect(Number(bars[0]?.getAttribute('height'))).toBeGreaterThan(0)
  expect(Number(bars[1]?.getAttribute('height'))).toBeGreaterThan(0)
  expect(bars[1]?.querySelector('title')?.textContent).toContain('Feb — Vacancy: -2')

  const axisLabel = view.container.querySelector('text')
  expect(getComputedStyle(axisLabel!).fill).toBe('rgb(72, 101, 129)')
  document.documentElement.style.setProperty('--ngb-muted', '#f8fafc')
  expect(getComputedStyle(axisLabel!).fill).toBe('rgb(248, 250, 252)')
})

test('keeps an empty chart renderable and accessible without series nodes', async () => {
  const view = await render(EmptyTrendHarness)

  await expect.element(view.getByRole('img', { name: 'Line chart: no data' })).toBeVisible()
  expect(view.container.querySelectorAll('[data-testid="ngb-trend-series"]')).toHaveLength(0)
  expect(view.container.querySelectorAll('text').length).toBeGreaterThan(0)
})

test('bounds long axes, includes the final label, and formats compact signed values', async () => {
  const view = await render(LongTrendHarness)
  const charts = view.container.querySelectorAll('[data-testid="ngb-trend-chart"]')

  expect(charts).toHaveLength(2)
  expect(charts[0]?.textContent).toContain('L15')
  expect(charts[1]?.textContent).toContain('M16')
  expect(charts[0]?.textContent).toContain('1.5M')
  expect(charts[0]?.textContent).toContain('-1.5M')
  expect(charts[0]?.textContent).toContain('-1.2K')
  expect(charts[0]?.textContent).toContain('12.3')

  const series = view.container.querySelectorAll('[data-testid="ngb-trend-series"]')
  expect(series[0]?.getAttribute('data-series-color')).toBe('#2563eb')
  expect(series[1]?.getAttribute('data-series-color')).toBe('#2563eb')
})
