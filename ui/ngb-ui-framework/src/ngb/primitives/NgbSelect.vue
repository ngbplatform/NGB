<template>
  <div class="w-full">
    <label v-if="label" class="block text-xs font-semibold text-ngb-muted mb-1">{{ label }}</label>

    <Listbox :modelValue="modelValue" @update:modelValue="(value) => emit('update:modelValue', value)" :disabled="disabled">
      <div class="relative">
        <ListboxButton
          ref="btnRef"
          :class="[buttonClass, disabled ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer']"
          @mousedown.capture="updatePosition"
          @keydown.enter.capture="updatePosition"
          @keydown.space.capture="updatePosition"
          @keydown.arrow-down.capture="updatePosition"
        >
          <span class="truncate">{{ displayValue }}</span>
          <span class="text-ngb-muted">▾</span>
        </ListboxButton>

        <!--
          IMPORTANT: this select is used inside line grids (tables) where ancestors often have overflow hidden.
          HeadlessUI renders options as an absolutely positioned element under the button, which gets clipped.
          We teleport the dropdown to <body> and position it using the button's viewport rect.
        -->
        <Teleport to="body">
          <ListboxOptions
            ref="optionsRef"
            :style="floatingStyle"
            class="fixed z-[1000] max-h-64 overflow-auto rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card p-1 focus:outline-none"
          >
            <ListboxOption
              v-for="o in options"
              :key="o.value"
              :value="o.value"
              v-slot="{ active, selected }"
            >
              <div
                class="px-3 py-2 rounded-[var(--ngb-radius)] text-sm flex items-center justify-between"
                :class="active ? 'bg-[var(--ngb-row-hover)]' : ''"
              >
                <span class="truncate" :class="selected ? 'font-semibold' : ''">{{ o.label }}</span>
                <span v-if="selected" class="text-ngb-blue">✓</span>
              </div>
            </ListboxOption>
          </ListboxOptions>
        </Teleport>
      </div>
    </Listbox>

    <div v-if="hint" class="mt-1 text-xs text-ngb-muted">{{ hint }}</div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, type ComponentPublicInstance } from 'vue'
import { Listbox, ListboxButton, ListboxOptions, ListboxOption } from '@headlessui/vue'
import { useFloatingDropdownPosition } from './useFloatingDropdownPosition'

export type SelectOption = {
  value: unknown
  label: string
}

type SelectVariant = 'default' | 'grid' | 'compact'
type FloatingTarget = HTMLElement | ComponentPublicInstance | null

const props = withDefaults(defineProps<{
  modelValue: unknown
  options: SelectOption[]
  label?: string
  hint?: string
  disabled?: boolean
  // Visual variant.
  // - default: standard select (bordered)
  // - grid: borderless cell-style select (used inside line grids)
  // - compact: compact select (used in page headers/toolbars)
  variant?: SelectVariant
}>(), {
  label: '',
  hint: '',
  disabled: false,
  variant: 'default',
})

const emit = defineEmits<{
  (e: 'update:modelValue', value: unknown): void
}>()

const displayValue = computed(() => {
  const found = props.options.find((option) => option.value === props.modelValue)
  return found ? found.label : 'Select…'
})

const buttonClass = computed(() => {
  if (props.variant === 'grid') {
    return 'h-8 w-full rounded-none border border-transparent bg-transparent px-2 text-sm text-ngb-text flex items-center justify-between ngb-focus'
  }

  if (props.variant === 'compact') {
    return 'h-[26px] w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-2.5 text-xs text-ngb-text flex items-center justify-between shadow-card ngb-focus'
  }
  return 'h-9 w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 text-sm text-ngb-text flex items-center justify-between ngb-focus'
})

const btnRef = ref<FloatingTarget>(null)
const optionsRef = ref<FloatingTarget>(null)

const { floatingStyle, updatePosition } = useFloatingDropdownPosition(btnRef, [optionsRef])
</script>
