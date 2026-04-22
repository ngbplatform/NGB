<template>
  <span
    class="relative inline-flex"
    @mouseenter="open = true"
    @mouseleave="open = false"
    @focusin="open = true"
    @focusout="open = false"
  >
    <slot />

    <span
      v-if="open"
      class="absolute z-50 -top-2 left-1/2 -translate-x-1/2 -translate-y-full px-2 py-1 rounded-[var(--ngb-radius)] border border-ngb-border bg-white shadow-card text-xs text-ngb-text w-max max-w-[240px]"
      role="tooltip"
    >
      {{ text }}
    </span>
  </span>
</template>

<script setup lang="ts">
import { ref } from 'vue';

defineProps<{ text: string }>();

const open = ref(false);
</script>