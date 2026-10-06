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

    <NgbMoreActionsMenu :groups="moreGroups" @action="(key) => emit('action', key)" />

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
import NgbMoreActionsMenu from './NgbMoreActionsMenu.vue'

import NgbIcon from '../primitives/NgbIcon.vue'
import type { NgbIconName } from '../primitives/iconNames'

type HeaderActionItem = {
  key: string
  title: string
  icon: NgbIconName
  disabled?: boolean
  badge?: number | null
  ariaLabel?: string
}

type HeaderActionGroup = {
  key: string
  label?: string
  items: HeaderActionItem[]
}

withDefaults(
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
</script>
