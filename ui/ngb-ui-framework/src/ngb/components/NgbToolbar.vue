<template>
  <div class="ngb-card flex flex-col gap-3 rounded-[var(--ngb-radius)] px-4 py-3 shadow-none sm:flex-row sm:items-center">
    <div class="flex w-full min-w-0 flex-wrap items-center gap-2 sm:flex-1">
      <slot name="left" />
    </div>
    <div class="flex w-full flex-wrap items-center gap-2 sm:w-auto sm:justify-end">
      <slot />
    </div>
  </div>
</template>

<script setup lang="ts">
// Actions row for docs/registers/reports.
</script>
