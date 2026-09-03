import { computed, getCurrentScope, onScopeDispose, ref, watch, type WatchSource } from 'vue'
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
  lookupById: (id: string, options?: { signal?: AbortSignal }) => Promise<string | null | undefined>
  search: (query: string, options?: { signal?: AbortSignal }) => Promise<TItem[]>
  openTarget: (value: TItem | null) => Promise<RouteLocationRaw | null>
}

export function useRouteLookupSelection<TItem extends UiLookupItem = UiLookupItem>(
  args: UseRouteLookupSelectionArgs<TItem>,
) {
  const selected = ref<TItem | null>(null)
  const items = ref<TItem[]>([])
  const routeId = useGuidQueryParam(args.route, args.queryKey)
  let hydrateSequence = 0
  let hydrateController: AbortController | null = null
  let searchSequence = 0
  let searchController: AbortController | null = null

  async function hydrateSelected(): Promise<void> {
    const sequence = ++hydrateSequence
    hydrateController?.abort()
    const id = routeId.value
    if (!id) {
      selected.value = null
      return
    }

    const controller = new AbortController()
    hydrateController = controller
    try {
      const label = await args.lookupById(id, { signal: controller.signal })
      if (sequence !== hydrateSequence || controller.signal.aborted) return
      selected.value = { id, label: label ?? id } as TItem
    } catch {
      if (sequence !== hydrateSequence || controller.signal.aborted) return
      selected.value = { id, label: id } as TItem
    } finally {
      if (hydrateController === controller) hydrateController = null
    }
  }

  async function onQuery(queryText: string): Promise<void> {
    const sequence = ++searchSequence
    searchController?.abort()
    const query = queryText.trim()
    if (!query) {
      items.value = []
      return
    }

    const controller = new AbortController()
    searchController = controller
    try {
      const nextItems = await args.search(query, { signal: controller.signal })
      if (sequence === searchSequence && !controller.signal.aborted) items.value = nextItems
    } catch (cause) {
      if (!controller.signal.aborted && sequence === searchSequence) throw cause
    } finally {
      if (searchController === controller) searchController = null
    }
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

  if (getCurrentScope()) {
    onScopeDispose(() => {
      hydrateSequence += 1
      searchSequence += 1
      hydrateController?.abort()
      searchController?.abort()
      hydrateController = null
      searchController = null
    })
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
