<script setup lang="ts">
import { computed } from 'vue'

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

const viewWidth = 1_000
const viewHeight = 320
const left = 72
const right = 24
const bottom = 42

const top = computed(() => props.series.length > 1 ? 50 : 20)
const plotWidth = viewWidth - left - right
const plotHeight = computed(() => viewHeight - top.value - bottom)

const pointCount = computed(() => {
  const seriesMax = Math.max(0, ...props.series.map((entry) => entry.values.length))
  return Math.max(props.labels.length, seriesMax, 1)
})

const labels = computed(() =>
  Array.from({ length: pointCount.value }, (_, index) => String(props.labels[index] ?? '')),
)

const normalizedSeries = computed(() =>
  props.series.map((entry) => ({
    ...entry,
    color: normalizeColor(entry.color),
    values: Array.from({ length: pointCount.value }, (_, index) => {
      const raw = Number(entry.values[index] ?? 0)
      return Number.isFinite(raw) ? raw : 0
    }),
  })),
)

const extent = computed(() => {
  const values = normalizedSeries.value.flatMap((entry) => entry.values)
  const minimum = Math.min(0, ...values)
  const maximum = Math.max(0, ...values)
  if (minimum === maximum) return { minimum, maximum: minimum + 1 }
  return { minimum, maximum }
})

const ticks = computed(() => Array.from({ length: 5 }, (_, index) => {
  const ratio = index / 4
  const value = extent.value.maximum - ((extent.value.maximum - extent.value.minimum) * ratio)
  return { value, y: top.value + (plotHeight.value * ratio) }
}))

const visibleLabelIndexes = computed(() => {
  const count = pointCount.value
  if (count <= 10) return Array.from({ length: count }, (_, index) => index)
  const step = Math.ceil(count / 8)
  const indexes = Array.from({ length: count }, (_, index) => index)
    .filter((index) => index % step === 0)
  if (indexes.at(-1) !== count - 1) indexes.push(count - 1)
  return indexes
})

const ariaLabel = computed(() => {
  const kind = props.mode === 'bar' ? 'Bar chart' : 'Line chart'
  const names = normalizedSeries.value.map((entry) => entry.label).filter(Boolean).join(', ')
  return names ? `${kind}: ${names}` : `${kind}: no data`
})

function normalizeColor(value: string): string {
  const normalized = String(value ?? '').trim()
  const variable = normalized.match(/^var\((--[^,)]+)\)$/)
  if (variable) return `var(${variable[1]}, #2563eb)`
  return normalized || '#2563eb'
}

function formatCompactNumber(value: number): string {
  const numeric = Number(value)
  if (!Number.isFinite(numeric)) return '0'
  const abs = Math.abs(numeric)
  if (abs >= 1_000_000) return `${numeric < 0 ? '-' : ''}${(abs / 1_000_000).toFixed(1)}M`
  if (abs >= 1_000) return `${numeric < 0 ? '-' : ''}${(abs / 1_000).toFixed(1)}K`
  if (abs >= 100) return `${Math.round(numeric)}`
  if (abs % 1 > 0.001) return numeric.toFixed(1)
  return `${Math.round(numeric)}`
}

function xFor(index: number): number {
  if (pointCount.value <= 1) return left + (plotWidth / 2)
  return left + ((plotWidth * index) / (pointCount.value - 1))
}

function yFor(value: number): number {
  const range = extent.value.maximum - extent.value.minimum
  return top.value + (((extent.value.maximum - value) / range) * plotHeight.value)
}

function linePoints(values: number[]): string {
  return values.map((value, index) => `${xFor(index)},${yFor(value)}`).join(' ')
}

function areaPoints(values: number[]): string {
  const baseline = yFor(0)
  return `${left},${baseline} ${linePoints(values)} ${xFor(pointCount.value - 1)},${baseline}`
}

