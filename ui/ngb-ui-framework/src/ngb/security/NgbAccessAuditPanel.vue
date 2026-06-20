<template>
  <section class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
    <h2 class="text-sm font-semibold text-ngb-text">Security audit</h2>
    <div class="mt-3 space-y-2">
      <div
        v-for="event in events"
        :key="event.id"
        class="grid grid-cols-[minmax(0,1fr),auto] gap-3 border-t border-ngb-border pt-2 first:border-t-0 first:pt-0"
      >
        <div class="min-w-0">
          <div class="truncate text-sm text-ngb-text">{{ event.title }}</div>
          <div v-if="event.actor" class="mt-0.5 truncate text-xs text-ngb-muted">{{ event.actor }}</div>
        </div>
        <time class="text-xs text-ngb-muted">{{ event.occurredAt }}</time>
      </div>
      <div v-if="events.length === 0" class="text-sm text-ngb-muted">No security audit events in this view.</div>
    </div>
  </section>
</template>

<script setup lang="ts">
withDefaults(defineProps<{
  events?: Array<{
    id: string
    title: string
    actor?: string | null
    occurredAt: string
  }>
}>(), {
  events: () => [],
})
</script>
