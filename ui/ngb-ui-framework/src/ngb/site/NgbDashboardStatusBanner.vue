<script setup lang="ts">
import { computed } from 'vue'

import NgbIcon from '../primitives/NgbIcon.vue'

const props = withDefaults(defineProps<{
  error?: string | null
  warnings?: readonly string[] | null | undefined
  errorTitle?: string
  warningTitle?: string
  warningLimit?: number
}>(), {
  error: null,
  warnings: () => [],
  errorTitle: 'Dashboard data failed to load',
  warningTitle: 'Some sections are partial',
  warningLimit: 3,
})

const normalizedError = computed(() => String(props.error ?? '').trim())
const normalizedWarningLimit = computed(() => Math.max(0, Math.trunc(props.warningLimit)))
const visibleWarnings = computed(() =>
  (props.warnings ?? [])
    .map((value) => String(value ?? '').trim())
    .filter((value, index, items) => value.length > 0 && items.indexOf(value) === index)
    .slice(0, normalizedWarningLimit.value),
)
const mode = computed<'error' | 'warn' | null>(() => {
  if (normalizedError.value) return 'error'
  if (visibleWarnings.value.length > 0) return 'warn'
  return null
})
</script>

<template>
  <div
    v-if="mode"
    class="ngb-dashboard-status-banner"
    :class="mode === 'error' ? 'ngb-dashboard-status-banner-danger' : 'ngb-dashboard-status-banner-warn'"
  >
    <div class="flex items-start gap-3">
      <div class="ngb-dashboard-status-banner-icon">
        <NgbIcon :name="mode === 'error' ? 'circle-x' : 'help-circle'" :size="18" />
      </div>

      <div>
        <div class="text-sm font-semibold text-ngb-text">
          {{ mode === 'error' ? errorTitle : warningTitle }}
        </div>

        <div v-if="mode === 'error'" class="mt-1 text-sm text-ngb-muted">
          {{ normalizedError }}
        </div>

        <div v-else class="mt-1 space-y-1 text-sm text-ngb-muted">
          <div v-for="warning in visibleWarnings" :key="warning">{{ warning }}</div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ngb-dashboard-status-banner {
  --ngb-dashboard-status-banner-color: var(--ngb-border);
  border: 1px solid color-mix(in srgb, var(--ngb-dashboard-status-banner-color) 24%, var(--ngb-border));
  border-radius: var(--ngb-radius);
  background: color-mix(in srgb, var(--ngb-dashboard-status-banner-color) 10%, var(--ngb-card));
  padding: 1rem 1.1rem;
}

.ngb-dashboard-status-banner-warn {
  --ngb-dashboard-status-banner-color: var(--ngb-warn);
}

.ngb-dashboard-status-banner-danger {
  --ngb-dashboard-status-banner-color: var(--ngb-danger);
}

.ngb-dashboard-status-banner-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  height: 2rem;
  width: 2rem;
  border-radius: 999px;
  border: 1px solid color-mix(in srgb, var(--ngb-dashboard-status-banner-color) 26%, var(--ngb-border));
  background: color-mix(in srgb, var(--ngb-dashboard-status-banner-color) 10%, var(--ngb-card));
  color: var(--ngb-text);
}
</style>
