<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import NgbPickerPopover from './NgbPickerPopover.vue'
import NgbPickerNavButton from './NgbPickerNavButton.vue'
import { parseDateOnlyValue, toDateOnlyValue } from '../utils/dateValues'

const props = defineProps<{
  modelValue: string | null | undefined
  placeholder?: string
  disabled?: boolean
  readonly?: boolean
  grouped?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', v: string | null): void
}>()

function toDisplay(v: string | null | undefined): string {
  if (!v) return ''
  const dt = parseDateOnlyValue(v)
  if (!dt) return String(v)
  const mm = String(dt.getMonth() + 1).padStart(2, '0')
  const dd = String(dt.getDate()).padStart(2, '0')
  const yy = String(dt.getFullYear())
  return `${mm}/${dd}/${yy}`
}

const selected = computed(() => parseDateOnlyValue(props.modelValue ?? null))

const today = () => {
  const n = new Date()
  return new Date(n.getFullYear(), n.getMonth(), n.getDate())
}

const viewYear = ref<number>(selected.value?.getFullYear() ?? today().getFullYear())
const viewMonth = ref<number>(selected.value?.getMonth() ?? today().getMonth())

watch(
  () => props.modelValue,
  (v) => {
    const dt = parseDateOnlyValue(v ?? null)
    if (!dt) return
    viewYear.value = dt.getFullYear()
    viewMonth.value = dt.getMonth()
  }
)

const monthLabel = computed(() => {
  const dt = new Date(viewYear.value, viewMonth.value, 1)
  return dt.toLocaleString(undefined, { month: 'long', year: 'numeric' })
})

const grid = computed(() => {
  const first = new Date(viewYear.value, viewMonth.value, 1)
  const firstDow = first.getDay()
  const daysInMonth = new Date(viewYear.value, viewMonth.value + 1, 0).getDate()

  const cells: Array<{ dt: Date | null; label: string }> = []
  for (let i = 0; i < firstDow; i++) cells.push({ dt: null, label: '' })
  for (let d = 1; d <= daysInMonth; d++) cells.push({ dt: new Date(viewYear.value, viewMonth.value, d), label: String(d) })
  while (cells.length % 7 !== 0) cells.push({ dt: null, label: '' })
  return cells
})

const isSameDay = (a: Date, b: Date) =>
  a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()

function prevMonth() {
  const dt = new Date(viewYear.value, viewMonth.value - 1, 1)
  viewYear.value = dt.getFullYear()
  viewMonth.value = dt.getMonth()
}

function nextMonth() {
  const dt = new Date(viewYear.value, viewMonth.value + 1, 1)
  viewYear.value = dt.getFullYear()
  viewMonth.value = dt.getMonth()
}

function pick(dt: Date, close: () => void) {
  emit('update:modelValue', toDateOnlyValue(dt))
  close()
}

function clear(close: () => void) {
  emit('update:modelValue', null)
  close()
}

function setToday(close: () => void) {
  emit('update:modelValue', toDateOnlyValue(today()))
  close()
}

const displayValue = computed(() => toDisplay(props.modelValue ?? null))
</script>

<template>
  <NgbPickerPopover
    :display-value="displayValue"
    :placeholder="placeholder ?? 'mm/dd/yyyy'"
    :disabled="disabled"
    :readonly="readonly"
    :grouped="grouped"
  >
    <template #header>
      <NgbPickerNavButton direction="prev" @click="prevMonth" />
      <div class="text-sm font-semibold text-ngb-text">{{ monthLabel }}</div>
      <NgbPickerNavButton direction="next" @click="nextMonth" />
    </template>

    <template #default="{ close }">
      <div class="grid grid-cols-7 gap-1 text-center text-xs text-ngb-muted">
        <div>Su</div><div>Mo</div><div>Tu</div><div>We</div><div>Th</div><div>Fr</div><div>Sa</div>
      </div>

      <div class="mt-2 grid grid-cols-7 gap-1">
        <button
          v-for="(c, i) in grid"
          :key="i"
          type="button"
          class="h-9 rounded-md text-sm"
          :class="[
            !c.dt ? 'pointer-events-none opacity-0' : '',
            c.dt && selected && isSameDay(c.dt, selected) ? 'bg-ngb-primary/20 text-ngb-text' : 'text-ngb-text hover:bg-ngb-muted/10',
            c.dt && isSameDay(c.dt, today()) ? 'ring-1 ring-ngb-primary/40' : ''
          ]"
          @click="c.dt && pick(c.dt, close)"
        >
          {{ c.label }}
        </button>
      </div>
    </template>

    <template #footer="{ close }">
      <button type="button" class="text-ngb-primary hover:underline" @click="clear(close)">Clear</button>
      <button type="button" class="text-ngb-primary hover:underline" @click="setToday(close)">Today</button>
    </template>
  </NgbPickerPopover>
</template>
