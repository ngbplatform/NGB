<template>
  <div class="w-full min-w-0 overflow-hidden rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card" :class="fillHeight ? 'flex flex-col min-h-0' : ''">
    <div v-if="showPanel" class="px-4 py-3 border-b border-ngb-border flex items-center justify-between gap-3">
      <div class="min-w-0">
        <div class="text-sm font-semibold text-ngb-text truncate">{{ title }}</div>
        <div class="text-xs text-ngb-muted truncate">{{ subtitle }}</div>
      </div>

      <div class="flex items-center gap-2">
        <slot name="toolbar" />
      </div>
    </div>

    <!-- Sticky header -->
    <div class="bg-[var(--ngb-grid-header)] border-b border-ngb-border sticky top-0 z-10">
      <div
        class="grid items-center text-sm font-semibold text-ngb-text select-none"
        :style="{ gridTemplateColumns }"
      >
        <div v-if="showStatusColumn" class="px-0 py-2 flex items-center justify-center">
          <!--
            Status column header is intentionally blank by default.
            Some screens (e.g. Chart of Accounts) may use this as a global group expand/collapse toggle.
          -->
          <slot
            name="statusHeader"
            :has-groups="hasGroups"
            :all-groups-expanded="allGroupsExpanded"
            :toggle-all-groups="toggleAllGroups"
          />
        </div>

        <div
          v-for="col in visibleColumns"
          :key="col.key"
          class="relative px-3 py-2 whitespace-nowrap flex items-center gap-2 border-l border-ngb-border min-w-0 overflow-hidden"
          :style="stickyStyle(col)"
          draggable="true"
          @dragstart="onDragStart(col.key)"
          @dragover.prevent
          @drop="onDrop(col.key)"
        >
          <button
            v-if="col.sortable !== false"
            class="flex items-center gap-1 hover:text-ngb-blue ngb-focus rounded-[var(--ngb-radius)] px-1 -mx-1 min-w-0"
            @click="toggleSort(col.key, $event)"
            :title="sortHint(col.key)"
          >
            <span class="truncate">{{ col.title }}</span>
            <span v-if="sortIndex(col.key) >= 0" class="text-xs text-ngb-muted">
              {{ sortBadge(col.key) }}
            </span>
          </button>
          <span v-else>{{ col.title }}</span>

          <!-- resize handle -->
          <div
            class="absolute right-0 top-0 h-full w-2 cursor-col-resize"
            @pointerdown.prevent="startResize(col.key, $event)"
            title="Resize"
          />
        </div>
      </div>
    </div>

    <!-- viewport -->
    <div
      ref="viewport"
      :class="fillHeight ? 'flex-1 min-h-0 overflow-auto' : 'overflow-auto'"
      :style="fillHeight ? {} : { height: heightPx + 'px' }"
      tabindex="0"
      @scroll="onScroll"
      @keydown="onKeyDown"
    >
      <div class="relative" :style="{ height: totalHeight + 'px' }">
        <div class="absolute left-0 right-0" :style="{ transform: `translateY(${offsetTop}px)` }">
          <div
            v-for="(row, idx) in visibleRows"
            :key="row.key"
            class="border-b border-ngb-border"
          >
            <!-- group header -->
            <button
              v-if="row.type === 'group'"
              class="w-full text-left grid items-center text-sm bg-ngb-card hover:bg-[var(--ngb-row-hover)]"
              :style="{ gridTemplateColumns }"
              @click="toggleGroup(row.groupId)"
            >
              <div v-if="showStatusColumn" class="px-0 py-2 flex items-center justify-center">
                <span
                  v-if="showGroupToggleIcons"
                  class="inline-flex h-5 w-5 items-center justify-center text-ngb-muted"
                  aria-hidden="true"
                >
                  <NgbIcon :name="isGroupExpanded(row.groupId) ? 'minus' : 'plus'" :size="14" />
                </span>
              </div>
              <div class="px-3 py-2 flex items-center gap-2" :style="{ gridColumn: showStatusColumn ? '2 / -1' : '1 / -1' }">
                <span class="font-semibold text-ngb-text">
                  {{ row.label }}<template v-if="showGroupCounts"> ({{ row.count }})</template>
                </span>
                <span v-if="showTotals" class="ml-auto text-xs text-ngb-muted tabular-nums">
                  D {{ fmt(row.totalDebit) }} · C {{ fmt(row.totalCredit) }}
                </span>
              </div>
            </button>

            <!-- data row -->
            <div
              v-else
              class="grid text-sm"
              :class="rowClass(row)"
              :style="{ gridTemplateColumns }"
              @click="onRowClick(row.key, row.__index, $event)"
            >
              <div v-if="showStatusColumn" class="px-3 py-2 flex items-center justify-center">
                <NgbStatusIcon v-if="showRowStatusIcons" :status="inferRowStatus(row)" />
              </div>

              <div
                v-for="(col, colIdx) in visibleColumns"
                :key="col.key"
                class="px-3 py-2 min-w-0 overflow-hidden border-l border-ngb-border self-stretch"
                :class="cellClass(col, row[col.key])"
                :style="cellStickyStyle(col)"
              >
                <div class="flex h-full min-w-0 gap-2 items-center">
                  <template v-if="Array.isArray(row[col.key])">
                    <div class="min-w-0 py-0.5 self-center" :class="col.wrap ? 'whitespace-normal break-words' : ''">
                      <div
                        v-for="(line, lineIdx) in formatCellLines(col, row[col.key], row)"
                        :key="lineIdx"
                        :class="col.wrap ? 'leading-5 whitespace-normal break-words' : 'truncate leading-5'"
                      >{{ line }}</div>
                    </div>
                  </template>
                  <template v-else>
                    <span :class="col.wrap ? 'whitespace-normal break-words leading-5' : 'truncate'">{{ formatCell(col, row[col.key], row) }}</span>
                  </template>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <div v-if="showTotals" class="px-4 py-3 text-sm flex items-center justify-between bg-ngb-card">
      <div class="text-ngb-muted">Total</div>
      <div class="flex items-center gap-6 tabular-nums">
        <div><span class="text-ngb-muted">Debit</span> <span class="font-semibold">{{ fmt(totals.debit) }}</span></div>
        <div><span class="text-ngb-muted">Credit</span> <span class="font-semibold">{{ fmt(totals.credit) }}</span></div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import NgbStatusIcon from '../../primitives/NgbStatusIcon.vue';
