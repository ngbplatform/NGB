<script setup lang="ts">
import { computed } from 'vue'
import NgbInput from '../primitives/NgbInput.vue'
import NgbLookup from '../primitives/NgbLookup.vue'
import NgbMultiSelect from '../primitives/NgbMultiSelect.vue'
import NgbSelect from '../primitives/NgbSelect.vue'
import NgbSwitch from '../primitives/NgbSwitch.vue'
import { filterInputType, filterPlaceholder, filterSelectOptions } from './filtering'
import type { FilterFieldLike, FilterFieldState, FilterLookupItem } from './types'

const props = withDefaults(defineProps<{
  field: FilterFieldLike
  state: FilterFieldState
  lookupItems: FilterLookupItem[]
  disabled?: boolean
  selectEmptyLabel?: string
  showOpen?: boolean
  showClear?: boolean
  allowIncludeDescendants?: boolean
  includeDescendantsLabel?: string
}>(), {
  disabled: false,
  selectEmptyLabel: 'Any',
  showOpen: false,
  showClear: false,
  allowIncludeDescendants: false,
  includeDescendantsLabel: 'Include descendants',
})

const emit = defineEmits<{
  (e: 'lookup-query', query: string): void
  (e: 'update:items', items: FilterLookupItem[]): void
  (e: 'update:raw', value: string): void
  (e: 'update:includeDescendants', value: boolean): void
  (e: 'open'): void
}>()

const selectedItem = computed(() => props.state.items[0] ?? null)
const isLookup = computed(() => !!props.field.lookup)
const hasOptions = computed(() => (props.field.options?.length ?? 0) > 0)
const shouldShowIncludeDescendants = computed(() =>
  props.allowIncludeDescendants
  && !!props.field.supportsIncludeDescendants
  && props.state.items.length > 0)
const selectOptions = computed(() => filterSelectOptions(props.field, props.selectEmptyLabel))
const placeholder = computed(() => filterPlaceholder(props.field))
const inputType = computed(() => filterInputType(props.field.dataType))
</script>

<template>
  <div class="space-y-3">
    <NgbMultiSelect
      v-if="isLookup && field.isMulti"
      :model-value="state.items"
      :items="lookupItems"
      :disabled="disabled"
      :placeholder="placeholder"
      @query="emit('lookup-query', $event)"
      @update:model-value="emit('update:items', $event as FilterLookupItem[])"
    />

    <NgbLookup
      v-else-if="isLookup"
      :model-value="selectedItem"
      :items="lookupItems"
      :disabled="disabled"
      :show-open="showOpen"
      :show-clear="showClear"
      :placeholder="placeholder"
      @query="emit('lookup-query', $event)"
      @update:model-value="emit('update:items', $event ? [$event as FilterLookupItem] : [])"
      @open="emit('open')"
    />

    <NgbSelect
      v-else-if="hasOptions && !field.isMulti"
      :model-value="state.raw"
      :options="selectOptions"
      :disabled="disabled"
      @update:model-value="emit('update:raw', String($event ?? ''))"
    />

    <NgbInput
      v-else
      :model-value="state.raw"
      :type="inputType"
      :disabled="disabled"
      :placeholder="placeholder"
      @update:model-value="emit('update:raw', String($event ?? ''))"
    />

    <div
      v-if="shouldShowIncludeDescendants"
      class="flex items-center justify-between gap-3 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 py-2"
    >
      <div class="text-sm text-ngb-text">{{ includeDescendantsLabel }}</div>
      <NgbSwitch
        :model-value="!!state.includeDescendants"
        :disabled="disabled"
        @update:model-value="emit('update:includeDescendants', $event)"
      />
    </div>
  </div>
</template>
