<template>
  <div class="w-full">
    <label v-if="label" class="block text-xs font-semibold text-ngb-muted mb-1">{{ label }}</label>

    <Combobox
      multiple
      :modelValue="modelValue"
      @update:modelValue="onSelect"
      :disabled="disabled"
      by="id"
    >
      <div ref="anchorRef" class="relative">
        <div
          class="min-h-9 w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-2 py-1 flex flex-wrap items-center gap-1 ngb-focus"
          :class="disabled ? 'opacity-60 cursor-not-allowed' : ''"
        >
          <span
            v-for="item in modelValue"
            :key="item.id"
            class="inline-flex items-center gap-1 rounded-full border border-ngb-border bg-[var(--ngb-row-hover)] px-2 py-0.5 text-[11px] font-semibold"
            :title="multiSelectItemTooltip(item) || undefined"
          >
            <span class="truncate max-w-[180px]">{{ item.label }}</span>
            <button
              v-if="!disabled"
              type="button"
              class="text-ngb-muted hover:text-ngb-text"
              aria-label="Remove"
              @click.stop="remove(item)"
            >
              ×
            </button>
          </span>

          <ComboboxInput
            class="h-7 flex-1 min-w-[120px] bg-transparent px-2 text-sm text-ngb-text placeholder:text-ngb-muted/70 outline-none"
            :displayValue="() => ''"
            :placeholder="modelValue.length === 0 ? placeholder : ''"
            @input="onQueryInput(($event.target as HTMLInputElement).value)"
            @mousedown.capture="updatePosition"
            @focus.capture="updatePosition"
            @keydown.arrow-down.capture="updatePosition"
            @keydown.enter.capture="updatePosition"
            @keydown.backspace.capture="onBackspace"
          />
        </div>

        <Teleport to="body">
          <ComboboxOptions
            v-if="filteredItems.length > 0"
            ref="optionsRef"
            :style="floatingStyle"
            class="fixed z-[1000] max-h-72 overflow-auto rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card p-1 focus:outline-none"
          >
            <ComboboxOption
              v-for="item in filteredItems"
              :key="item.id"
              :value="item"
              v-slot="{ active, selected }"
            >
              <div
                class="px-3 py-2 rounded-[var(--ngb-radius)] text-sm flex items-center justify-between gap-3"
                :class="active ? 'bg-[var(--ngb-row-hover)]' : ''"
                :title="multiSelectItemTooltip(item) || undefined"
              >
                <div class="min-w-0">
                  <div class="truncate" :class="selected ? 'font-semibold' : ''">{{ item.label }}</div>
                  <div v-if="item.meta" class="text-xs text-ngb-muted truncate mt-0.5">{{ item.meta }}</div>
                </div>
                <div v-if="selected" class="text-ngb-blue shrink-0">✓</div>
              </div>
            </ComboboxOption>
          </ComboboxOptions>

          <div
            v-else-if="query.trim() && !disabled"
            ref="emptyRef"
            :style="floatingStyle"
            class="fixed z-[1000] rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card p-3 text-sm text-ngb-muted"
          >
            <span v-if="isSearching">Searching…</span>
            <span v-else>No results</span>
          </div>
        </Teleport>
      </div>
    </Combobox>

    <div v-if="hint" class="mt-1 text-xs text-ngb-muted">{{ hint }}</div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch, type ComponentPublicInstance } from 'vue'
import { Combobox, ComboboxInput, ComboboxOptions, ComboboxOption } from '@headlessui/vue'

import { useFloatingDropdownPosition } from './useFloatingDropdownPosition'
import { useAsyncComboboxQuery } from './useAsyncComboboxQuery'

export type MultiSelectItem = { id: string; label: string; meta?: string }
type FloatingTarget = HTMLElement | ComponentPublicInstance | null

const props = defineProps<{
  modelValue: MultiSelectItem[]
  items: MultiSelectItem[]
  label?: string
  hint?: string
  placeholder?: string
  disabled?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', v: MultiSelectItem[]): void
  (e: 'query', v: string): void
}>()

const {
  query,
  isSearching,
  resetQueryState,
  onInput,
} = useAsyncComboboxQuery({
  disabled: computed(() => !!props.disabled),
  items: computed(() => props.items),
  emitQuery: (value) => emit('query', value),
})

const filteredItems = computed(() => {
  const normalizedQuery = query.value.trim().toLowerCase()
  const selectedIds = new Set(props.modelValue.map((item) => item.id))
  const source = props.items.filter((item) => !selectedIds.has(item.id))
  if (!normalizedQuery) return source.slice(0, 50)

  return source
    .filter((item) => `${item.label} ${item.meta ?? ''}`.toLowerCase().includes(normalizedQuery))
    .slice(0, 50)
})
const anchorRef = ref<FloatingTarget>(null)
const optionsRef = ref<FloatingTarget>(null)
const emptyRef = ref<FloatingTarget>(null)

const { floatingStyle, updatePosition } = useFloatingDropdownPosition(anchorRef, [optionsRef, emptyRef])

function remove(item: MultiSelectItem) {
  emit('update:modelValue', props.modelValue.filter((entry) => entry.id !== item.id))
}

function multiSelectItemTooltip(item: MultiSelectItem): string {
  const label = String(item.label ?? '').trim()
  const meta = String(item.meta ?? '').trim()
  if (!meta) return label
  if (!label) return meta
  return `${label} - ${meta}`
}

function onQueryInput(value: string) {
  onInput(value)
  updatePosition()
}

function onSelect(value: MultiSelectItem[]) {
  emit('update:modelValue', value)
  resetQueryState({ emitEmptyQuery: true })
}

function onBackspace(event: KeyboardEvent) {
  if (query.value.trim().length > 0 || props.modelValue.length === 0) return
  event.stopPropagation()
  remove(props.modelValue[props.modelValue.length - 1]!)
}

watch(
  () => filteredItems.value.length,
  () => {
    updatePosition()
  },
)

onBeforeUnmount(() => resetQueryState())
</script>
