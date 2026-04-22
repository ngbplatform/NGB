<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { BarChart, LineChart } from 'echarts/charts'
import { CanvasRenderer } from 'echarts/renderers'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'

use([CanvasRenderer, LineChart, BarChart, GridComponent, TooltipComponent, LegendComponent])

export type NgbTrendChartSeries = {
  label: string
  color: string
  values: number[]
}

const props = withDefaults(defineProps<{
  labels: string[]
  series: NgbTrendChartSeries[]
  mode?: 'line' | 'bar'
}>(), {
  mode: 'line',
})

const themeVersion = ref(0)
let themeObserver: MutationObserver | null = null

const pointCount = computed(() => {
  const seriesMax = Math.max(0, ...props.series.map((entry) => entry.values.length))
  return Math.max(props.labels.length, seriesMax, 1)
})

const normalizedSeries = computed(() =>
  props.series.map((entry) => ({
    ...entry,
    values: Array.from({ length: pointCount.value }, (_, index) => {
      const raw = Number(entry.values[index] ?? 0)
      return Number.isFinite(raw) ? raw : 0
    }),
  })),
)

function readCssVar(name: string, fallback: string): string {
  themeVersion.value
  if (typeof window === 'undefined') return fallback
  const value = window.getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  return value.length > 0 ? value : fallback
}

function resolveColor(value: string): string {
  const match = String(value ?? '').trim().match(/^var\((--[^)]+)\)$/)
  return match ? readCssVar(match[1], value) : value
}

function formatCompactNumber(value: number): string {
  const numeric = Number(value ?? 0)
  if (!Number.isFinite(numeric)) return '0'
  const abs = Math.abs(numeric)
  if (abs >= 1_000_000) return `${numeric < 0 ? '-' : ''}${(abs / 1_000_000).toFixed(1)}M`
  if (abs >= 1_000) return `${numeric < 0 ? '-' : ''}${(abs / 1_000).toFixed(1)}K`
  if (abs >= 100) return `${Math.round(numeric)}`
  if (abs % 1 > 0.001) return numeric.toFixed(1)
  return `${Math.round(numeric)}`
}

const palette = computed(() => ({
  text: readCssVar('--ngb-text', '#1F2933'),
  muted: readCssVar('--ngb-muted', '#4B5563'),
  border: readCssVar('--ngb-border', '#CBD5E1'),
  card: readCssVar('--ngb-card', '#FFFFFF'),
  background: readCssVar('--ngb-bg', '#F5F7FA'),
}))

const chartOptions = computed(() => ({
  animationDuration: 420,
  animationDurationUpdate: 260,
  color: normalizedSeries.value.map((entry) => resolveColor(entry.color)),
  legend: {
    show: normalizedSeries.value.length > 1,
    top: 0,
    right: 0,
    icon: 'roundRect',
    itemWidth: 14,
    itemHeight: 8,
    textStyle: {
      color: palette.value.text,
      fontSize: 12,
      fontWeight: 600,
    },
  },
  tooltip: {
    trigger: 'axis',
    confine: true,
    backgroundColor: palette.value.card,
    borderColor: palette.value.border,
    borderWidth: 1,
    textStyle: {
      color: palette.value.text,
      fontSize: 12,
    },
    axisPointer: props.mode === 'bar'
      ? {
          type: 'shadow',
          shadowStyle: {
            color: 'rgba(148, 163, 184, 0.12)',
          },
        }
      : {
          type: 'line',
          lineStyle: {
            color: 'rgba(148, 163, 184, 0.42)',
            width: 1,
          },
        },
    formatter: (params: Array<{ axisValueLabel?: string; seriesName?: string; value?: number; color?: string }>) => {
      const rows = Array.isArray(params) ? params : [params]
      const title = rows[0]?.axisValueLabel ?? ''
      const body = rows.map((row) => {
        const color = String(row?.color ?? palette.value.border)
        const label = String(row?.seriesName ?? '').trim()
        const value = formatCompactNumber(Number(row?.value ?? 0))
        return `<div style="display:flex;align-items:center;justify-content:space-between;gap:16px;min-width:160px;"><span style="display:inline-flex;align-items:center;gap:8px;color:${palette.value.text};"><span style="display:inline-block;width:8px;height:8px;border-radius:999px;background:${color};"></span>${label}</span><strong style="color:${palette.value.text};font-weight:700;">${value}</strong></div>`
      }).join('')
      return `<div style="display:grid;gap:8px;"><div style="font-weight:700;color:${palette.value.text};">${title}</div>${body}</div>`
    },
  },
  grid: {
    top: normalizedSeries.value.length > 1 ? 48 : 20,
    right: 10,
    bottom: 10,
    left: 6,
    containLabel: true,
  },
  xAxis: {
    type: 'category',
    boundaryGap: props.mode === 'bar',
    data: props.labels,
    axisTick: { show: false },
    axisLine: {
      lineStyle: {
        color: palette.value.border,
      },
    },
    axisLabel: {
      color: palette.value.muted,
      fontSize: 11,
      margin: 12,
    },
  },
  yAxis: {
    type: 'value',
    axisLine: { show: false },
    axisTick: { show: false },
    splitLine: {
      lineStyle: {
        color: palette.value.border,
        opacity: 0.55,
        type: 'dashed',
      },
    },
    axisLabel: {
      color: palette.value.muted,
      fontSize: 11,
      formatter: (value: number) => formatCompactNumber(value),
    },
  },
  series: normalizedSeries.value.map((entry, index) => props.mode === 'bar'
    ? {
        name: entry.label,
        type: 'bar',
        data: entry.values,
        barMaxWidth: 38,
        itemStyle: {
          borderRadius: 0,
        },
        emphasis: {
          focus: 'series',
        },
      }
    : {
        name: entry.label,
        type: 'line',
        data: entry.values,
        smooth: 0.32,
        showSymbol: false,
        symbol: 'circle',
        symbolSize: 7,
        lineStyle: {
          width: 3,
        },
        areaStyle: {
          opacity: index === 0 ? 0.16 : 0.1,
        },
        emphasis: {
          focus: 'series',
        },
      }),
}))

onMounted(() => {
  themeObserver = new MutationObserver(() => {
    themeVersion.value += 1
  })

  themeObserver.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['class', 'style'],
  })
})

onBeforeUnmount(() => {
  themeObserver?.disconnect()
  themeObserver = null
})
</script>

<template>
  <VChart
    class="h-full w-full"
    :option="chartOptions"
    :init-options="{ renderer: 'canvas' }"
    autoresize
  />
</template>
