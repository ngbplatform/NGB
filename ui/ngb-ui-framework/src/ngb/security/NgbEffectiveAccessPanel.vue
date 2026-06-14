<template>
  <section class="flex min-w-0 flex-col rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card" data-testid="effective-access-panel">
    <div class="flex items-center justify-between gap-3 border-b border-ngb-border px-4 py-3">
      <div class="min-w-0">
        <h2 class="truncate text-sm font-semibold text-ngb-text">Effective access</h2>
        <div class="mt-0.5 text-xs text-ngb-muted">Version {{ access?.accessVersion ?? '-' }}</div>
      </div>
      <NgbButton v-if="showRefresh" size="sm" variant="ghost" :disabled="loading" @click="$emit('refresh')">
        <NgbIcon name="refresh" :size="15" />
        Refresh
      </NgbButton>
    </div>

    <div v-if="loading" class="p-4 text-sm text-ngb-muted">Loading...</div>
    <div v-else-if="error" class="p-4 text-sm text-ngb-danger">{{ error }}</div>
    <div v-else-if="!access || access.groups.length === 0" class="p-4 text-sm text-ngb-muted">No effective permissions.</div>
    <div v-else class="min-h-0 flex-1 overflow-auto">
      <section v-for="group in access.groups" :key="group.group" class="border-b border-ngb-border last:border-b-0">
        <div class="bg-[var(--ngb-grid-header)] px-4 py-2 text-xs font-semibold uppercase tracking-wide text-ngb-muted">
          {{ group.group }}
        </div>
        <div class="divide-y divide-ngb-border">
          <div
            v-for="resource in group.resources"
            :key="`${resource.resourceKind}:${resource.resourceCode}`"
            class="grid grid-cols-[minmax(0,1fr),minmax(160px,auto)] gap-3 px-4 py-3"
          >
            <div class="min-w-0">
              <div class="truncate text-sm font-medium text-ngb-text">{{ resource.displayName }}</div>
              <div class="mt-0.5 truncate font-mono text-[11px] text-ngb-muted">{{ resource.resourceKind }}.{{ resource.resourceCode }}</div>
            </div>
            <div class="flex flex-wrap justify-end gap-1.5">
              <NgbBadge v-for="action in resource.actions" :key="action" tone="neutral">{{ action }}</NgbBadge>
            </div>
          </div>
        </div>
      </section>
    </div>
  </section>
</template>

<script setup lang="ts">
import NgbBadge from '../primitives/NgbBadge.vue'
import NgbButton from '../primitives/NgbButton.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import type { EffectiveAccessDto } from './types'

withDefaults(defineProps<{
  access: EffectiveAccessDto | null
  loading?: boolean
  error?: string | null
  showRefresh?: boolean
}>(), {
  showRefresh: true,
})

defineEmits<{
  (e: 'refresh'): void
}>()
</script>
