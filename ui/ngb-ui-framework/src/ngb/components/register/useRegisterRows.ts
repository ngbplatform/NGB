import { computed, ref, watch, type ComputedRef } from 'vue';
import type { DisplayRow, RegisterDataRow, RegisterSortSpec, RowStatus } from './registerTypes';
import { isDisplayDataRow } from './registerTypes';

type UseRegisterRowsArgs = {
  rows: ComputedRef<RegisterDataRow[]>;
  groupBy: ComputedRef<string[]>;
  sortBy: ComputedRef<RegisterSortSpec[]>;
  defaultExpanded: ComputedRef<boolean>;
};

function compareRows(left: RegisterDataRow, right: RegisterDataRow, sortBy: RegisterSortSpec[]): number {
  for (const sort of sortBy) {
    const leftValue = left[sort.key];
    const rightValue = right[sort.key];

    if (leftValue === rightValue) continue;
    if (leftValue == null) return sort.dir === 'asc' ? -1 : 1;
    if (rightValue == null) return sort.dir === 'asc' ? 1 : -1;

    const result = leftValue > rightValue ? 1 : -1;
    return sort.dir === 'asc' ? result : -result;
  }

  return 0;
}

function groupKeyPart(row: RegisterDataRow, key: string) {
  const value = row[key];
  return value == null || value === '' ? '—' : String(value);
}

type RegisterGroupNode = {
  groupId: string;
  label: string;
  count: number;
  totalDebit: number;
  totalCredit: number;
  children: RegisterGroupNode[];
  rows: RegisterDataRow[];
};

function buildGroupTree(
  rows: RegisterDataRow[],
  keys: string[],
  keyIndex = 0,
  path: string[] = [],
): RegisterGroupNode[] {
  const key = keys[keyIndex];
  if (!key) return [];
  const buckets = new Map<string, RegisterDataRow[]>();
  for (const row of rows) {
    const label = groupKeyPart(row, key);
    const bucket = buckets.get(label);
    if (bucket) bucket.push(row);
    else buckets.set(label, [row]);
  }

  const output: RegisterGroupNode[] = [];
  for (const [label, bucket] of buckets.entries()) {
    const groupId = [...path, `${key}:${label}`].join('|');
    let totalDebit = 0;
    let totalCredit = 0;
    for (const row of bucket) {
      totalDebit += Number(row.debit ?? 0);
      totalCredit += Number(row.credit ?? 0);
    }
    output.push({
      groupId,
      label,
      count: bucket.length,
      totalDebit,
      totalCredit,
      children: keyIndex + 1 < keys.length
        ? buildGroupTree(bucket, keys, keyIndex + 1, [...path, `${key}:${label}`])
        : [],
      rows: keyIndex + 1 < keys.length ? [] : bucket,
    });
  }

  return output;
}

function collectGroupIds(nodes: RegisterGroupNode[], output: string[] = []): string[] {
  for (const node of nodes) {
    output.push(node.groupId);
    collectGroupIds(node.children, output);
  }
  return output;
}

function flattenGroups(nodes: RegisterGroupNode[], expandedGroups: Set<string>, output: DisplayRow[] = []): DisplayRow[] {
  for (const node of nodes) {
    output.push({
      type: 'group',
      key: `g:${node.groupId}`,
      groupId: node.groupId,
      label: node.label,
      count: node.count,
      totalDebit: node.totalDebit,
      totalCredit: node.totalCredit,
    });
    if (!expandedGroups.has(node.groupId)) continue;
    if (node.children.length > 0) flattenGroups(node.children, expandedGroups, output);
    else output.push(...node.rows.map((row) => ({ ...row, type: 'row' as const, __index: 0 })));
  }
  return output;
}

export function inferRegisterRowStatus(row: RegisterDataRow): RowStatus {
  const explicit = row.__status;
  if (explicit === 'active' || explicit === 'saved' || explicit === 'posted' || explicit === 'marked') return explicit;

  if (row.isMarkedForDeletion === true || row.isDeleted === true) return 'marked';

  const status = row.status;
  if (typeof status === 'number') {
    if (status === 2) return 'posted';
    if (status === 3) return 'marked';
    return 'saved';
  }

  if (typeof row.isActive === 'boolean') return row.isActive ? 'active' : 'saved';

  return 'active';
}

export function useRegisterRows(args: UseRegisterRowsArgs) {
  const hasGroups = computed(() => args.groupBy.value.length > 0);
  const expandedGroups = ref<Set<string>>(new Set());

  const sortedRows = computed(() => {
    const rows = args.rows.value.map((row) => ({ ...row }));
    if (!args.sortBy.value.length) return rows;
    return rows.sort((left, right) => compareRows(left, right, args.sortBy.value));
  });

  const groupTree = computed(() => {
    if (!hasGroups.value) return [];
    return buildGroupTree(sortedRows.value, args.groupBy.value);
  });

  const allGroupIds = computed(() => {
    if (!hasGroups.value) return [];
    return collectGroupIds(groupTree.value);
  });

  const allGroupsExpanded = computed(() => {
    const ids = allGroupIds.value;
    if (!ids.length) return false;
    for (const id of ids) {
      if (!expandedGroups.value.has(id)) return false;
    }
    return true;
  });

  function isGroupExpanded(id: string) {
    return expandedGroups.value.has(id);
  }

  function toggleGroup(id: string) {
    const next = new Set(expandedGroups.value);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    expandedGroups.value = next;
  }

  function toggleAllGroups() {
    if (!hasGroups.value) return;
    const ids = allGroupIds.value;
    if (!ids.length) return;
    expandedGroups.value = allGroupsExpanded.value ? new Set() : new Set(ids);
  }

  watch(
    () => [args.defaultExpanded.value, allGroupIds.value.join('|')],
    ([isDefaultExpanded]) => {
      if (!isDefaultExpanded) return;
      const ids = allGroupIds.value;
      if (!ids.length) return;
      expandedGroups.value = new Set(ids);
    },
    { immediate: true },
  );

  const displayRows = computed<DisplayRow[]>(() => {
    if (!args.groupBy.value.length) {
      return sortedRows.value.map((row, index) => ({ ...row, type: 'row', __index: index }));
    }

    const grouped = flattenGroups(groupTree.value, expandedGroups.value);
    let index = 0;

    return grouped.map((row) => {
      if (isDisplayDataRow(row)) return { ...row, __index: index++ };
      return row;
    });
  });

  const totals = computed(() => {
    return args.rows.value.reduce(
      (accumulator, row) => {
        accumulator.debit += Number(row.debit ?? 0);
        accumulator.credit += Number(row.credit ?? 0);
        return accumulator;
      },
      { debit: 0, credit: 0 },
    );
  });

  const dataRows = computed(() => displayRows.value.filter(isDisplayDataRow));

  return {
    hasGroups,
    displayRows,
    dataRows,
    totals,
    allGroupIds,
    allGroupsExpanded,
    isGroupExpanded,
    toggleGroup,
    toggleAllGroups,
    inferRowStatus: inferRegisterRowStatus,
  };
}
