<template>
  <label class="inline-flex items-center gap-2 select-none" :class="disabled ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer'">
    <input
      class="sr-only"
      type="checkbox"
      :checked="modelValue"
      :disabled="disabled"
      @change="$emit('update:modelValue', ($event.target as HTMLInputElement).checked)"
    />

    <span
      class="h-4 w-4 border border-ngb-border bg-ngb-card flex items-center justify-center"
      :class="modelValue ? 'border-[rgba(11,60,93,.55)] bg-[rgba(11,60,93,.06)] dark:border-[rgba(147,197,253,.55)] dark:bg-[rgba(147,197,253,.10)]' : ''"
      aria-hidden="true"
    >
      <span v-if="modelValue" class="text-[color:var(--ngb-logo)] text-[12px] leading-none">✓</span>
    </span>

    <span v-if="label" class="text-sm text-ngb-text">{{ label }}</span>
  </label>
</template>

<script setup lang="ts">
defineProps<{
  modelValue: boolean;
  label?: string;
  disabled?: boolean;
}>();

defineEmits<{
  (e: 'update:modelValue', v: boolean): void;
}>();
</script>
