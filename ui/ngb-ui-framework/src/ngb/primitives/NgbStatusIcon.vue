<template>
  <span
    class="inline-flex items-center justify-center leading-none"
    :title="title ?? defaultTitle"
    :class="colorClass"
    aria-hidden="true"
  >
    <svg
      viewBox="0 0 24 24"
      class="w-6 h-6 block"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      <!-- Consistent, compact ERP-style status glyphs. -->
      <circle cx="12" cy="12" r="7" />

      <!-- Posted: check in the ring -->
      <template v-if="status === 'posted'">
        <path d="M8.7 12.1l2.0 2.1 4.6-4.8" />
      </template>

      <!-- Marked: X in the ring -->
      <template v-else-if="status === 'marked'">
        <path d="M9 9l6 6" />
        <path d="M15 9l-6 6" />
      </template>

      <!-- Active/Saved: ring only -->
    </svg>
  </span>
</template>

<script setup lang="ts">
import { computed } from 'vue';

const props = defineProps<{
  status: 'active' | 'saved' | 'posted' | 'marked';
  title?: string;
}>();

const defaultTitle = computed(() => {
  switch (props.status) {
    case 'active':
      return 'Active';
    case 'saved':
      return 'Saved';
    case 'posted':
      return 'Posted';
    case 'marked':
      return 'Marked for deletion';
  }
});

const colorClass = computed(() => {
  if (props.status === 'posted') return 'text-ngb-success';
  if (props.status === 'marked') return 'text-ngb-danger';
  // Keep the same subdued “active” color as catalogs.
  return 'text-ngb-muted opacity-30';
});
</script>
