<template>
  <aside
    data-testid="site-sidebar"
    class="h-full bg-ngb-card border-r border-ngb-border flex flex-col min-h-0 relative"
    :class="collapsed ? 'w-[72px]' : 'w-full'"
  >
    <!-- Brand bar aligned with topbar height -->
  <div data-testid="site-sidebar-brand" class="h-14 border-b border-ngb-border relative">
      <!-- Collapsed: centered >> button, no logo/title -->
      <div v-if="collapsed" class="h-full w-full flex items-center justify-center">
        <button
          v-if="allowCollapse"
          class="ngb-iconbtn"
          title="Expand sidebar"
          @click="$emit('toggleCollapsed')"
        >
          <NgbIcon name="chevrons-right" :size="18" />
        </button>
      </div>

      <!-- Expanded: << button top-left, logo+title centered -->
      <div v-else class="h-full w-full">
        <button
          v-if="allowCollapse"
          class="ngb-iconbtn absolute left-3 top-1/2 -translate-y-1/2 z-10"
          title="Collapse sidebar"
          @click="$emit('toggleCollapsed')"
        >
          <NgbIcon name="chevrons-left" :size="18" />
        </button>

        <div class="h-full w-full flex items-center justify-center mt-1">
          <div class="flex flex-col items-center -translate-y-1">
            <NgbLogo
              title="NGB"
              class="block select-none h-8 w-auto"
              :style="{ color: 'var(--ngb-logo)' }"
            />
            <div v-if="brandSubtitle" class="text-sm leading-none font-semibold text-ngb-text/90">
              {{ brandSubtitle }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Nav -->
    <div data-testid="site-sidebar-nav" class="flex-1 min-h-0 overflow-auto py-2" :class="collapsed ? 'px-2' : 'px-3'">
      <!-- Expanded -->
      <div v-if="!collapsed" class="space-y-2">
        <template v-for="n in nodes" :key="n.id">
          <!-- leaf root -->
          <button
            v-if="!(n.children?.length)"
            class="w-full h-9 rounded-[var(--ngb-radius)] px-2 border flex items-center gap-2 text-left"
            :class="leafClass(n)"
            @click="onLeafClick(n)"
          >
            <span class="text-ngb-muted">
              <NgbIcon :name="resolveNodeIcon(n)" :size="18" />
            </span>
            <span class="truncate text-sm">{{ n.label }}</span>
            <span
              v-if="n.badge"
              class="ml-auto text-[11px] px-2 py-0.5 rounded-full border border-ngb-border text-ngb-muted bg-ngb-card"
            >
              {{ n.badge }}
            </span>
          </button>

          <!-- section -->
          <div v-else>
            <button
              class="w-full h-9 rounded-[var(--ngb-radius)] px-2 flex items-center justify-between border border-transparent hover:border-ngb-border hover:bg-ngb-bg"
              @click="toggle(n.id)"
            >
              <span class="flex items-center gap-2 min-w-0">
                <span class="text-ngb-muted">
                  <NgbIcon :name="resolveNodeIcon(n)" :size="18" />
                </span>
                <span class="text-sm font-semibold truncate">{{ n.label }}</span>
              </span>
              <span class="text-ngb-muted text-sm">{{ expanded.has(n.id) ? '▾' : '▸' }}</span>
            </button>

            <div v-if="expanded.has(n.id)" class="mt-1 pl-8 space-y-1">
              <button
                v-for="c in (n.children ?? [])"
                :key="c.id"
                class="w-full h-9 rounded-[var(--ngb-radius)] px-2 border flex items-center gap-2 text-left"
                :class="leafClass(c)"
                @click="onLeafClick(c)"
              >
                <span class="text-ngb-muted">
                  <NgbIcon :name="resolveNodeIcon(c)" :size="16" />
                </span>
                <span class="truncate text-sm">{{ c.label }}</span>
                <span
                  v-if="c.badge"
                  class="ml-auto text-[11px] px-2 py-0.5 rounded-full border border-ngb-border text-ngb-muted bg-ngb-card"
                >
                  {{ c.badge }}
                </span>
              </button>
            </div>
          </div>
        </template>
      </div>

      <!-- Collapsed -->
      <div v-else class="flex flex-col items-center gap-1">
        <template v-for="n in nodes" :key="n.id">
          <button
            class="ngb-iconbtn"
            :class="isRootActive(n) ? 'bg-ngb-bg/80' : ''"
            :title="n.label"
            @click="onCollapsedRootClick(n)"
          >
            <NgbIcon :name="resolveNodeIcon(n)" :size="18" />
            <span v-if="n.badge" class="absolute -top-0.5 -right-0.5 h-2 w-2 rounded-full bg-red-500" />
          </button>
        </template>

        <!-- flyout -->
        <div
          v-if="flyoutRoot"
          data-testid="site-sidebar-flyout"
          class="absolute left-full top-14 z-30 ml-2 w-[260px] max-h-[calc(100%-56px-52px)] overflow-auto rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-lg p-2"
        >
          <div class="px-2 py-1 text-xs font-semibold text-ngb-muted">{{ flyoutRoot.label }}</div>

          <div class="mt-1 space-y-1">
            <button
              v-for="c in (flyoutRoot.children ?? [])"
              :key="c.id"
              class="w-full h-9 rounded-[var(--ngb-radius)] px-2 border flex items-center gap-2 text-left"
              :class="leafClass(c)"
              @click="onLeafClick(c); flyoutRootId = null"
            >
              <span class="text-ngb-muted">
                <NgbIcon :name="resolveNodeIcon(c)" :size="16" />
              </span>
              <span class="truncate text-sm">{{ c.label }}</span>
              <span
                v-if="c.badge"
                class="ml-auto text-[11px] px-2 py-0.5 rounded-full border border-ngb-border text-ngb-muted bg-ngb-card"
              >
                {{ c.badge }}
              </span>
            </button>
          </div>
        </div>
      </div>
    </div>

    <div class="px-3 py-3 border-t border-ngb-border text-xs text-ngb-muted text-center">
      <template v-if="collapsed">
        <span class="font-semibold text-ngb-text">NGB</span>
      </template>
      <template v-else>
        powered by: <span class="font-semibold text-ngb-text">NGB</span>
      </template>
    </div>
  </aside>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import type { SiteNavNode, SiteQuickLink } from './types';
import NgbIcon from '../primitives/NgbIcon.vue';
import NgbLogo from './NgbLogo.vue';
import { coerceNgbIconName, type NgbIconName } from '../primitives/iconNames';

const props = withDefaults(defineProps<{
  productTitle: string;
  brandSubtitle?: string;
  pinned: SiteQuickLink[];
  recent: SiteQuickLink[];
  nodes: SiteNavNode[];
  selectedId: string | null;
  collapsed?: boolean;
  themeResolved?: 'light' | 'dark';
  allowCollapse?: boolean;
}>(), {
  allowCollapse: true,
});

const emit = defineEmits<{
  (e: 'navigate', route: string): void;
  (e: 'select', id: string, route: string): void;
  (e: 'toggleCollapsed'): void;
}>();

const expanded = ref(new Set<string>(props.nodes.map(x => x.id)));
const flyoutRootId = ref<string | null>(null);

const collapsed = computed(() => !!props.collapsed);
const allowCollapse = computed(() => props.allowCollapse);

watch(
  () => props.selectedId,
  () => {
    // when selection changes, close flyout in collapsed mode
    flyoutRootId.value = null;
  },
);

const flyoutRoot = computed(() => props.nodes.find(x => x.id === flyoutRootId.value) ?? null);

function toggle(id: string) {
  const s = new Set(expanded.value);
  if (s.has(id)) s.delete(id);
  else s.add(id);
  expanded.value = s;
}

function isDisabled(n: SiteNavNode) {
  return !!n.disabled || !n.route;
}

function onLeafClick(n: SiteNavNode) {
  if (isDisabled(n) || !n.route) return;
  // leaf click selects and navigates
  emit('select', n.id, n.route);
  emit('navigate', n.route);
}

function leafClass(n: SiteNavNode) {
  if (isDisabled(n)) return 'bg-transparent border-transparent opacity-60 cursor-not-allowed';
  if (props.selectedId === n.id) return 'bg-ngb-bg border-ngb-border';
  return 'bg-transparent border-transparent hover:bg-ngb-bg hover:border-ngb-border';
}

function resolveNodeIcon(n: SiteNavNode): NgbIconName {
  return coerceNgbIconName(n.icon, 'file-text');
}

function isRootActive(root: SiteNavNode) {
  if (props.selectedId === root.id) return true;
  const kids = root.children ?? [];
  return kids.some(x => x.id === props.selectedId);
}

function onCollapsedRootClick(root: SiteNavNode) {
  if (!root.children?.length) {
    onLeafClick(root);
    flyoutRootId.value = null;
    return;
  }
  flyoutRootId.value = flyoutRootId.value === root.id ? null : root.id;
}
</script>
