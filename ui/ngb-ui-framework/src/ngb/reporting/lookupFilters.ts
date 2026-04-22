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
): Promise<ReportComposerLookupItem[]> {
  return await searchResolvedLookupItems(lookupStore, lookup, query)
}

export async function hydrateReportLookupItemsFromFilters(
  lookupStore: ReportLookupStoreApi,
  definition: Pick<ReportDefinitionDto, 'filters'>,
  draft: ReportComposerDraft,
  filters: Record<string, ReportFilterValueDto> | null | undefined,
): Promise<void> {
  if (!filters) return

  for (const field of definition.filters ?? []) {
    const state = draft.filters[field.fieldCode]
    const filterValue = filters[field.fieldCode]
    if (!state || !field.lookup || !filterValue) continue

    const ids = extractLookupIds(filterValue.value)
    if (ids.length === 0) continue

    state.items = await hydrateResolvedLookupItems(lookupStore, field.lookup, ids)
    if (state.items.length > 0) state.raw = ''
  }
}
