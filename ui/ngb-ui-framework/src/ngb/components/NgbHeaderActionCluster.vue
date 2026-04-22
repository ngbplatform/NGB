<template>
  <div class="inline-flex max-w-full items-center gap-1">
    <div v-if="primaryActions.length > 0" class="flex min-w-0 items-center gap-1">
      <button
        v-for="item in primaryActions"
        :key="item.key"
        type="button"
        class="flex h-8 w-8 items-center justify-center rounded-[calc(var(--ngb-radius)-1px)] text-ngb-muted transition-colors hover:bg-ngb-bg hover:text-ngb-text ngb-focus disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-ngb-muted"
        :title="item.title"
        :aria-label="item.title"
        :disabled="item.disabled"
        @click="emit('action', item.key)"
      >
        <NgbIcon :name="item.icon" :size="17" />
      </button>
    </div>

    <Menu v-if="hasMoreActions" as="div" class="relative shrink-0">
      <MenuButton
        class="flex h-8 w-8 items-center justify-center rounded-[calc(var(--ngb-radius)-1px)] text-ngb-muted transition-colors hover:bg-ngb-bg hover:text-ngb-text ngb-focus disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-ngb-muted"
        title="More actions"
        aria-label="More actions"
      >
        <NgbIcon name="more-vertical" :size="17" />
      </MenuButton>

      <MenuItems class="absolute right-0 z-20 mt-2 w-64 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-1.5 shadow-card focus:outline-none">
        <div v-for="(group, groupIndex) in visibleMoreGroups" :key="group.key">
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
              @click="close(); emit('action', item.key)"
            >
              <span class="flex h-4 w-4 items-center justify-center text-ngb-muted">
                <NgbIcon :name="item.icon" :size="16" />
              </span>
              <span class="truncate">{{ item.title }}</span>
            </button>
          </MenuItem>

          <div v-if="groupIndex < visibleMoreGroups.length - 1" class="my-1 h-px bg-ngb-border" />
        </div>
      </MenuItems>
    </Menu>

    <div class="mx-1 h-6 w-px shrink-0 bg-ngb-border" />

    <button
      type="button"
      class="flex h-8 w-8 shrink-0 items-center justify-center rounded-[calc(var(--ngb-radius)-1px)] text-ngb-muted transition-colors hover:bg-ngb-bg hover:text-ngb-text ngb-focus disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-ngb-muted"
      title="Close"
      aria-label="Close"
      :disabled="closeDisabled"
      @click="emit('close')"
    >
      <NgbIcon name="x" :size="17" />
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Menu, MenuButton, MenuItems, MenuItem } from '@headlessui/vue'

import NgbIcon from '../primitives/NgbIcon.vue'

type HeaderActionIconName =
  | 'panel-right'
  | 'open-in-new'
  | 'copy'
  | 'printer'
  | 'trash'
  | 'trash-restore'
  | 'save'
  | 'check'
  | 'undo'
  | 'file-apply'
  | 'share'
  | 'history'
  | 'effects-flow'
  | 'document-flow'

type HeaderActionItem = {
  key: string
  title: string
  icon: HeaderActionIconName
  disabled?: boolean
}

type HeaderActionGroup = {
  key: string
  label?: string
  items: HeaderActionItem[]
}

const props = withDefaults(
  defineProps<{
    primaryActions?: HeaderActionItem[]
    moreGroups?: HeaderActionGroup[]
    closeDisabled?: boolean
  }>(),
  {
    primaryActions: () => [],
    moreGroups: () => [],
    closeDisabled: false,
  },
)

const emit = defineEmits<{
  (e: 'action', key: string): void
  (e: 'close'): void
}>()

const visibleMoreGroups = computed(() =>
  props.moreGroups
    .map((group) => ({
      ...group,
      items: (group.items ?? []).filter(Boolean),
    }))
    .filter((group) => group.items.length > 0),
)

const hasMoreActions = computed(() => visibleMoreGroups.value.length > 0)
</script>
