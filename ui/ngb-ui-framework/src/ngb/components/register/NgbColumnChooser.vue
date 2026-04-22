<template>
  <div class="relative" ref="root">
    <NgbButton variant="ghost" @click="open = !open">Columns ▾</NgbButton>
    <div v-if="open" class="absolute right-0 mt-2 w-72 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card p-2 z-10">
      <div class="px-2 py-2 text-sm font-semibold text-ngb-text">Columns</div>
      <div class="max-h-[320px] overflow-auto px-2 pb-2">
        <label v-for="c in columns" :key="c.id" class="flex items-center gap-2 py-1.5 text-sm">
          <input type="checkbox" :checked="visibleSet.has(c.id)" @change="toggle(c.id)" />
          <span class="flex-1">{{ c.label }}</span>
        </label>
      </div>
      <div class="px-2 pt-2 border-t border-ngb-border flex items-center justify-between gap-2">
        <button class="text-sm text-ngb-muted hover:text-ngb-text" @click="reset">Reset</button>
        <div class="flex items-center gap-2">
          <NgbButton variant="secondary" @click="open = false">Close</NgbButton>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import NgbButton from '../../primitives/NgbButton.vue';
import { loadJson, saveJson } from '../../utils/storage';

export type ColumnDef = { id: string; label: string; };
type PersistedColumnChooserState = string[] | {
  visible?: string[];
  order?: string[];
  widths?: Record<string, number>;
};

const props = defineProps<{
  columns: ColumnDef[];
  modelValue: string[];
  storageKey?: string;
}>();

const emit = defineEmits<{
  (e: 'update:modelValue', value: string[]): void;
}>();

const open = ref(false);
const root = ref<HTMLElement | null>(null);

const visibleSet = computed(() => new Set(props.modelValue));

function readStoredVisibleColumns(storageKey: string): string[] | null {
  const stored = loadJson<PersistedColumnChooserState | null>(storageKey, null);
  if (!stored) return null;
  if (Array.isArray(stored)) return [...stored];
  return Array.isArray(stored.visible) ? [...stored.visible] : null;
}

function saveStoredVisibleColumns(storageKey: string, visible: string[]) {
  const stored = loadJson<PersistedColumnChooserState | null>(storageKey, null);
  if (stored && !Array.isArray(stored)) {
    saveJson(storageKey, {
      ...stored,
      visible,
    });
    return;
  }

  saveJson(storageKey, {
    visible,
  });
}

function toggle(id: string) {
  const next = new Set(props.modelValue);
  if (next.has(id)) next.delete(id);
  else next.add(id);
  const list = props.columns.map(c => c.id).filter(cid => next.has(cid));
  emit('update:modelValue', list);
  if (props.storageKey) saveStoredVisibleColumns(props.storageKey, list);
}

function reset() {
  const list = props.columns.map(c => c.id);
  emit('update:modelValue', list);
  if (props.storageKey) saveStoredVisibleColumns(props.storageKey, list);
}

function onDocClick(e: MouseEvent) {
  if (!open.value) return;
  const t = e.target as Node;
  if (!root.value) return;
  if (!root.value.contains(t)) open.value = false;
}

onMounted(() => {
  document.addEventListener('mousedown', onDocClick);
  if (props.storageKey) {
    const stored = readStoredVisibleColumns(props.storageKey);
    if (stored) emit('update:modelValue', stored);
  }
});
onBeforeUnmount(() => document.removeEventListener('mousedown', onDocClick));
</script>
