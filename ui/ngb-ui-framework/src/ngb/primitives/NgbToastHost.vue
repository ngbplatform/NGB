<template>
  <div
    role="region"
    aria-label="Notifications"
    aria-live="polite"
    aria-atomic="false"
    aria-relevant="additions text"
    class="fixed z-[60] top-4 right-4 w-[360px] max-w-[calc(100vw-32px)] flex flex-col gap-2"
  >
    <div
      v-for="t in api.toasts"
      :key="t.id"
      class="ngb-card p-3"
    >
      <div class="flex items-start justify-between gap-3">
        <div class="min-w-0">
          <div class="text-sm font-semibold" :class="toneClass(t.tone)">
            {{ t.title }}
          </div>
          <div v-if="t.message" class="text-sm text-ngb-muted mt-1">{{ t.message }}</div>
        </div>

        <button
          type="button"
          class="h-7 w-7 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card text-ngb-muted hover:text-ngb-text hover:bg-ngb-bg transition-colors ngb-focus"
          @click="api.remove(t.id)"
          aria-label="Close"
        >
          ×
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useToasts, type ToastTone } from './toast';

const api = useToasts();

function toneClass(tone: ToastTone | undefined) {
  return {
    neutral: 'text-ngb-text',
    success: 'text-ngb-success',
    warn: 'text-ngb-warn',
    danger: 'text-ngb-danger',
  }[tone ?? 'neutral'];
}
</script>
