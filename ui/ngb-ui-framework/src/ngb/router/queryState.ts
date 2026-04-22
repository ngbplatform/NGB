import { computed, ref, watch, type WatchSource } from 'vue'
import type { RouteLocationNormalizedLoaded, RouteLocationRaw, Router } from 'vue-router'

import type { UiLookupItem } from '../lookup/store'
import { isNonEmptyGuid } from '../utils/guid'
import {
  firstQueryValue,
  normalizeAllowedQueryValue,
  normalizeBooleanQueryFlag,
  replaceCleanRouteQuery,
  type QueryPatch,
} from './queryParams'

export type UseRouteQueryMigrationArgs<TValues extends readonly unknown[]> = {
  route: RouteLocationNormalizedLoaded
  router: Router
  sources: WatchSource<TValues>
  migrate: (values: TValues) => QueryPatch | null
}

export function useRouteQueryMigration<TValues extends readonly unknown[]>(
  args: UseRouteQueryMigrationArgs<TValues>,
) {
  watch(
    args.sources,
    (values) => {
      const patch = args.migrate(values)
      if (!patch) return
      void replaceCleanRouteQuery(args.route, args.router, patch)
    },
    { immediate: true },
  )
}

export function useGuidQueryParam(
  route: RouteLocationNormalizedLoaded,
  key: string,
) {
  return computed(() => {
    const value = firstQueryValue(route.query[key])
    return isNonEmptyGuid(value) ? value : null
  })
}

export function useBooleanQueryFlag(
  route: RouteLocationNormalizedLoaded,
  key: string,
) {
  return computed(() => normalizeBooleanQueryFlag(route.query[key]))
}

export function useAllowedQueryValue<TValue extends string>(
  route: RouteLocationNormalizedLoaded,
  key: string,
  allowedValues: readonly TValue[],
) {
  return computed<TValue | null>(() => normalizeAllowedQueryValue(route.query[key], allowedValues))
}

export type UseRouteLookupSelectionArgs<TItem extends UiLookupItem = UiLookupItem> = {
  route: RouteLocationNormalizedLoaded
  router: Router
  queryKey: string
  lookupById: (id: string) => Promise<string | null | undefined>
  search: (query: string) => Promise<TItem[]>
  openTarget: (value: TItem | null) => Promise<RouteLocationRaw | null>
}

export function useRouteLookupSelection<TItem extends UiLookupItem = UiLookupItem>(
  args: UseRouteLookupSelectionArgs<TItem>,
) {
  const selected = ref<TItem | null>(null)
  const items = ref<TItem[]>([])
  const routeId = useGuidQueryParam(args.route, args.queryKey)

  async function hydrateSelected(): Promise<void> {
    const id = routeId.value
    if (!id) {
      selected.value = null
      return
    }

    try {
      const label = await args.lookupById(id)
      selected.value = { id, label: label ?? id } as TItem
    } catch {
      selected.value = { id, label: id } as TItem
    }
  }

  async function onQuery(queryText: string): Promise<void> {
    const query = queryText.trim()
    if (!query) {
      items.value = []
      return
    }

    items.value = await args.search(query)
  }

  function onSelect(value: TItem | null): void {
    selected.value = value
    void replaceCleanRouteQuery(args.route, args.router, { [args.queryKey]: value?.id ?? null })
  }

  async function openSelected(): Promise<void> {
    const target = await args.openTarget(selected.value)
    if (!target) return
    await args.router.push(target)
  }

  return {
    selected,
    items,
    routeId,
    hydrateSelected,
    onQuery,
    onSelect,
    openSelected,
  }
}
