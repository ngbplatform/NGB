<template>
  <div class="w-full">
    <div v-if="label" class="block text-xs font-semibold text-ngb-muted mb-1">{{ label }}</div>

    <div class="flex flex-col gap-2">
      <label
        v-for="o in options"
        :key="o.value"
        class="inline-flex items-center gap-2 select-none"
        :class="disabled ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer'"
      >
        <input
          class="sr-only"
          type="radio"
          :name="name"
          :value="o.value"
          :checked="modelValue === o.value"
          :disabled="disabled"
          @change="$emit('update:modelValue', o.value)"
        />

        <span
          class="h-4 w-4 rounded-full border border-ngb-border bg-white flex items-center justify-center"
          :class="modelValue === o.value ? 'border-[rgba(11,60,93,.55)] bg-[rgba(11,60,93,.06)]' : ''"
          aria-hidden="true"
        >
          <span v-if="modelValue === o.value" class="h-2 w-2 rounded-full bg-ngb-blue" />
        </span>

        <span class="text-sm text-ngb-text">{{ o.label }}</span>
      </label>
    </div>

    <div v-if="hint" class="mt-1 text-xs text-ngb-muted">{{ hint }}</div>
  </div>
</template>

<script setup lang="ts">
export type RadioOption = { value: string; label: string };

defineProps<{
  modelValue: string;
  options: RadioOption[];
  name?: string;
  label?: string;
  hint?: string;
  disabled?: boolean;
}>();

defineEmits<{
  (e: 'update:modelValue', v: string): void;
}>();
</script>
