<template>
  <button :type="type" :disabled="disabled || loading" :class="classes">
    <span v-if="loading" class="ngb-spinner" aria-hidden="true"></span>
    <slot />
  </button>
</template>

<script setup lang="ts">
import { computed } from 'vue'

type ButtonVariant = 'secondary' | 'primary' | 'ghost' | 'danger'
type ButtonSize = 'sm' | 'md' | 'lg'
type NativeButtonType = 'button' | 'submit' | 'reset'

const props = withDefaults(defineProps<{
  variant?: ButtonVariant
  size?: ButtonSize
  disabled?: boolean
  loading?: boolean
  type?: NativeButtonType
}>(), {
  variant: 'secondary',
  size: 'md',
  disabled: false,
  loading: false,
  type: 'button',
})

const classes = computed(() => {
  const base =
    'inline-flex items-center justify-center gap-2 rounded-[var(--ngb-radius)] border text-sm font-medium select-none ' +
    'transition-shadow transition-colors shadow-none ngb-focus';

  const sizes = {
    sm: 'h-8 px-3',
    md: 'h-9 px-3.5',
    lg: 'h-10 px-4',
  }[props.size] ?? 'h-9 px-3.5'

  const common = (props.disabled || props.loading)
    ? 'opacity-60 cursor-not-allowed'
    : 'cursor-pointer';

  const variants = {
    secondary: 'bg-ngb-card border-ngb-border text-ngb-text hover:bg-ngb-bg hover:border-[rgba(11,60,93,.35)] hover:shadow-[0_2px_10px_rgba(0,0,0,.06)]',
    primary: 'bg-ngb-primary border-ngb-primary text-white hover:bg-ngb-primary-hover hover:border-ngb-primary-hover hover:shadow-[0_8px_22px_rgba(0,0,0,.10)]',
    ghost: 'bg-transparent border-transparent text-ngb-text hover:bg-ngb-bg hover:border-ngb-border',
    danger: 'bg-ngb-card border-ngb-border text-ngb-danger hover:bg-ngb-bg hover:border-[rgba(155,28,28,.35)] hover:shadow-[0_2px_10px_rgba(0,0,0,.06)]',
  }[props.variant] ?? 'bg-ngb-card border-ngb-border text-ngb-text'

  return [base, sizes, common, variants].join(' ')
})
</script>
