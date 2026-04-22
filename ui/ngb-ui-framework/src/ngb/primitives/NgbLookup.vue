<template>
  <div class="w-full">
    <label v-if="label" class="block text-xs font-semibold text-ngb-muted mb-1">{{ label }}</label>

    <Combobox
      :modelValue="modelValue"
      @update:modelValue="onSelect"
      :disabled="comboboxDisabled"
      by="id"
    >
      <div class="relative" :title="tooltipText || undefined">
        <ComboboxInput
          ref="inputRef"
          :class="[inputClass, comboboxDisabled ? 'opacity-60 cursor-not-allowed' : '']"
          :displayValue="displayValue"
          :placeholder="placeholder"
          :disabled="comboboxDisabled"
          :readonly="readonly"
          :title="tooltipText || undefined"
          @input="onInput(($event.target as HTMLInputElement).value)"
          @mousedown.capture="updatePosition"
          @focus.capture="updatePosition"
          @keydown.arrow-down.capture="updatePosition"
          @keydown.enter.capture="updatePosition"
        />

        <div class="absolute inset-y-0 right-0 flex items-center pr-1">
          <button
            v-if="showOpen"
            type="button"
            :class="actionButtonClass"
            :disabled="disabled || !modelValue"
            aria-label="Open"
            title="Open"
            @mousedown.prevent.stop
            @click.prevent.stop="emit('open')"
          >
            <NgbIcon name="open-in-new" :size="actionIconSize" />
          </button>

          <button
            v-if="showClear"
            type="button"
            :class="actionButtonClass"
            :disabled="disabled || !modelValue"
            aria-label="Clear"
            title="Clear"
            @mousedown.prevent.stop
            @click.prevent.stop="clearSelection"
          >
            <NgbIcon name="x" :size="actionIconSize" />
          </button>

          <ComboboxButton
            :class="[dropdownButtonClass, comboboxDisabled ? 'pointer-events-none' : '']"
            aria-label="Show options"
            @mousedown.capture="updatePosition"
          >
            ▾
          </ComboboxButton>
        </div>

        <!--
          Teleport dropdown to body to avoid clipping inside tables/scroll regions.
          Position is computed from the input's viewport rect.
        -->
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
                class="px-3 py-2 rounded-[var(--ngb-radius)] text-sm flex items-start justify-between gap-3"
                :class="active ? 'bg-[var(--ngb-row-hover)]' : ''"
                :title="optionTooltip(item) || undefined"
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
            v-else-if="query && !comboboxDisabled"
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
import { Combobox, ComboboxInput, ComboboxButton, ComboboxOptions, ComboboxOption } from '@headlessui/vue'
import NgbIcon from './NgbIcon.vue'
import { useFloatingDropdownPosition } from './useFloatingDropdownPosition'
import { useAsyncComboboxQuery } from './useAsyncComboboxQuery'

export type LookupItem = { id: string; label: string; meta?: string }
type LookupVariant = 'default' | 'grid' | 'compact'
type FloatingTarget = HTMLElement | ComponentPublicInstance | null

const props = withDefaults(defineProps<{
  modelValue: LookupItem | null
  items: LookupItem[]
  label?: string
  hint?: string
  placeholder?: string
  disabled?: boolean
  readonly?: boolean
  showOpen?: boolean
  showClear?: boolean
  // Visual variant.
  // - default: standard input (bordered)
  // - grid: borderless cell-style input (used inside line grids)
  // - compact: compact bordered input (used in page headers/toolbars)
  variant?: LookupVariant
}>(), {
  label: '',
  hint: '',
  placeholder: '',
  disabled: false,
  readonly: false,
  showOpen: false,
  showClear: false,
  variant: 'default',
})

const emit = defineEmits<{
  (e: 'update:modelValue', v: LookupItem | null): void
  (e: 'query', v: string): void
  (e: 'open'): void
}>()

