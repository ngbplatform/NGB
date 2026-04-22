import { computed, ref, watch, type ComputedRef } from 'vue';
import type { RouteLocationNormalizedLoaded } from 'vue-router';

import { prefetchLookupsForPage } from '../lookup/prefetch';
import { normalizeSingleQueryValue } from '../router/queryParams';
import { isGuidString } from '../utils/guid';
import { stableStringify } from '../utils/stableValue';
import { buildFilterOptionLabelsByKey, labelForResolvedLookup } from './filtering';
import { isReferenceValue } from './entityModel';
import { buildMetadataRegisterColumns, buildMetadataRegisterRows } from './register';
import type {
  ColumnMetadata,
  JsonValue,
  ListFilterField,
  LookupHint,
  LookupSource,
  LookupStoreApi,
  RecordFields,
} from './types';

export type MetadataRegisterPageItem = {
  id: string;
  payload?: {
    fields?: RecordFields | null;
  } | null;
  isDeleted?: boolean;
  isMarkedForDeletion?: boolean;
  status?: unknown;
};

export type MetadataRegisterPageResponse<TItem extends MetadataRegisterPageItem = MetadataRegisterPageItem> = {
  items: readonly TItem[];
  total?: number | null;
};

export type MetadataRegisterPageMetadata<TField extends ListFilterField = ListFilterField> = {
  displayName: string;
  list?: {
    columns: readonly ColumnMetadata[];
    filters?: readonly TField[] | null;
  } | null;
};

export type MetadataRegisterCellArgs<TItem extends MetadataRegisterPageItem = MetadataRegisterPageItem> = {
  column: ColumnMetadata;
  item: TItem;
  rawValue: JsonValue | undefined;
  defaultValue: unknown;
  entityTypeCode: string;
};

export type UseMetadataPageReloadKeyArgs = {
  route: RouteLocationNormalizedLoaded;
  entityTypeCode: ComputedRef<string>;
  ignoreQueryKey?: (key: string) => boolean;
};

export type UseMetadataRegisterPageDataArgs<
  TField extends ListFilterField = ListFilterField,
  TMeta extends MetadataRegisterPageMetadata<TField> = MetadataRegisterPageMetadata<TField>,
  TItem extends MetadataRegisterPageItem = MetadataRegisterPageItem,
  TPage extends MetadataRegisterPageResponse<TItem> = MetadataRegisterPageResponse<TItem>,
> = {
  route: RouteLocationNormalizedLoaded;
  entityTypeCode: ComputedRef<string>;
  reloadKey: ComputedRef<string>;
  loadMetadata: (entityTypeCode: string) => Promise<TMeta>;
  loadPage: (args: {
    entityTypeCode: string;
    metadata: TMeta;
  }) => Promise<TPage>;
  lookupStore?: LookupStoreApi;
  resolveLookupHint?: (args: {
    entityTypeCode: string;
    fieldKey: string;
    lookup?: LookupSource | null;
  }) => LookupHint | null;
  mapFieldValue?: (args: MetadataRegisterCellArgs<TItem>) => unknown;
  formatError?: (cause: unknown) => string;
};

function defaultErrorMessage(cause: unknown): string {
  return cause instanceof Error ? cause.message : String(cause);
}

export function useMetadataPageReloadKey(args: UseMetadataPageReloadKeyArgs) {
  return computed(() =>
    stableStringify({
      path: args.route.path,
      entityTypeCode: args.entityTypeCode.value,
      query: Object.entries(args.route.query)
        .filter(([key]) => !args.ignoreQueryKey?.(key))
        .map(([key, value]) => [key, normalizeSingleQueryValue(value)] as const)
        .sort(([left], [right]) => left.localeCompare(right)),
    }),
  );
}

export function useMetadataRegisterPageData<
  TField extends ListFilterField = ListFilterField,
  TMeta extends MetadataRegisterPageMetadata<TField> = MetadataRegisterPageMetadata<TField>,
  TItem extends MetadataRegisterPageItem = MetadataRegisterPageItem,
  TPage extends MetadataRegisterPageResponse<TItem> = MetadataRegisterPageResponse<TItem>,
