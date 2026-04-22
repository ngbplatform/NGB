<script setup lang="ts">
import { computed } from 'vue'
import { Popover, PopoverButton, PopoverPanel } from '@headlessui/vue'

const props = withDefaults(defineProps<{
  displayValue: string
  placeholder: string
  disabled?: boolean
  readonly?: boolean
  grouped?: boolean
  panelClass?: string
}>(), {
  panelClass: 'w-[320px]',
})

const triggerClass = computed(() => {
  const base = 'relative flex w-full items-center justify-start text-left ngb-focus'
  const disabled = props.disabled || props.readonly ? ' opacity-60 cursor-not-allowed' : ''
  if (props.grouped) {
    return `${base} h-full min-h-full bg-transparent px-3 py-0 pr-8 text-xs text-ngb-text placeholder:text-ngb-muted/70${disabled}`
  }
  return `${base} h-9 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 py-0 pr-10 text-sm text-ngb-text placeholder:text-ngb-muted/70${disabled}`
})

const popoverClass = computed(() => (props.grouped ? 'relative block h-full w-full' : 'relative block w-full'))
const panelClasses = computed(() => [
  'absolute right-0 z-50 mt-2 max-w-[calc(100vw-2rem)] overflow-hidden rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-lg',
  props.panelClass,
])
</script>

<template>
  <Popover v-slot="{ close }" :class="popoverClass">
    <PopoverButton
      as="button"
      type="button"
      :class="triggerClass"
      :disabled="disabled || readonly"
    >
      <span
        v-if="displayValue"
        class="inline-flex h-full min-w-0 flex-1 items-center truncate leading-none"
      >
        {{ displayValue }}
      </span>
      <span
        v-else
        class="inline-flex h-full min-w-0 flex-1 items-center truncate leading-none text-ngb-muted/70"
      >
        {{ placeholder }}
      </span>

      <span class="pointer-events-none absolute right-2 top-1/2 inline-flex -translate-y-1/2 items-center">
        <svg class="h-4 w-4 text-ngb-muted" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
          <path d="M8 2v4" />
          <path d="M16 2v4" />
          <path d="M3 9h18" />
          <path d="M5 6h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z" />
        </svg>
      </span>
    </PopoverButton>

    <PopoverPanel :class="panelClasses">
      <div class="p-3">
        <div v-if="$slots.header" class="flex items-center justify-between gap-2">
          <slot name="header" :close="close" />
        </div>

        <div :class="$slots.header ? 'mt-3' : ''">
          <slot :close="close" />
        </div>

        <div v-if="$slots.footer" class="mt-3 flex items-center justify-between text-sm">
          <slot name="footer" :close="close" />
        </div>
      </div>
    </PopoverPanel>
  </Popover>
</template>
