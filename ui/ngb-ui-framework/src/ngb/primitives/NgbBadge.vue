<template>
  <span :class="classes">
    <slot />
  </span>
</template>

<script setup lang="ts">
import { computed } from 'vue'

type BadgeTone = 'neutral' | 'dev' | 'success' | 'warn' | 'danger'

const props = withDefaults(defineProps<{
  tone?: BadgeTone
}>(), {
  tone: 'neutral',
})

const classes = computed(() => {
  const base = 'inline-flex shrink-0 whitespace-nowrap items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold tracking-[.02em]'

  const tone = {
    neutral: 'border-ngb-border bg-ngb-neutral-subtle text-ngb-text',
    dev: 'border-ngb-border bg-ngb-card text-ngb-text',
    success: 'border-ngb-success-border bg-ngb-success-subtle text-ngb-success',
    warn: 'border-ngb-warn-border bg-ngb-warn-subtle text-ngb-warn',
    danger: 'border-ngb-danger-border bg-ngb-danger-subtle text-ngb-danger',
  }[props.tone] ?? 'border-ngb-border bg-ngb-neutral-subtle text-ngb-text'

  return [base, tone].join(' ')
})
</script>
