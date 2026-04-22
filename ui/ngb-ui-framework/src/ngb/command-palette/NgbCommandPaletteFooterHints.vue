<template>
  <div class="flex flex-wrap items-center justify-between gap-3 border-t border-ngb-border px-4 py-3 text-xs text-ngb-muted">
    <div class="flex flex-wrap items-center gap-2">
      <span class="ngb-kbd">↑</span>
      <span class="ngb-kbd">↓</span>
      <span>Navigate</span>
      <span class="ngb-kbd">Enter</span>
      <span>Open</span>
      <span class="ngb-kbd">{{ primaryModifier }}</span>
      <span class="ngb-kbd">Enter</span>
      <span>New tab</span>
    </div>

    <div class="flex flex-wrap items-center gap-2">
      <span class="ngb-kbd">Esc</span>
      <span>Close</span>
      <span class="ngb-kbd">{{ primaryModifier }}</span>
      <span class="ngb-kbd">K</span>
      <span>Focus</span>
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  primaryModifier: string
}>()
</script>