function barMetrics(seriesIndex: number, valueIndex: number, value: number) {
  const slot = plotWidth / pointCount.value
  const groupWidth = Math.min(slot * 0.72, 72)
  const width = Math.max(2, groupWidth / Math.max(normalizedSeries.value.length, 1))
  const groupLeft = left + (slot * valueIndex) + ((slot - groupWidth) / 2)
  const zeroY = yFor(0)
  const valueY = yFor(value)
  return {
    x: groupLeft + (seriesIndex * width),
    y: Math.min(zeroY, valueY),
    width: Math.max(width - 2, 1),
    height: Math.max(Math.abs(zeroY - valueY), 1),
  }
}
</script>

<template>
  <div class="relative h-full min-h-[12rem] w-full" data-testid="ngb-trend-chart">
    <div
      v-if="normalizedSeries.length > 1"
      class="pointer-events-none absolute right-2 top-0 z-[1] flex max-w-[80%] flex-wrap justify-end gap-x-4 gap-y-1 text-xs font-semibold text-ngb-text"
      aria-hidden="true"
    >
      <span v-for="entry in normalizedSeries" :key="entry.label" class="inline-flex items-center gap-1.5">
        <span class="h-2 w-3 rounded-sm" :style="{ backgroundColor: entry.color }" />
        {{ entry.label }}
      </span>
    </div>

    <svg
      class="h-full w-full overflow-visible"
      :viewBox="`0 0 ${viewWidth} ${viewHeight}`"
      preserveAspectRatio="none"
      role="img"
      :aria-label="ariaLabel"
    >
      <g aria-hidden="true">
        <g v-for="tick in ticks" :key="tick.y">
          <line
            :x1="left"
            :x2="viewWidth - right"
            :y1="tick.y"
            :y2="tick.y"
            stroke="var(--ngb-border, #cbd5e1)"
            stroke-dasharray="5 5"
            stroke-opacity="0.65"
          />
          <text
            :x="left - 12"
            :y="tick.y + 4"
            text-anchor="end"
            font-size="12"
            fill="var(--ngb-muted, #4b5563)"
          >{{ formatCompactNumber(tick.value) }}</text>
        </g>

        <line
          :x1="left"
          :x2="viewWidth - right"
          :y1="yFor(0)"
          :y2="yFor(0)"
          stroke="var(--ngb-border, #cbd5e1)"
        />

        <text
          v-for="index in visibleLabelIndexes"
          :key="index"
          :x="xFor(index)"
          :y="viewHeight - 12"
          text-anchor="middle"
          font-size="12"
          fill="var(--ngb-muted, #4b5563)"
        >{{ labels[index] }}</text>
      </g>

      <g
        v-for="(entry, seriesIndex) in normalizedSeries"
        :key="entry.label"
        data-testid="ngb-trend-series"
        :data-series-label="entry.label"
        :data-series-values="JSON.stringify(entry.values)"
        :data-series-color="entry.color"
      >
        <template v-if="mode === 'line'">
          <polygon
            :points="areaPoints(entry.values)"
            :fill="entry.color"
            :fill-opacity="seriesIndex === 0 ? 0.14 : 0.08"
            aria-hidden="true"
          />
          <polyline
            :points="linePoints(entry.values)"
            fill="none"
            :stroke="entry.color"
            stroke-width="3"
            vector-effect="non-scaling-stroke"
            aria-hidden="true"
          />
          <circle
            v-for="(value, valueIndex) in entry.values"
            :key="valueIndex"
            :cx="xFor(valueIndex)"
            :cy="yFor(value)"
            r="7"
            :fill="entry.color"
            stroke="var(--ngb-card, #fff)"
            stroke-width="2"
            vector-effect="non-scaling-stroke"
          >
            <title>{{ labels[valueIndex] }} — {{ entry.label }}: {{ formatCompactNumber(value) }}</title>
          </circle>
        </template>
        <rect
          v-for="(value, valueIndex) in mode === 'bar' ? entry.values : []"
          :key="valueIndex"
          v-bind="barMetrics(seriesIndex, valueIndex, value)"
          :fill="entry.color"
        >
          <title>{{ labels[valueIndex] }} — {{ entry.label }}: {{ formatCompactNumber(value) }}</title>
        </rect>
      </g>
    </svg>
  </div>
</template>
