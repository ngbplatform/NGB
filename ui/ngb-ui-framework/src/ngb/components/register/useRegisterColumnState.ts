import { computed, ref, watch, type CSSProperties, type ComputedRef, type Ref } from 'vue';
import { loadJson, saveJson } from '../../utils/storage';
import type { RegisterColumn } from './registerTypes';

type PersistedColumns = {
  order?: string[];
  widths?: Record<string, number>;
  visible?: string[];
};

type UseRegisterColumnStateArgs = {
  columns: ComputedRef<RegisterColumn[]>;
  visibleColumnKeys: ComputedRef<string[] | undefined>;
  storageKey: ComputedRef<string | undefined>;
  showStatusColumn: ComputedRef<boolean>;
  statusColWidth: ComputedRef<number>;
  emitVisibleColumnKeys: (value: string[]) => void;
};

type RegisterColumnState = {
  localOrder: Ref<string[]>;
  localWidths: Ref<Record<string, number>>;
  visibleColumns: ComputedRef<RegisterColumn[]>;
  gridTemplateColumns: ComputedRef<string>;
  colWidth: (column: RegisterColumn) => number;
  stickyStyle: (column: RegisterColumn) => CSSProperties;
  cellStickyStyle: (column: RegisterColumn) => CSSProperties;
  setVisible: (keys: string[]) => void;
};

function isAccidentalVisible(value: string[] | null) {
  return !!value && value.length === 1 && value[0] === 'display';
}

export function useRegisterColumnState(args: UseRegisterColumnStateArgs): RegisterColumnState {
  const localOrder = ref<string[]>(args.columns.value.map((column) => column.key));
  const localWidths = ref<Record<string, number>>({});
  const localVisible = ref<string[] | null>(args.visibleColumnKeys.value ? [...args.visibleColumnKeys.value] : null);
  const persistEnabled = ref(false);
  const derivedFromPartialColumns = ref(args.columns.value.length <= 1);

  function ensureStateAfterColumnsReady() {
    const columns = args.columns.value;
    if (columns.length <= 1) return;

    const keys = columns.map((column) => column.key);
    if (derivedFromPartialColumns.value) {
      if (localOrder.value.length <= 1 && localOrder.value[0] === 'display') {
        localOrder.value = [...keys];
      }
      if (isAccidentalVisible(localVisible.value)) {
        localVisible.value = null;
      }
      derivedFromPartialColumns.value = false;
    }

    if (isAccidentalVisible(localVisible.value) && keys.length > 1) {
      localVisible.value = null;
    }

    if (!persistEnabled.value) persistEnabled.value = true;
  }

  function resetLocalStateFromColumns() {
    const columns = args.columns.value;
    localOrder.value = columns.map((column) => column.key);
    localWidths.value = {};
    localVisible.value = args.visibleColumnKeys.value ? [...args.visibleColumnKeys.value] : null;
    derivedFromPartialColumns.value = columns.length <= 1;
  }

  function hydrateFromStorage() {
    const storageKey = args.storageKey.value;
    if (!storageKey) return;

    const saved = loadJson<PersistedColumns | string[] | null>(storageKey, null);
    if (!saved) return;

    if (Array.isArray(saved)) {
      localVisible.value = [...saved];
      return;
    }

    if (saved.order?.length) localOrder.value = saved.order;
    if (saved.widths) localWidths.value = saved.widths;
    if (Array.isArray(saved.visible)) localVisible.value = [...saved.visible];
  }

  watch(
    args.storageKey,
    (next, prev) => {
      if (!next) return;
      if (prev && next === prev) return;
      resetLocalStateFromColumns();
      hydrateFromStorage();
      ensureStateAfterColumnsReady();
    },
    { immediate: true },
  );

  watch(
    () => args.columns.value.map((column) => column.key).join('|'),
    () => {
      ensureStateAfterColumnsReady();
    },
    { immediate: true },
  );

  watch(
    [localOrder, localWidths, localVisible],
    () => {
      const storageKey = args.storageKey.value;
      if (!storageKey || !persistEnabled.value) return;

      saveJson<PersistedColumns>(storageKey, {
        order: localOrder.value,
        widths: localWidths.value,
        visible: localVisible.value ?? undefined,
      });
    },
    { deep: true },
  );

  watch(
    args.visibleColumnKeys,
    (value) => {
      if (!value) return;
      localVisible.value = [...value];
    },
    { deep: true },
  );

  const orderedColumns = computed(() => {
    const byKey = new Map(args.columns.value.map((column) => [column.key, column] as const));
    const result: RegisterColumn[] = [];

    for (const key of localOrder.value) {
      const column = byKey.get(key);
      if (column) result.push(column);
    }

    for (const column of args.columns.value) {
      if (!result.find((entry) => entry.key === column.key)) result.push(column);
    }

    return result;
  });

  const visibleColumns = computed(() => {
    const keys = localVisible.value ?? args.visibleColumnKeys.value ?? args.columns.value.map((column) => column.key);
    const visibleSet = new Set(keys);
    return orderedColumns.value.filter((column) => visibleSet.has(column.key));
  });

  function setVisible(keys: string[]) {
    localVisible.value = keys;
    args.emitVisibleColumnKeys(keys);
  }

  function colWidth(column: RegisterColumn) {
    const width = localWidths.value[column.key] ?? column.width ?? 140;
    const minWidth = column.minWidth ?? 80;
    return Math.max(minWidth, width);
  }

  const gridTemplateColumns = computed(() => {
    const columns = [
      ...(args.showStatusColumn.value ? [`${args.statusColWidth.value}px`] : []),
      ...visibleColumns.value.map((column) => `${colWidth(column)}px`),
    ];
    return columns.join(' ');
  });

  const stickyLeftOffsets = computed(() => {
    let left = args.showStatusColumn.value ? args.statusColWidth.value : 0;
    const offsets: Record<string, number> = {};

    for (const column of visibleColumns.value) {
      if (column.pinned === 'left') {
        offsets[column.key] = left;
        left += colWidth(column);
      }
    }

    return offsets;
  });

  function stickyStyle(column: RegisterColumn): CSSProperties {
    if (column.pinned !== 'left') return {};
    const left = stickyLeftOffsets.value[column.key] ?? (args.showStatusColumn.value ? args.statusColWidth.value : 0);
    return {
      position: 'sticky',
      left: `${left}px`,
      zIndex: 2,
      background: 'var(--ngb-grid-header)',
    };
  }

  function cellStickyStyle(column: RegisterColumn): CSSProperties {
    if (column.pinned !== 'left') return {};
    const left = stickyLeftOffsets.value[column.key] ?? (args.showStatusColumn.value ? args.statusColWidth.value : 0);
    return {
      position: 'sticky',
      left: `${left}px`,
      zIndex: 1,
      background: 'inherit',
    };
  }

  return {
    localOrder,
    localWidths,
    visibleColumns,
    gridTemplateColumns,
    colWidth,
    stickyStyle,
    cellStickyStyle,
    setVisible,
  };
}
