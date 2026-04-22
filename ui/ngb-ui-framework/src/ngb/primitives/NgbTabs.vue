<template>
  <div :class="fill ? 'w-full h-full flex flex-col min-h-0' : 'w-full'">
    <div
      ref="tablistRef"
      role="tablist"
      aria-orientation="horizontal"
      :class="fullWidthBar ? 'flex w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-1' : 'inline-flex rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-1'"
    >
      <button
        v-for="(t, index) in tabs"
        :key="keyOf(t)"
        type="button"
        role="tab"
        :id="tabId(keyOf(t))"
        :data-tab-index="index"
        :aria-selected="keyOf(t) === modelValue"
        :aria-controls="panelId"
        :tabindex="keyOf(t) === modelValue ? 0 : -1"
        class="h-8 px-3 rounded-[var(--ngb-radius)] text-sm font-medium transition-colors ngb-focus"
        :class="keyOf(t) === modelValue
          ? 'bg-ngb-bg text-ngb-text shadow-sm'
          : 'bg-transparent text-ngb-muted hover:bg-ngb-bg hover:text-ngb-text'
          "
        @click="emit('update:modelValue', keyOf(t))"
        @keydown="onTabKeyDown(index, $event)"
      >
        {{ t.label }}
      </button>
    </div>

    <div
      :id="panelId"
      role="tabpanel"
      :aria-labelledby="activeTabId"
      :class="fill ? 'mt-3 flex-1 min-h-0' : 'mt-3'"
    >
      <slot :active="modelValue" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'

export type TabItem = { key?: string; id?: string; label: string };

const props = withDefaults(
  defineProps<{
    modelValue: string;
    tabs: TabItem[];
    fill?: boolean;
    fullWidthBar?: boolean;
  }>(),
  {
    fill: false,
    fullWidthBar: false,
  },
);

const emit = defineEmits<{
  (e: 'update:modelValue', v: string): void;
}>()

const tablistRef = ref<HTMLElement | null>(null)
const activeTabId = computed(() => tabId(props.modelValue))
const panelId = computed(() => `${tabId(props.modelValue)}-panel`)

function keyOf(t: TabItem) {
  return t.key ?? t.id ?? '';
}

function tabId(key: string) {
  const normalized = String(key ?? '').trim().replace(/[^a-z0-9_-]+/gi, '-')
  return `ngb-tab-${normalized || 'item'}`
}

function focusTab(index: number) {
  tablistRef.value
    ?.querySelector<HTMLElement>(`[data-tab-index="${index}"]`)
    ?.focus()
}

function emitActiveIndex(index: number) {
  const tab = props.tabs[index]
  if (!tab) return
  emit('update:modelValue', keyOf(tab))
  void nextTick(() => focusTab(index))
}

function onTabKeyDown(index: number, event: KeyboardEvent) {
  if (!props.tabs.length) return

  if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
    event.preventDefault()
    emitActiveIndex((index + 1) % props.tabs.length)
    return
  }

  if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
    event.preventDefault()
    emitActiveIndex((index - 1 + props.tabs.length) % props.tabs.length)
    return
  }

  if (event.key === 'Home') {
    event.preventDefault()
    emitActiveIndex(0)
    return
  }

  if (event.key === 'End') {
    event.preventDefault()
    emitActiveIndex(props.tabs.length - 1)
  }
}
</script>
