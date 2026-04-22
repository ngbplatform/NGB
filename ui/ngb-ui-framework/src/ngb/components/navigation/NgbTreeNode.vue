<template>
  <div class="select-none">
    <div
      role="treeitem"
      tabindex="0"
      :aria-level="level + 1"
      :aria-selected="selectedId === node.id"
      :aria-expanded="hasChildren ? isExpanded : undefined"
      class="h-9 rounded-[var(--ngb-radius)] flex items-center gap-2 px-2 text-sm cursor-pointer"
      :class="selectedId === node.id ? 'bg-[#F1F5F9] border border-[#E2E8F0]' : 'hover:bg-[#F8FAFC]'"
      :style="{ paddingLeft: `${8 + level * 14}px` }"
      @click="$emit('select', node)"
      @keydown="onRowKeyDown"
    >
      <button
        v-if="hasChildren"
        type="button"
        class="w-6 h-6 inline-flex items-center justify-center rounded hover:bg-[#F1F5F9] text-ngb-muted"
        @click.stop="$emit('toggle', node.id)"
        aria-label="Toggle"
      >
        {{ isExpanded ? '▾' : '▸' }}
      </button>
      <span v-else class="w-6 h-6 inline-flex items-center justify-center text-ngb-muted">•</span>

      <span class="truncate flex-1 text-ngb-text">{{ node.label }}</span>

      <span
        v-if="node.badge"
        class="text-[11px] font-semibold px-2 py-0.5 rounded-full border border-ngb-border text-ngb-muted"
      >
        {{ node.badge }}
      </span>
    </div>

    <div v-if="hasChildren && isExpanded" class="mt-1" role="group">
      <NgbTreeNode
        v-for="c in node.children"
        :key="c.id"
        :node="c"
        :level="level + 1"
        :expanded="expanded"
        :selectedId="selectedId"
        @toggle="$emit('toggle', $event)"
        @select="$emit('select', $event)"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';

export type NavNode = {
  id: string;
  label: string;
  children?: NavNode[];
  badge?: string;
};

const props = defineProps<{
  node: NavNode;
  level: number;
  expanded: Set<string>;
  selectedId: string | null;
}>();

const emit = defineEmits<{
  (e: 'toggle', id: string): void;
  (e: 'select', node: NavNode): void;
}>();

const hasChildren = computed(() => !!(props.node.children && props.node.children.length));
const isExpanded = computed(() => props.expanded.has(props.node.id));

function onRowKeyDown(event: KeyboardEvent) {
  if (event.key === 'Enter' || event.key === ' ' || event.key === 'Spacebar') {
    event.preventDefault();
    emit('select', props.node);
    return;
  }

  if (event.key === 'ArrowRight' && hasChildren.value && !isExpanded.value) {
    event.preventDefault();
    emit('toggle', props.node.id);
    return;
  }

  if (event.key === 'ArrowLeft' && hasChildren.value && isExpanded.value) {
    event.preventDefault();
    emit('toggle', props.node.id);
  }
}
</script>
