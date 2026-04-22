<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import NgbPickerPopover from './NgbPickerPopover.vue'
import NgbPickerNavButton from './NgbPickerNavButton.vue'
import {
  currentMonthValue,
  formatMonthValue,
  normalizeMonthValue,
  parseMonthValue,
  toMonthValue,
} from '../utils/dateValues'

const props = defineProps<{
  modelValue: string | null | undefined
  placeholder?: string
  disabled?: boolean
  readonly?: boolean
  grouped?: boolean
  displayFormat?: 'short' | 'long'
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', v: string | null): void
}>()

function toDisplay(v: string | null | undefined, format: 'short' | 'long'): string {
  if (!v) return ''
  return formatMonthValue(v, { month: format, year: 'numeric' }) ?? String(v)
}

const months = [
  { value: 1, label: 'Jan' },
  { value: 2, label: 'Feb' },
  { value: 3, label: 'Mar' },
  { value: 4, label: 'Apr' },
  { value: 5, label: 'May' },
  { value: 6, label: 'Jun' },
  { value: 7, label: 'Jul' },
  { value: 8, label: 'Aug' },
  { value: 9, label: 'Sep' },
  { value: 10, label: 'Oct' },
  { value: 11, label: 'Nov' },
  { value: 12, label: 'Dec' },
] as const

const selected = computed(() => parseMonthValue(props.modelValue ?? null))
const viewYear = ref<number>(selected.value?.year ?? parseMonthValue(currentMonthValue())?.year ?? new Date().getFullYear())

watch(
  () => props.modelValue,
  (v) => {
    const ym = parseMonthValue(normalizeMonthValue(v) ?? null)
    if (!ym) return
    viewYear.value = ym.year
  }
)

const displayValue = computed(() => toDisplay(props.modelValue ?? null, props.displayFormat ?? 'long'))

function prevYear() {
  viewYear.value -= 1
}

function nextYear() {
  viewYear.value += 1
}

function pick(month: number, close: () => void) {
  emit('update:modelValue', toMonthValue(viewYear.value, month))
  close()
}

function clear(close: () => void) {
  emit('update:modelValue', null)
  close()
}

function setCurrent(close: () => void) {
  emit('update:modelValue', currentMonthValue())
  close()
}

function isSelected(month: number): boolean {
  return selected.value?.year === viewYear.value && selected.value?.month === month
}

function isCurrent(month: number): boolean {
  const ym = parseMonthValue(currentMonthValue())
  return ym?.year === viewYear.value && ym.month === month
}
</script>

<template>
  <NgbPickerPopover
    :display-value="displayValue"
    :placeholder="placeholder ?? 'Select month'"
    :disabled="disabled"
    :readonly="readonly"
    :grouped="grouped"
  >
    <template #header>
      <NgbPickerNavButton direction="prev" @click="prevYear" />
      <div class="text-sm font-semibold text-ngb-text">{{ viewYear }}</div>
      <NgbPickerNavButton direction="next" @click="nextYear" />
    </template>

    <template #default="{ close }">
      <div class="grid grid-cols-4 gap-2">
        <button
          v-for="month in months"
          :key="month.value"
          type="button"
          class="h-10 rounded-md border text-sm transition-colors"
          :class="[
            isSelected(month.value)
              ? 'border-ngb-primary/50 bg-ngb-primary/20 text-ngb-text'
              : 'border-ngb-border bg-transparent text-ngb-text hover:bg-ngb-muted/10',
            isCurrent(month.value) ? 'ring-1 ring-ngb-primary/40' : ''
          ]"
          @click="pick(month.value, close)"
        >
          {{ month.label }}
        </button>
      </div>
    </template>

    <template #footer="{ close }">
      <button type="button" class="text-ngb-primary hover:underline" @click="clear(close)">Clear</button>
      <button type="button" class="text-ngb-primary hover:underline" @click="setCurrent(close)">This month</button>
    </template>
  </NgbPickerPopover>
</template>
