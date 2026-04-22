<template>
  <aside class="w-full h-full bg-white border-r border-ngb-border">
    <div class="p-3 border-b border-ngb-border">
      <div class="text-xs font-semibold text-ngb-muted mb-2">Navigation</div>
      <input
        v-model="q"
        class="h-9 w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-white px-3 text-sm text-ngb-text placeholder:text-ngb-muted/70 ngb-focus"
        placeholder="Search…"
        aria-label="Search navigation"
      />
    </div>

    <div class="p-2 overflow-auto h-[calc(100%-61px)]" role="tree" aria-label="Navigation">
      <NgbTreeNode
        v-for="n in visibleNodes"
        :key="n.id"
        :node="n"
        :level="0"
        :expanded="expanded"
        :selectedId="modelValue ?? null"
        @toggle="toggle"
        @select="select"
      />
    </div>
  </aside>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import NgbTreeNode, { type NavNode } from './NgbTreeNode.vue';

const props = defineProps<{
  nodes: NavNode[];
  modelValue?: string | null;
}>();

const emit = defineEmits<{
  (e: 'update:modelValue', v: string | null): void;
  (e: 'select', node: NavNode): void;
}>();

const q = ref('');
const expanded = ref<Set<string>>(new Set(['documents', 'catalogs', 'reports']));

function toggle(id: string) {
  const s = new Set(expanded.value);
  if (s.has(id)) s.delete(id);
  else s.add(id);
  expanded.value = s;
}

function select(node: NavNode) {
  emit('update:modelValue', node.id);
  emit('select', node);
}

function matchNode(node: NavNode, query: string): boolean {
  const hit = node.label.toLowerCase().includes(query);
  const childHit = (node.children ?? []).some(c => matchNode(c, query));
  return hit || childHit;
}

const visibleNodes = computed(() => {
  const query = q.value.trim().toLowerCase();
  if (!query) return props.nodes;
  return props.nodes.filter(n => matchNode(n, query));
});
</script>
