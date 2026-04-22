<template>
  <div class="w-full">
    <label v-if="label" class="block text-xs font-semibold text-ngb-muted mb-1">
      {{ label }}
    </label>
    <input
      :type="type"
      :value="modelValue"
      :placeholder="placeholder"
      :disabled="disabled"
      :readonly="readonly"
      :title="title || undefined"
      @input="$emit('update:modelValue', $event.target.value)"
      :class="[inputClass, disabled ? 'opacity-60 cursor-not-allowed' : '']"
    />
    <div v-if="hint" class="mt-1 text-xs text-ngb-muted">{{ hint }}</div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

type InputVariant = 'default' | 'grid'

const props = withDefaults(defineProps<{
  modelValue?: string | number
  label?: string
  hint?: string
  placeholder?: string
  type?: string
  disabled?: boolean
  readonly?: boolean
  title?: string
  variant?: InputVariant
}>(), {
  modelValue: '',
  label: '',
  hint: '',
  placeholder: '',
  type: 'text',
  disabled: false,
  readonly: false,
  title: '',
  variant: 'default',
})

defineEmits<{
  (e: 'update:modelValue', value: string): void
}>()

const inputClass = computed(() => {
  if (props.variant === 'grid') {
    return 'h-8 w-full rounded-none border border-transparent bg-transparent px-2 text-sm text-ngb-text placeholder:text-ngb-muted/70 ngb-focus'
  }
  return 'h-9 w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 text-sm text-ngb-text placeholder:text-ngb-muted/70 ngb-focus'
})
</script>
