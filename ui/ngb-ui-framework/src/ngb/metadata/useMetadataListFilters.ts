import { computed, onBeforeUnmount, ref, watch, type ComputedRef } from 'vue';
import type { RouteLocationNormalizedLoaded, Router } from 'vue-router';

import { isGuidString } from '../utils/guid';
import { stableStringify } from '../utils/stableValue';
import { normalizeSingleQueryValue, setCleanRouteQuery } from '../router/queryParams';
import {
  buildFilterOptionLabelsByKey,
  hydrateResolvedLookupItems,
  joinFilterValues,
  labelForResolvedLookup,
  optionLabelForFilter,
  searchResolvedLookupItems,
  splitFilterValues,
  summarizeFilterValues,
} from './filtering';
import type {
  FilterFieldState,
  LookupHint,
  LookupItem,
  LookupStoreApi,
  ListFilterField,
} from './types';

export type MetadataListFilterBadge = {
  key: string;
  text: string;
};

export type UseMetadataListFiltersArgs<
  TField extends ListFilterField = ListFilterField,
  TItem extends LookupItem = LookupItem,
> = {
  route: RouteLocationNormalizedLoaded;
  router: Router;
  entityTypeCode: ComputedRef<string>;
  filters: ComputedRef<readonly TField[]>;
  lookupStore: LookupStoreApi<TItem>;
  resolveLookupHint: (args: {
    entityTypeCode: string;
    field: TField;
  }) => LookupHint | null;
  commitDelayMs?: number;
};

function buildEmptyFilterState<TField extends Pick<ListFilterField, 'key'>, TItem extends LookupItem>(
  filters: readonly TField[],
): Record<string, FilterFieldState<TItem>> {
  return Object.fromEntries(filters.map((field) => [field.key, { raw: '', items: [] }]));
}

export function useMetadataListFilters<
  TField extends ListFilterField = ListFilterField,
  TItem extends LookupItem = LookupItem,
