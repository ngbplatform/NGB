<script setup lang="ts">
import { computed } from 'vue'

import NgbDatePicker from '../primitives/NgbDatePicker.vue'
import NgbIcon from '../primitives/NgbIcon.vue'

const props = withDefaults(defineProps<{
  modelValue: string | null | undefined
  loading?: boolean
  disabled?: boolean
}>(), {
  loading: false,
  disabled: false,
})

const emit = defineEmits<{
  (e: 'refresh'): void
  (e: 'update:modelValue', value: string | null): void
}>()

const isDisabled = computed(() => props.loading || props.disabled)

function onRefresh(): void {
  if (isDisabled.value) return
  emit('refresh')
}
</script>

<template>
  <div class="ngb-dashboard-toolbar-control">
    <div class="ngb-dashboard-toolbar-picker">
      <NgbDatePicker
        :model-value="modelValue"
        grouped
        :disabled="isDisabled"
        @update:model-value="emit('update:modelValue', $event)"
      />
    </div>

    <div class="ngb-dashboard-toolbar-divider" aria-hidden="true" />

    <button
      type="button"
      class="ngb-dashboard-toolbar-button ngb-focus"
      :disabled="isDisabled"
      title="Refresh"
      aria-label="Refresh"
      @click="onRefresh"
    >
      <NgbIcon name="refresh" :size="15" />
    </button>
  </div>
</template>

<style scoped>
.ngb-dashboard-toolbar-control {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  min-height: 28px;
  border: 1px solid var(--ngb-border);
  border-radius: var(--ngb-radius);
  background: var(--ngb-card);
  padding: 0 0.25rem;
  box-shadow: var(--ngb-shadow-1);
}

.ngb-dashboard-toolbar-picker {
  width: 10rem;
}

.ngb-dashboard-toolbar-divider {
  height: 1rem;
  width: 1px;
  background: var(--ngb-border);
}

.ngb-dashboard-toolbar-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  height: 26px;
  width: 26px;
  border: none;
  border-radius: var(--ngb-radius);
  background: transparent;
  color: var(--ngb-muted);
  transition: background-color 140ms ease, color 140ms ease;
}

.ngb-dashboard-toolbar-button:hover:not(:disabled) {
  background: var(--ngb-bg);
  color: var(--ngb-text);
}

.ngb-dashboard-toolbar-button:disabled {
  cursor: not-allowed;
  opacity: 0.45;
}
</style>
