import {
  extractLookupIds,
  hydrateResolvedLookupItems,
  searchResolvedLookupItems,
} from '../metadata/filtering'
import type { LookupStoreApi } from '../metadata/types'
import type {
  ReportComposerDraft,
  ReportComposerLookupItem,
  ReportDefinitionDto,
  ReportFilterValueDto,
} from './types'

export type ReportLookupStoreApi = LookupStoreApi<ReportComposerLookupItem>

export async function searchReportLookupItems(
  lookupStore: ReportLookupStoreApi,
  lookup: NonNullable<NonNullable<ReportDefinitionDto['filters']>[number]['lookup']>,
  query: string,
  options?: { signal?: AbortSignal },
): Promise<ReportComposerLookupItem[]> {
  return await searchResolvedLookupItems(lookupStore, lookup, query, options)
}

export async function hydrateReportLookupItemsFromFilters(
  lookupStore: ReportLookupStoreApi,
  definition: Pick<ReportDefinitionDto, 'filters'>,
  draft: ReportComposerDraft,
  filters: Record<string, ReportFilterValueDto> | null | undefined,
): Promise<void> {
  if (!filters) return

  const tasks: Array<Promise<{ state: ReportComposerDraft['filters'][string]; items: ReportComposerLookupItem[] }>> = []
  for (const field of definition.filters ?? []) {
    const state = draft.filters[field.fieldCode]
    const filterValue = filters[field.fieldCode]
    if (!state || !field.lookup || !filterValue) continue

    const ids = extractLookupIds(filterValue.value)
    if (ids.length === 0) continue

    tasks.push(
      hydrateResolvedLookupItems(lookupStore, field.lookup, ids)
        .then((items) => ({ state, items })),
    )
  }

  const hydrated = await Promise.all(tasks)
  for (const { state, items } of hydrated) {
    state.items = items
    state.raw = ''
  }
}
