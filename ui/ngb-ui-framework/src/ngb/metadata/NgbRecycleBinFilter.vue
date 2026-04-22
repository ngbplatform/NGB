<script setup lang="ts">
import type { QueryTrashMode } from '../router/queryParams'

type RecycleBinMode = QueryTrashMode

const props = defineProps<{
  modelValue: RecycleBinMode
  disabled?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', v: RecycleBinMode): void
}>()

type Option = { key: RecycleBinMode; label: string }
const options: Option[] = [
  { key: 'active', label: 'Active' },
  { key: 'deleted', label: 'Deleted' },
  { key: 'all', label: 'All' },
]

function pick(v: RecycleBinMode) {
  if (props.disabled) return
  if (v === props.modelValue) return
  emit('update:modelValue', v)
}

function btnClass(v: RecycleBinMode) {
  const isActive = v === props.modelValue
  return [
    'px-2.5 py-1 text-xs font-medium ngb-focus',
    isActive
      ? 'bg-[var(--ngb-row-selected)] text-ngb-text'
      : 'text-ngb-muted hover:bg-[var(--ngb-row-hover)]',
    props.disabled ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer',
  ].join(' ')
}
</script>

<template>
  <div class="inline-flex items-center overflow-hidden rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card">
    <button
      v-for="opt in options"
      :key="opt.key"
      type="button"
      :class="btnClass(opt.key)"
      :disabled="!!disabled"
      @click="pick(opt.key)"
    >
      {{ opt.label }}
    </button>
  </div>
</template>
