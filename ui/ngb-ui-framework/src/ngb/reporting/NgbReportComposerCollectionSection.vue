<script setup lang="ts" generic="TItem">
import type { VNodeChild } from 'vue'
import NgbButton from '../primitives/NgbButton.vue'
import NgbIcon from '../primitives/NgbIcon.vue'

type ComposerCollectionColumn = {
  title: string
  width?: string
}

const props = withDefaults(defineProps<{
  title: string
  addLabel: string
  items: TItem[]
  columns: ComposerCollectionColumn[]
  emptyMessage: string
  section: string
  rowKey: (item: TItem, index: number) => string
  addDisabled?: boolean
  tableClass?: string
}>(), {
  addDisabled: false,
  tableClass: 'w-full table-fixed text-sm',
})

const emit = defineEmits<{
  (e: 'add'): void
  (e: 'remove', index: number): void
  (e: 'dragstart', payload: { section: string; index: number; event: DragEvent }): void
  (e: 'dragover', event: DragEvent): void
  (e: 'drop', payload: { section: string; index: number; event: DragEvent }): void
}>()

defineSlots<{
  cells(props: { item: TItem; index: number }): VNodeChild
}>()
</script>

<template>
  <div class="space-y-4">
    <div class="flex items-center justify-between gap-3">
      <div class="text-xs font-semibold uppercase tracking-wide text-ngb-muted">{{ title }}</div>
      <NgbButton size="sm" :disabled="addDisabled" @click="emit('add')">
        <NgbIcon name="plus" :size="16" />
        <span>{{ addLabel }}</span>
      </NgbButton>
    </div>

    <div class="overflow-x-auto overflow-y-hidden rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card">
      <table v-if="items.length > 0" :class="tableClass">
        <colgroup>
          <col style="width: 28px" />
          <col v-for="(column, index) in columns" :key="`${title}:col:${index}`" :style="column.width ? `width: ${column.width}` : undefined" />
          <col style="width: 40px" />
        </colgroup>
        <thead class="bg-ngb-bg text-xs text-ngb-muted">
          <tr>
            <th class="px-2 py-2"></th>
            <th
              v-for="(column, index) in columns"
              :key="`${title}:head:${index}`"
              class="border-r border-dotted border-ngb-border px-3 py-2 text-left font-semibold truncate"
            >
              {{ column.title }}
            </th>
            <th class="px-2 py-2"></th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(item, index) in items"
            :key="rowKey(item, index)"
            class="border-t border-ngb-border transition-colors hover:bg-ngb-bg align-top"
            draggable="true"
            @dragstart="emit('dragstart', { section, index, event: $event })"
            @dragover="emit('dragover', $event)"
            @drop="emit('drop', { section, index, event: $event })"
          >
            <td class="px-1 py-1 align-middle">
              <div class="flex h-8 w-6 items-center justify-center text-ngb-muted cursor-grab active:cursor-grabbing" title="Drag to reorder">
                <NgbIcon name="grip-vertical" :size="16" />
              </div>
            </td>

            <slot name="cells" :item="item" :index="index" />

            <td class="px-1 py-1 align-middle">
              <button
                type="button"
                class="flex h-8 w-8 items-center justify-center rounded-[var(--ngb-radius)] text-ngb-muted hover:bg-ngb-bg hover:text-ngb-text ngb-focus"
                title="Delete"
                @click="emit('remove', index)"
              >
                <NgbIcon name="trash" :size="16" />
              </button>
            </td>
          </tr>
        </tbody>
      </table>

      <div v-else class="px-4 py-3 text-sm text-ngb-muted">
        {{ emptyMessage }}
      </div>
    </div>
  </div>
</template>
