<template>
  <section class="ngb-card p-4">
    <div v-if="title" class="text-sm font-semibold text-ngb-text">{{ title }}</div>
    <div v-if="description" class="text-xs text-ngb-muted mt-1">{{ description }}</div>
    <div class="mt-4 space-y-3">
      <slot />
    </div>
  </section>
</template>

<script setup lang="ts">
defineProps<{
  title?: string;
  description?: string;
}>();
</script>
