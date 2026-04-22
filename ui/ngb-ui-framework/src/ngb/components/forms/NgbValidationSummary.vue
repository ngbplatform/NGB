<template>
  <div
    v-if="messages?.length"
    role="alert"
    aria-live="assertive"
    aria-atomic="true"
    class="rounded-[var(--ngb-radius)] border border-ngb-border bg-[#FFFBEB] dark:bg-[rgba(251,191,36,.10)] dark:border-[rgba(251,191,36,.25)] px-4 py-3"
  >
    <div class="text-sm font-semibold text-ngb-text">Please fix the following</div>
    <ul class="mt-2 space-y-1 text-sm text-ngb-muted list-disc pl-5">
      <li v-for="(m, idx) in messages" :key="idx">{{ m }}</li>
    </ul>
  </div>
</template>

<script setup lang="ts">
defineProps<{ messages: string[] }>();
</script>
