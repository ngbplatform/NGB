<template>
  <div class="grid grid-cols-1 items-start gap-3" :class="dense ? 'sm:grid-cols-[180px_minmax(0,1fr)]' : 'sm:grid-cols-[220px_minmax(0,1fr)]'">
    <div class="pt-0 sm:pt-2">
      <div class="text-sm font-medium text-ngb-text">{{ label }}</div>
      <div v-if="hint" class="text-xs text-ngb-muted mt-1">{{ hint }}</div>
    </div>
    <div class="min-w-0">
      <slot />
      <div v-if="error" class="text-xs text-ngb-danger mt-1">{{ error }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  label: string;
  hint?: string;
  error?: string;
  dense?: boolean;
}>();
</script>
