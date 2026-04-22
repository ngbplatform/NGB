<template>
  <button
    type="button"
    role="switch"
    :aria-checked="modelValue ? 'true' : 'false'"
    :disabled="disabled"
    class="inline-flex items-center gap-2 select-none"
    :class="disabled ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer'"
    @click="toggle"
  >
    <span
      class="relative inline-flex h-[1.125rem] w-8 rounded-full border bg-white transition-colors duration-150 dark:bg-ngb-card"
      :class="modelValue
        ? 'border-[rgba(11,60,93,.40)] dark:border-[rgba(147,197,253,.40)]'
        : 'border-ngb-border dark:border-ngb-border'"
      aria-hidden="true"
    >
      <span
        class="absolute left-0.5 top-1/2 h-[0.875rem] w-[0.875rem] -translate-y-1/2 rounded-full bg-ngb-blue shadow-[0_0_0_1px_rgba(11,60,93,.04)] transition-transform duration-150 dark:bg-white dark:shadow-[0_0_0_1px_rgba(255,255,255,.10)]"
        :class="modelValue ? 'translate-x-[0.75rem]' : 'translate-x-0'"
      />
    </span>
    <span v-if="label" class="text-sm text-ngb-text">{{ label }}</span>
  </button>
</template>

<script setup lang="ts">
const props = defineProps<{
  modelValue: boolean;
  label?: string;
  disabled?: boolean;
}>();

const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void;
}>();

function toggle() {
  if (props.disabled) return;
  emit('update:modelValue', !props.modelValue);
}
</script>