>(
  args: UseMetadataRegisterPageDataArgs<TField, TMeta, TItem, TPage>,
) {
  const loading = ref(false);
  const error = ref<string | null>(null);
  const metadata = ref<TMeta | null>(null);
  const page = ref<TPage | null>(null);
  const loadSeq = ref(0);

  const listFilters = computed<readonly TField[]>(() => metadata.value?.list?.filters ?? []);
  const hasListFilters = computed(() => listFilters.value.length > 0);
  const optionLabelsByColumnKey = computed(() => ({
    ...buildFilterOptionLabelsByKey(metadata.value?.list?.columns ?? []),
    ...buildFilterOptionLabelsByKey(listFilters.value),
  }));

  function resolveDefaultFieldValue(column: ColumnMetadata, rawValue: JsonValue | undefined): unknown {
    if (isReferenceValue(rawValue)) return rawValue;
    if (!args.lookupStore || !args.resolveLookupHint || !isGuidString(rawValue)) return rawValue;

    const hint = args.resolveLookupHint({
      entityTypeCode: args.entityTypeCode.value,
      fieldKey: column.key,
      lookup: column.lookup,
    });
    if (!hint) return rawValue;

    return {
      id: rawValue,
      display: labelForResolvedLookup(args.lookupStore, hint, rawValue),
    };
  }

  const columns = computed(() =>
    buildMetadataRegisterColumns({
      columns: metadata.value?.list?.columns ?? [],
      optionLabelsByColumnKey: optionLabelsByColumnKey.value,
    }),
  );

  const rows = computed(() =>
    buildMetadataRegisterRows({
      items: page.value?.items ?? [],
      columns: metadata.value?.list?.columns ?? [],
      mapFieldValue: (column, rawValue, item) => {
        const defaultValue = resolveDefaultFieldValue(column, rawValue);
        return args.mapFieldValue?.({
          column,
          item,
          rawValue,
          defaultValue,
          entityTypeCode: args.entityTypeCode.value,
        }) ?? defaultValue;
      },
    }),
  );

  async function prefetchReferenceLabels(): Promise<void> {
    if (!args.lookupStore || !args.resolveLookupHint) return;

    const entityTypeCode = String(args.entityTypeCode.value ?? '').trim();
    const columns = metadata.value?.list?.columns ?? [];
    const items = page.value?.items ?? [];
    if (!entityTypeCode || columns.length === 0 || items.length === 0) return;

    await prefetchLookupsForPage({
      entityTypeCode,
      columns,
      items,
      lookupStore: args.lookupStore,
      resolveLookupHint: (nextEntityTypeCode, fieldKey, lookup) =>
        args.resolveLookupHint?.({
          entityTypeCode: nextEntityTypeCode,
          fieldKey,
          lookup,
        }) ?? null,
    });
  }

  async function load(): Promise<boolean> {
    const entityTypeCode = String(args.entityTypeCode.value ?? '').trim();
    const seq = ++loadSeq.value;

    if (!entityTypeCode) {
      if (seq === loadSeq.value) {
        loading.value = false;
        error.value = null;
        metadata.value = null;
        page.value = null;
      }
      return false;
    }

    loading.value = true;
    error.value = null;

    try {
      const nextMetadata = await args.loadMetadata(entityTypeCode);
      if (seq !== loadSeq.value) return false;
      metadata.value = nextMetadata;

      const nextPage = await args.loadPage({
        entityTypeCode,
        metadata: nextMetadata,
      });
      if (seq !== loadSeq.value) return false;
      page.value = nextPage;
      return true;
    } catch (cause) {
      if (seq !== loadSeq.value) return false;
      error.value = args.formatError?.(cause) ?? defaultErrorMessage(cause);
      return false;
    } finally {
      if (seq === loadSeq.value) loading.value = false;
    }
  }

  watch(args.reloadKey, () => {
    void load();
  }, { immediate: true });

  watch(
    () => [args.entityTypeCode.value, metadata.value?.list?.columns, page.value?.items],
    () => {
      void prefetchReferenceLabels();
    },
    { immediate: true, deep: true },
  );

  return {
    loading,
    error,
    metadata,
    page,
    listFilters,
    hasListFilters,
    optionLabelsByColumnKey,
    columns,
    rows,
    load,
    prefetchReferenceLabels,
  };
}
