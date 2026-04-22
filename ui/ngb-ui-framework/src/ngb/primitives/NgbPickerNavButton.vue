<template>
  <button
    type="button"
    class="inline-flex h-8 w-8 items-center justify-center rounded-md hover:bg-ngb-muted/10"
    @click="emit('click')"
  >
    <svg
      class="h-5 w-5 text-ngb-muted"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.7"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <path v-if="direction === 'prev'" d="M15 18l-6-6 6-6" />
      <path v-else d="M9 6l6 6-6 6" />
    </svg>
  </button>
</template>

<script setup lang="ts">
defineProps<{
  direction: 'prev' | 'next'
}>()

const emit = defineEmits<{
  (e: 'click'): void
}>()
</script>
