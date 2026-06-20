<template>
  <div class="flex h-full min-h-[320px] items-center justify-center p-6">
    <div class="w-full max-w-[520px] rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-6 text-center shadow-card">
      <div class="mx-auto flex h-11 w-11 items-center justify-center rounded-full border border-ngb-border bg-ngb-bg text-ngb-muted">
        <NgbIcon name="shield" :size="22" />
      </div>
      <h1 class="mt-4 text-lg font-semibold text-ngb-text">{{ title }}</h1>
      <p class="mt-2 text-sm leading-6 text-ngb-muted">{{ message }}</p>
      <div v-if="$slots.actions" class="mt-5 flex justify-center gap-2">
        <slot name="actions" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import NgbIcon from '../primitives/NgbIcon.vue'

withDefaults(defineProps<{
  title?: string
  message?: string
}>(), {
  title: 'Access denied',
  message: 'Your current access profile does not allow this operation.',
})
</script>