>(
  args: UseMetadataListFiltersArgs<TField, TItem>,
) {
  const filterDraft = ref<Record<string, FilterFieldState<TItem>>>({});
  const lookupItemsByFilterKey = ref<Record<string, TItem[]>>({});
  const lookupSearchSeqByFilterKey = ref<Record<string, number>>({});
  const filterDraftSyncToken = ref(0);
  const filterCommitTimers = new Map<string, ReturnType<typeof setTimeout>>();
  const commitDelayMs = args.commitDelayMs ?? 280;

  function listFilterStateFor(key: string): FilterFieldState<TItem> {
    return filterDraft.value[key] ?? { raw: '', items: [] };
  }

  function setListFilterState(key: string, next: FilterFieldState<TItem>) {
    filterDraft.value = {
      ...filterDraft.value,
      [key]: next,
    };
  }

  function clearPendingFilterCommit(key: string) {
    const timer = filterCommitTimers.get(key);
    if (!timer) return;
    clearTimeout(timer);
    filterCommitTimers.delete(key);
  }

  function clearAllPendingFilterCommits() {
    for (const timer of filterCommitTimers.values()) clearTimeout(timer);
    filterCommitTimers.clear();
  }

  function resolveListFilterLookupHint(field: TField): LookupHint | null {
    return args.resolveLookupHint({
      entityTypeCode: args.entityTypeCode.value,
      field,
    });
  }

  async function searchListFilterLookupItems(field: TField, query: string): Promise<TItem[]> {
    const hint = resolveListFilterLookupHint(field);
    if (!hint) return [];
    return await searchResolvedLookupItems(args.lookupStore, hint, query);
  }

  async function syncFilterDraftFromRoute() {
    const filters = args.filters.value;
    const token = ++filterDraftSyncToken.value;
    const next = buildEmptyFilterState<TField, TItem>(filters);

    for (const field of filters) {
      const raw = normalizeSingleQueryValue(args.route.query[field.key]);
      next[field.key] = { raw, items: [] };
    }

    filterDraft.value = next;

    for (const field of filters) {
      if (!field.lookup) continue;

      const raw = normalizeSingleQueryValue(args.route.query[field.key]);
      const ids = (field.isMulti ? splitFilterValues(raw) : [raw]).filter(isGuidString);
      if (ids.length === 0) continue;

      const hint = resolveListFilterLookupHint(field);
      next[field.key] = {
        raw,
        items: hint ? await hydrateResolvedLookupItems(args.lookupStore, hint, ids) : [],
      };

      if (token !== filterDraftSyncToken.value) return;
    }

    if (token === filterDraftSyncToken.value) {
      filterDraft.value = next;
    }
  }

  async function replaceRouteQuery(mutator: (query: Record<string, unknown>) => void) {
    const nextQuery: Record<string, unknown> = { ...args.route.query };
    mutator(nextQuery);
    await setCleanRouteQuery(args.route, args.router, nextQuery, 'replace');
  }

  async function applyFilterValue(key: string, value: string) {
    clearPendingFilterCommit(key);
    await replaceRouteQuery((query) => {
      query[key] = value || undefined;
      query.offset = 0;
    });
  }

  function scheduleFilterCommit(key: string, value: string) {
    clearPendingFilterCommit(key);
    const timer = setTimeout(() => {
      filterCommitTimers.delete(key);
      void applyFilterValue(key, value.trim());
    }, commitDelayMs);
    filterCommitTimers.set(key, timer);
  }

  function isImmediateFilter(field: TField): boolean {
    return !!field.lookup || (field.options?.length ?? 0) > 0;
  }

  async function handleLookupQuery(payload: { key: string; query: string }) {
    const field = args.filters.value.find((entry) => entry.key === payload.key);
    if (!field?.lookup) return;

    const nextSeq = (lookupSearchSeqByFilterKey.value[payload.key] ?? 0) + 1;
    lookupSearchSeqByFilterKey.value = {
      ...lookupSearchSeqByFilterKey.value,
      [payload.key]: nextSeq,
    };

    const items = payload.query.trim()
      ? await searchListFilterLookupItems(field, payload.query)
      : [];

    if ((lookupSearchSeqByFilterKey.value[payload.key] ?? 0) !== nextSeq) return;

    lookupItemsByFilterKey.value = {
      ...lookupItemsByFilterKey.value,
      [payload.key]: items,
    };
  }

  function handleItemsUpdate(payload: { key: string; items: TItem[] }) {
    const raw = joinFilterValues(payload.items.map((item) => item.id));
    setListFilterState(payload.key, {
      raw,
      items: payload.items.map((item) => ({ ...item })),
    });

    void applyFilterValue(payload.key, raw);
  }

  function handleValueUpdate(payload: { key: string; value: string }) {
    const field = args.filters.value.find((entry) => entry.key === payload.key);
    if (!field) return;

    setListFilterState(payload.key, {
      raw: payload.value,
      items: [],
    });

    if (isImmediateFilter(field)) {
      void applyFilterValue(payload.key, payload.value.trim());
      return;
    }

    scheduleFilterCommit(payload.key, payload.value);
  }

  async function undo() {
    clearAllPendingFilterCommits();
    filterDraft.value = buildEmptyFilterState<TField, TItem>(args.filters.value);

    await replaceRouteQuery((query) => {
      for (const field of args.filters.value) delete query[field.key];
      query.offset = 0;
    });
  }

  const optionLabelsByColumnKey = computed(() => buildFilterOptionLabelsByKey(args.filters.value));

  const activeFilterBadges = computed<MetadataListFilterBadge[]>(() =>
    args.filters.value
      .map((field) => {
        const appliedRaw = normalizeSingleQueryValue(args.route.query[field.key]);
        if (!appliedRaw) return null;

        const state = listFilterStateFor(field.key);
        const selectedLabels = state.raw.trim() === appliedRaw
          ? new Map(
              state.items
                .map((item) => [
                  String(item.id ?? '').trim().toLowerCase(),
                  String(item.label ?? item.id ?? '').trim(),
                ] satisfies [string, string])
                .filter((entry) => entry[0].length > 0 && entry[1].length > 0),
            )
          : new Map<string, string>();
        const lookupHint = field.lookup ? resolveListFilterLookupHint(field) : null;
        const displayValues = (field.isMulti ? splitFilterValues(appliedRaw) : [appliedRaw])
          .map((value) => {
            const normalized = value.trim();
            if (!normalized) return '';

            const fromDraft = selectedLabels.get(normalized.toLowerCase());
            if (fromDraft) return fromDraft;
            if (lookupHint) return labelForResolvedLookup(args.lookupStore, lookupHint, normalized);
            return optionLabelForFilter(field, normalized) || normalized;
          })
          .filter((value) => value.length > 0);
        const summary = summarizeFilterValues(displayValues);
        if (!summary) return null;

        return {
          key: field.key,
          text: `${field.label}: ${summary}`,
        } satisfies MetadataListFilterBadge;
      })
      .filter((entry): entry is MetadataListFilterBadge => !!entry),
  );

  const hasActiveFilters = computed(() => activeFilterBadges.value.length > 0);
  const canUndoFilters = computed(() =>
    hasActiveFilters.value
    || Object.values(filterDraft.value).some((state) => state.items.length > 0 || state.raw.trim().length > 0),
  );

  watch(
    () => stableStringify(
      args.filters.value
        .map((field) => [field.key, normalizeSingleQueryValue(args.route.query[field.key])])
        .sort(([left], [right]) => String(left).localeCompare(String(right))),
    ),
    () => {
      void syncFilterDraftFromRoute();
    },
    { immediate: true },
  );

  watch(
    () => args.entityTypeCode.value,
    () => {
      filterDraft.value = {};
      lookupItemsByFilterKey.value = {};
      lookupSearchSeqByFilterKey.value = {};
      clearAllPendingFilterCommits();
    },
  );

  onBeforeUnmount(() => {
    clearAllPendingFilterCommits();
  });

  return {
    filterDraft,
    lookupItemsByFilterKey,
    optionLabelsByColumnKey,
    activeFilterBadges,
    hasActiveFilters,
    canUndoFilters,
    handleLookupQuery,
    handleItemsUpdate,
    handleValueUpdate,
    undo,
  };
}
