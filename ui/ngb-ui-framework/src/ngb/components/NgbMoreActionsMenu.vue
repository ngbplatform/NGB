<script setup lang="ts">
import { computed } from 'vue'
import { Menu, MenuButton, MenuItems, MenuItem } from '@headlessui/vue'

import NgbIcon from '../primitives/NgbIcon.vue'
import type { NgbIconName } from '../primitives/iconNames'

const props = defineProps<{
  groups: {
    key: string
    label?: string
    items: {
      key: string
      title: string
      icon: NgbIconName
      disabled?: boolean
      badge?: number | null
      ariaLabel?: string
    }[]
  }[]
}>()

const emit = defineEmits<{
  (e: 'action', key: string): void
}>()

const visibleGroups = computed(() => props.groups.filter(group => group.items.length > 0))

function activate(key: string, close: () => void) {
  close()
  emit('action', key)
}
</script>

<template>
  <Menu v-if="visibleGroups.length" as="div" class="relative shrink-0">
    <MenuButton
      class="flex h-8 w-8 items-center justify-center rounded-[calc(var(--ngb-radius)-1px)] text-ngb-muted transition-colors hover:bg-ngb-bg hover:text-ngb-text ngb-focus disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-ngb-muted"
      title="More actions"
      aria-label="More actions"
    >
      <NgbIcon name="more-vertical" :size="17" />
    </MenuButton>

    <MenuItems class="absolute right-0 z-20 mt-2 w-64 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-1.5 shadow-card focus:outline-none">
      <div v-for="(group, groupIndex) in visibleGroups" :key="group.key">
        <div v-if="group.label" class="px-2 py-1 text-[11px] font-semibold uppercase tracking-[0.08em] text-ngb-muted">
          {{ group.label }}
        </div>

        <MenuItem
          v-for="item in group.items"
          :key="item.key"
          as="template"
          :disabled="item.disabled"
          v-slot="{ active, disabled, close }"
        >
          <button
            type="button"
            class="flex w-full items-center gap-3 rounded-[var(--ngb-radius)] px-2.5 py-2 text-left text-sm transition-colors"
            :class="[
              disabled ? 'cursor-not-allowed opacity-40' : '',
              active && !disabled ? 'bg-ngb-bg text-ngb-text' : 'text-ngb-text',
            ]"
            :disabled="disabled"
            :aria-label="item.ariaLabel"
            @click="activate(item.key, close)"
          >
            <span class="flex h-4 w-4 items-center justify-center text-ngb-muted">
              <NgbIcon :name="item.icon" :size="16" />
            </span>
            <span class="truncate">{{ item.title }}</span>
            <span
              v-if="item.badge && item.badge > 0"
              class="ml-auto min-w-5 rounded-full bg-ngb-primary px-1.5 text-center text-xs leading-5 text-white"
              aria-hidden="true"
            >
              {{ item.badge }}
            </span>
          </button>
        </MenuItem>

        <div v-if="groupIndex < visibleGroups.length - 1" class="my-1 h-px bg-ngb-border" />
      </div>
    </MenuItems>
  </Menu>
</template>