import NgbIcon from '../../primitives/NgbIcon.vue';
import type { RegisterColumn, RegisterDataRow, RegisterSortSpec } from './registerTypes';
import { useRegisterColumnResize } from './useRegisterColumnResize';
import { useRegisterColumnState } from './useRegisterColumnState';
import { useRegisterRows } from './useRegisterRows';
import { useRegisterViewport } from './useRegisterViewport';

const props = withDefaults(defineProps<{
  title?: string;
  subtitle?: string;
  showPanel?: boolean;
  heightPx?: number;
  fillHeight?: boolean;
  columns: RegisterColumn[];
  rows: RegisterDataRow[];
  groupBy?: string[];
  showTotals?: boolean;
  defaultExpanded?: boolean;
  storageKey?: string; // persist column order + widths + pinned/visible for demo
  visibleColumnKeys?: string[];
  sortBy?: RegisterSortSpec[];
  selectedKeys?: string[];
  // If true, single-click activates the row (emits rowActivate).
  // Multi-select modifiers (Ctrl/Meta/Shift) keep selection behavior without activation.
  activateOnRowClick?: boolean;
  rowHeightPx?: number;
  showStatusColumn?: boolean;
  showGroupCounts?: boolean;
  showGroupToggleIcons?: boolean;
  showRowStatusIcons?: boolean;
}>(), {
  showPanel: true,
  heightPx: 460,
  fillHeight: false,
  showTotals: true,
  defaultExpanded: false,
  activateOnRowClick: false,
  rowHeightPx: 36,
  showStatusColumn: true,
  showGroupCounts: true,
  showGroupToggleIcons: false,
  showRowStatusIcons: true,
});

