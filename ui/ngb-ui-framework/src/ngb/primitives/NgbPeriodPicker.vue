<template>
  <div class="w-full">
    <label v-if="label" class="block text-xs font-semibold text-ngb-muted mb-1">{{ label }}</label>

    <div class="flex items-center gap-3 flex-wrap">
      <NgbTabs v-model="kind" :tabs="kindTabs" />

      <div class="flex items-center gap-2">
        <span class="text-xs font-semibold text-ngb-muted">Year</span>
        <div class="w-[112px]">
          <NgbSelect
            :model-value="year"
            :options="yearOptions"
            @update:model-value="onYear(String($event ?? ''))"
          />
        </div>
      </div>

      <div class="flex items-center gap-2">
        <span class="text-xs font-semibold text-ngb-muted">{{ periodLabel }}</span>
        <div class="w-[112px]">
          <NgbSelect
            :model-value="period"
            :options="periodOptions"
            @update:model-value="onPeriod(String($event ?? ''))"
          />
        </div>
      </div>
    </div>

    <div v-if="hint" class="mt-1 text-xs text-ngb-muted">{{ hint }}</div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

import NgbSelect from './NgbSelect.vue'
import NgbTabs from './NgbTabs.vue'

export type PeriodKind = 'month' | 'quarter' | 'year'
export type PeriodValue = {
  kind: PeriodKind
  year: number
  period: number
}

const props = defineProps<{
  modelValue: PeriodValue
  label?: string
  hint?: string
  minYear?: number
  maxYear?: number
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: PeriodValue): void
}>()

const kindTabs = [
  { key: 'month', label: 'Month' },
  { key: 'quarter', label: 'Quarter' },
  { key: 'year', label: 'Year' },
]

const years = computed(() => {
  const now = new Date()
  const min = props.minYear ?? (now.getUTCFullYear() - 5)
  const max = props.maxYear ?? (now.getUTCFullYear() + 1)
  const values: number[] = []

  for (let year = max; year >= min; year -= 1) values.push(year)
  return values
})

const yearOptions = computed(() => years.value.map((year) => ({ value: year, label: String(year) })))

const kind = computed({
  get: () => props.modelValue.kind,
  set: (value: string) => {
    const nextKind = value as PeriodKind
    emit('update:modelValue', {
      ...props.modelValue,
      kind: nextKind,
      period: 1,
    })
  },
})

const year = computed(() => props.modelValue.year)
const period = computed(() => props.modelValue.period)

const periodLabel = computed(() => {
  if (props.modelValue.kind === 'month') return 'Month'
  if (props.modelValue.kind === 'quarter') return 'Quarter'
  return 'Period'
})

const periodOptions = computed(() => {
  if (props.modelValue.kind === 'month') {
    return Array.from({ length: 12 }, (_, index) => ({
      value: index + 1,
      label: String(index + 1).padStart(2, '0'),
    }))
  }

  if (props.modelValue.kind === 'quarter') {
    return [1, 2, 3, 4].map((quarter) => ({ value: quarter, label: `Q${quarter}` }))
  }

  return [{ value: 1, label: 'FY' }]
})

function onYear(value: string) {
  const nextYear = Number(value)
  if (!Number.isFinite(nextYear)) return
  emit('update:modelValue', { ...props.modelValue, year: nextYear })
}

function onPeriod(value: string) {
  const nextPeriod = Number(value)
  if (!Number.isFinite(nextPeriod)) return
  emit('update:modelValue', { ...props.modelValue, period: nextPeriod })
}
</script>
