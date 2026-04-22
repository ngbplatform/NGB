<script setup lang="ts">
import NgbDatePicker from '../primitives/NgbDatePicker.vue'
import { normalizeDateOnlyValue } from '../utils/dateValues'

const props = defineProps<{
  fromDate: string
  toDate: string
  disabled?: boolean
  fromPlaceholder?: string
  toPlaceholder?: string
  title?: string
}>()

const emit = defineEmits<{
  (e: 'update:fromDate', value: string): void
  (e: 'update:toDate', value: string): void
}>()

function updateFrom(value: string | null) {
  emit('update:fromDate', value ?? '')
}

function updateTo(value: string | null) {
  emit('update:toDate', value ?? '')
}
</script>

<template>
  <div class="relative inline-flex h-[26px] items-stretch rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card text-xs shadow-card" :title="props.title ?? undefined">
    <div class="h-full w-[10rem]">
      <NgbDatePicker
        :model-value="normalizeDateOnlyValue(props.fromDate)"
        :placeholder="props.fromPlaceholder ?? 'Start date'"
        grouped
        :disabled="!!props.disabled"
        @update:model-value="updateFrom"
      />
    </div>

    <div class="w-px self-stretch bg-ngb-border" />
    <div class="flex items-center px-2 text-[11px] text-ngb-muted select-none">-</div>
    <div class="w-px self-stretch bg-ngb-border" />

    <div class="h-full w-[10rem]">
      <NgbDatePicker
        :model-value="normalizeDateOnlyValue(props.toDate)"
        :placeholder="props.toPlaceholder ?? 'End date'"
        grouped
        :disabled="!!props.disabled"
        @update:model-value="updateTo"
      />
    </div>
  </div>
</template>