const emit = defineEmits<{
  (e: 'update:groupBy', v: string[]): void;
  (e: 'update:visibleColumnKeys', v: string[]): void;
  (e: 'update:sortBy', v: RegisterSortSpec[]): void;
  (e: 'update:selectedKeys', v: string[]): void;
  (e: 'rowActivate', key: string): void;
}>();

const title = computed(() => props.title ?? 'Register');
const subtitle = computed(() => props.subtitle ?? '');
const showPanel = computed(() => props.showPanel);
const heightPx = computed(() => props.heightPx);
const fillHeight = computed(() => props.fillHeight);
const showTotals = computed(() => props.showTotals);

const statusColWidth = computed(() => props.showStatusColumn ? 40 : 0); // px (icon-only status column)
const showStatusColumn = computed(() => props.showStatusColumn);
const showGroupCounts = computed(() => props.showGroupCounts);
const showGroupToggleIcons = computed(() => props.showGroupToggleIcons);
const showRowStatusIcons = computed(() => props.showRowStatusIcons);

const rowHeight = computed(() => props.rowHeightPx);
const viewport = ref<HTMLElement | null>(null);

function fmt(v: number) {
  if (!v) return '—';
  const n = Math.round(v * 100) / 100;
  return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

const sortBy = computed<RegisterSortSpec[]>(() => props.sortBy ?? []);
const groupBy = computed(() => props.groupBy ?? []);
const defaultExpanded = computed(() => props.defaultExpanded);

const {
  localOrder,
  localWidths,
  visibleColumns,
  gridTemplateColumns,
  colWidth,
  stickyStyle,
  cellStickyStyle,
} = useRegisterColumnState({
  columns: computed(() => props.columns),
  visibleColumnKeys: computed(() => props.visibleColumnKeys),
  storageKey: computed(() => props.storageKey),
  showStatusColumn,
  statusColWidth,
  emitVisibleColumnKeys: (value) => emit('update:visibleColumnKeys', value),
});

const { startResize } = useRegisterColumnResize({
  columns: computed(() => props.columns),
  localWidths,
  colWidth,
});

const {
  hasGroups,
  displayRows,
  dataRows,
  totals,
  allGroupsExpanded,
  isGroupExpanded,
  toggleGroup,
  toggleAllGroups,
  inferRowStatus,
} = useRegisterRows({
  rows: computed(() => props.rows),
  groupBy,
  sortBy,
  defaultExpanded,
});

const { onScroll, totalHeight, offsetTop, visibleRows } = useRegisterViewport({
  viewport,
  heightPx,
  rowHeight,
  displayRows,
});

function sortIndex(key: string) {
  return sortBy.value.findIndex(s => s.key === key);
}
function sortBadge(key: string) {
  const i = sortIndex(key);
  if (i < 0) return '';
  const dir = sortBy.value[i].dir === 'asc' ? '↑' : '↓';
  return `${i + 1}${dir}`;
}
function sortHint(key: string) {
  return 'Click: sort · Shift+Click: multi-sort';
}
function toggleSort(key: string, ev: MouseEvent) {
  const multi = ev.shiftKey;
  const current = [...sortBy.value];
  const idx = current.findIndex(s => s.key === key);
  if (idx < 0) {
    const next = multi ? [...current, { key, dir: 'asc' as const }] : [{ key, dir: 'asc' as const }];
    emit('update:sortBy', next);
    return;
  }
  const dir = current[idx].dir === 'asc' ? 'desc' : 'asc';
  current[idx] = { key, dir };
  emit('update:sortBy', multi ? current : [current[idx]]);
}

/* ---------------- Selection + keyboard ---------------- */
const selected = computed(() => new Set(props.selectedKeys ?? []));
const activeIndex = ref<number>(0);
const lastSelectedIndex = ref<number | null>(null);

function rowClass(row: RegisterDataRow) {
  const key = row.key;
  const isSel = selected.value.has(key);
  const isMarked = inferRowStatus(row) === 'marked';
  return [
    'hover:bg-[var(--ngb-row-hover)] cursor-pointer',
    isSel ? 'bg-[var(--ngb-row-selected)]' : 'bg-ngb-card',
    isMarked ? 'opacity-70' : '',
  ].join(' ');
}

function onRowClick(key: string, index: number, ev: MouseEvent) {
  // ignore group rows (they don't call this)
  const mult = ev.metaKey || ev.ctrlKey;
  const range = ev.shiftKey;

  const current = new Set(selected.value);

  if (range && lastSelectedIndex.value != null) {
    const a = Math.min(lastSelectedIndex.value, index);
    const b = Math.max(lastSelectedIndex.value, index);
    const rowKeys = dataRows.value.map((row) => row.key);
    for (let i = a; i <= b && i < rowKeys.length; i++) current.add(rowKeys[i]);
  } else if (mult) {
    if (current.has(key)) current.delete(key);
    else current.add(key);
    lastSelectedIndex.value = index;
  } else {
    current.clear();
    current.add(key);
    lastSelectedIndex.value = index;
  }

  activeIndex.value = index;
  emit('update:selectedKeys', Array.from(current));

  // Activation: open on single-click when enabled.
  if (props.activateOnRowClick && !mult && !range) {
    emit('rowActivate', key);
  }
}

function onKeyDown(ev: KeyboardEvent) {
  const rowKeys = dataRows.value;
  if (!rowKeys.length) return;

  const clamp = (v: number) => Math.max(0, Math.min(rowKeys.length - 1, v));

  if (ev.key === 'ArrowDown') {
    ev.preventDefault();
    activeIndex.value = clamp(activeIndex.value + 1);
    scrollToActive();
    return;
  }
  if (ev.key === 'ArrowUp') {
    ev.preventDefault();
    activeIndex.value = clamp(activeIndex.value - 1);
    scrollToActive();
    return;
  }
  if (ev.key === 'Enter') {
    ev.preventDefault();
    const key = rowKeys[activeIndex.value]?.key;
    if (key) emit('rowActivate', key);
    return;
  }
  if (ev.key === ' ' || ev.key === 'Spacebar') {
    ev.preventDefault();
    const key = rowKeys[activeIndex.value]?.key;
    if (!key) return;
    const current = new Set(selected.value);
    if (current.has(key)) current.delete(key);
    else current.add(key);
    emit('update:selectedKeys', Array.from(current));
  }
}

function scrollToActive() {
  const vp = viewport.value;
  if (!vp) return;
  const top = activeIndex.value * rowHeight.value;
  const bottom = top + rowHeight.value;
  if (top < vp.scrollTop) vp.scrollTop = top;
  else if (bottom > vp.scrollTop + vp.clientHeight) vp.scrollTop = bottom - vp.clientHeight;
}

/* ---------------- Cell formatting ---------------- */
function formatCell(col: RegisterColumn, v: unknown, row: RegisterDataRow) {
  if (col.format) return col.format(v, row);
  if (v == null || v === '') return '—';
  if (typeof v === 'number') {
    if (col.align === 'right') return fmt(v);
    return String(v);
  }
  return String(v);
}

function formatCellLines(col: RegisterColumn, v: unknown, row: RegisterDataRow): string[] {
  if (Array.isArray(v)) {
    const lines = v
      .map((item) => formatCell(col, item, row))
      .filter((item) => item && item !== '—');
    return lines.length > 0 ? lines : ['—'];
  }

  return [formatCell(col, v, row)];
}

function cellClass(col: RegisterColumn, v: unknown) {
  const align = col.align === 'right' ? 'text-right tabular-nums' : col.align === 'center' ? 'text-center' : '';
  const neg = typeof v === 'number' && v < 0 ? 'text-ngb-danger' : '';
  return [align, neg].filter(Boolean).join(' ');
}

/* ---------------- Column reorder (drag) ---------------- */
const dragKey = ref<string | null>(null);

function onDragStart(key: string) {
  dragKey.value = key;
}

function onDrop(targetKey: string) {
  if (!dragKey.value || dragKey.value === targetKey) return;
  const order = [...localOrder.value];
  const from = order.indexOf(dragKey.value);
  const to = order.indexOf(targetKey);
  if (from < 0 || to < 0) return;
  order.splice(from, 1);
  order.splice(to, 0, dragKey.value);
  localOrder.value = order;
  dragKey.value = null;
}
</script>
