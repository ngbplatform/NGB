<script setup lang="ts">
import NgbDrawer from '../components/NgbDrawer.vue'
import NgbFormLayout from '../components/forms/NgbFormLayout.vue'
import NgbFormRow from '../components/forms/NgbFormRow.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import type { FilterFieldState, FilterLookupItem, ListFilterField } from './types'
import NgbFilterFieldControl from './NgbFilterFieldControl.vue'

const props = defineProps<{
  open: boolean
  filters: ListFilterField[]
  values: Record<string, FilterFieldState>
  lookupItemsByKey: Record<string, FilterLookupItem[]>
  canUndo: boolean
  disabled?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void
  (e: 'lookup-query', payload: { key: string; query: string }): void
  (e: 'update:items', payload: { key: string; items: FilterLookupItem[] }): void
  (e: 'update:value', payload: { key: string; value: string }): void
  (e: 'undo'): void
}>()

function stateFor(key: string): FilterFieldState {
  return props.values[key] ?? { raw: '', items: [] }
}
</script>

<template>
  <NgbDrawer
    :open="open"
    title="Filter"
    subtitle="Define criteria to refine results"
    @update:open="emit('update:open', $event)"
  >
    <template #actions>
      <button
        type="button"
        class="ngb-iconbtn"
        title="Undo"
        :disabled="disabled || !canUndo"
        @click="emit('undo')"
      >
        <NgbIcon name="undo" />
      </button>
    </template>

    <NgbFormLayout v-if="filters.length > 0">
      <NgbFormRow
        v-for="field in filters"
        :key="field.key"
        :label="field.label"
        :hint="field.description ?? undefined"
        dense
      >
        <NgbFilterFieldControl
          :field="field"
          :state="stateFor(field.key)"
          :lookup-items="lookupItemsByKey[field.key] ?? []"
          :disabled="disabled"
          select-empty-label="Any"
          @lookup-query="emit('lookup-query', { key: field.key, query: $event })"
          @update:items="emit('update:items', { key: field.key, items: $event as FilterLookupItem[] })"
          @update:raw="emit('update:value', { key: field.key, value: $event })"
        />
      </NgbFormRow>
    </NgbFormLayout>

    <div
      v-else
      class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-3 py-2 text-sm text-ngb-muted"
    >
      No filters available.
    </div>
  </NgbDrawer>
</template>
