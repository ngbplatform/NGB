<script setup lang="ts">
import NgbMonthPicker from '../primitives/NgbMonthPicker.vue'
import { normalizeMonthValue } from '../utils/dateValues'

const props = defineProps<{
  fromMonth: string
  toMonth: string
  disabled?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:fromMonth', value: string): void
  (e: 'update:toMonth', value: string): void
}>()

function updateFrom(value: string | null) {
  emit('update:fromMonth', value ?? '')
}

function updateTo(value: string | null) {
  emit('update:toMonth', value ?? '')
}
</script>

<template>
  <div class="relative inline-flex h-[26px] items-stretch rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card text-xs shadow-card">
    <div class="w-[9rem]">
      <NgbMonthPicker
        :model-value="normalizeMonthValue(props.fromMonth)"
        placeholder="Start month"
        grouped
        display-format="short"
        :disabled="!!props.disabled"
        @update:model-value="updateFrom"
      />
    </div>

    <div class="w-px self-stretch bg-ngb-border" />
    <div class="flex items-center px-2 text-[11px] text-ngb-muted select-none">-</div>
    <div class="w-px self-stretch bg-ngb-border" />

    <div class="w-[9rem]">
      <NgbMonthPicker
        :model-value="normalizeMonthValue(props.toMonth)"
        placeholder="End month"
        grouped
        display-format="short"
        :disabled="!!props.disabled"
        @update:model-value="updateTo"
      />
    </div>
  </div>
</template>