const comboboxDisabled = computed(() => !!props.disabled || !!props.readonly)

const actionCount = computed(() => 1 + (props.showOpen ? 1 : 0) + (props.showClear ? 1 : 0))

function inputPaddingClass(count: number, variant: LookupVariant): string {
  if (variant === 'grid') {
    if (count >= 3) return 'pr-20'
    if (count === 2) return 'pr-14'
    return 'pr-9'
  }

  if (variant === 'compact') {
    if (count >= 3) return 'pr-20'
    if (count === 2) return 'pr-14'
    return 'pr-8'
  }

  if (count >= 3) return 'pr-24'
  if (count === 2) return 'pr-16'
  return 'pr-10'
}

const inputClass = computed(() => {
  if (props.variant === 'grid') {
    return `h-8 w-full rounded-none border border-transparent bg-transparent px-2 text-sm text-ngb-text placeholder:text-ngb-muted/70 ngb-focus ${inputPaddingClass(actionCount.value, props.variant)}`
  }

  if (props.variant === 'compact') {
    return `h-[26px] w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 text-xs text-ngb-text placeholder:text-ngb-muted/70 shadow-card ngb-focus ${inputPaddingClass(actionCount.value, props.variant)}`
  }

  return `h-9 w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 text-sm text-ngb-text placeholder:text-ngb-muted/70 ngb-focus ${inputPaddingClass(actionCount.value, props.variant)}`
})

const actionButtonClass = computed(() => {
  const base = 'inline-flex items-center justify-center text-ngb-muted transition-colors hover:text-ngb-text disabled:cursor-not-allowed disabled:opacity-40'
  if (props.variant === 'grid') return `${base} h-8 w-6`
  if (props.variant === 'compact') return `${base} h-[26px] w-6 text-[11px]`
  return `${base} h-9 w-8`
})

const dropdownButtonClass = computed(() => {
  if (props.variant === 'grid') return `${actionButtonClass.value} text-[11px]`
  if (props.variant === 'compact') return `${actionButtonClass.value} text-[10px]`
  return actionButtonClass.value
})

const actionIconSize = computed(() => (props.variant === 'compact' ? 13 : 14))
const tooltipText = computed(() => {
  const label = String(props.modelValue?.label ?? '').trim()
  return label.length > 0 ? label : ''
})

const {
  query,
  isSearching,
  clearPendingState,
  onInput,
  resetQueryState,
} = useAsyncComboboxQuery({
  disabled: comboboxDisabled,
  items: computed(() => props.items),
  emitQuery: (value) => emit('query', value),
})

const filteredItems = computed(() => {
  const q = query.value.trim().toLowerCase()
  if (!q) return props.items.slice(0, 50)
  return props.items
    .filter((item) => `${item.label} ${item.meta ?? ''}`.toLowerCase().includes(q))
    .slice(0, 50)
})

function displayValue(item: LookupItem | null) {
  return item?.label ?? ''
}

function optionTooltip(item: LookupItem): string {
  const label = String(item.label ?? '').trim()
  const meta = String(item.meta ?? '').trim()
  if (!meta) return label
  if (!label) return meta
  return `${label} - ${meta}`
}

onBeforeUnmount(() => clearPendingState())

function onSelect(v: LookupItem | null) {
  emit('update:modelValue', v)
  resetQueryState()
}

function clearSelection() {
  if (!props.modelValue) return
  emit('update:modelValue', null)
  resetQueryState({ emitEmptyQuery: true })
}

const inputRef = ref<FloatingTarget>(null)
const optionsRef = ref<FloatingTarget>(null)
const emptyRef = ref<FloatingTarget>(null)

const { floatingStyle, updatePosition } = useFloatingDropdownPosition(inputRef, [optionsRef, emptyRef])

watch(
  () => [filteredItems.value.length, query.value, isSearching.value] as const,
  () => {
    updatePosition()
  },
)
</script>
