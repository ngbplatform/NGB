import { isNonEmptyGuid } from '../utils/guid'
import type { FilterFieldLike, FilterFieldOption, FilterFieldState, FilterLookupItem, LookupStoreApi, ResolvedLookupSource } from './types'

export type {
  FilterFieldLike,
  FilterFieldOption,
  FilterFieldState,
  FilterLookupItem,
  LookupStoreApi,
  ResolvedLookupSource,
} from './types'

export function splitFilterValues(raw: string): string[] {
  return raw
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part.length > 0)
}

export function joinFilterValues(values: string[]): string {
  return values
    .map((value) => value.trim())
    .filter((value) => value.length > 0)
    .join(',')
}

export function summarizeFilterValues(values: string[]): string | null {
  if (values.length === 0) return null
  const first = values[0]
  const remaining = values.length - 1
  return remaining > 0 ? `${first} (+${remaining})` : first
}

export function optionLabelForFilter(field: Pick<FilterFieldLike, 'options'>, rawValue: string): string {
  const normalized = rawValue.trim()
  if (!normalized) return ''
  const option = (field.options ?? []).find((entry) => String(entry.value ?? '').trim() === normalized)
  return String(option?.label ?? normalized).trim()
}

type FilterOptionLabelField = {
  key: string
  options?: readonly FilterFieldOption[] | null
}

export function buildFilterOptionLabelsByKey<TField extends FilterOptionLabelField>(
  fields: readonly TField[],
): Record<string, Map<string, string>> {
  const result: Record<string, Map<string, string>> = {}

  for (const field of fields) {
    if (!field.key || !field.options?.length) continue

    const options = new Map<string, string>()
    for (const option of field.options) {
      const value = String(option?.value ?? '').trim()
      const label = String(option?.label ?? '').trim()
      if (!value || !label) continue
      options.set(value, label)
    }

    if (options.size > 0) result[field.key] = options
  }

  return result
}

export function filterInputType(dataType: unknown): 'number' | 'text' {
  const normalized = String(dataType ?? '').trim().toLowerCase()
  if (normalized === 'decimal' || normalized === 'money' || normalized === 'int32' || normalized === '2' || normalized === '3') {
    return 'number'
  }
  if (normalized.includes('decimal') || normalized.includes('number') || normalized.includes('int')) return 'number'
  return 'text'
}

export function filterPlaceholder(field: Pick<FilterFieldLike, 'label' | 'lookup' | 'isMulti'>): string {
  if (field.lookup) return `Type ${field.label.toLowerCase()}…`
  if (field.isMulti) return 'Comma-separated values…'
  return field.label
}

export function filterSelectOptions(
  field: Pick<FilterFieldLike, 'options'>,
  emptyLabel = 'Any',
): FilterFieldOption[] {
  return [{ value: '', label: emptyLabel }, ...((field.options ?? []) as FilterFieldOption[])]
}

export function extractLookupIds(value: unknown): string[] {
  if (Array.isArray(value)) return value.filter(isNonEmptyGuid)
  return isNonEmptyGuid(value) ? [value] : []
}

export function labelForResolvedLookup(
  lookupStore: LookupStoreApi,
  lookup: ResolvedLookupSource,
  id: string,
): string {
  if (lookup.kind === 'catalog') return lookupStore.labelForCatalog(lookup.catalogType, id)
  if (lookup.kind === 'coa') return lookupStore.labelForCoa(id)
  return lookupStore.labelForAnyDocument(lookup.documentTypes, id)
}

export async function ensureResolvedLookupLabels(
  lookupStore: LookupStoreApi,
  lookup: ResolvedLookupSource,
  ids: string[],
): Promise<void> {
  if (lookup.kind === 'catalog') {
    await lookupStore.ensureCatalogLabels(lookup.catalogType, ids)
    return
  }

  if (lookup.kind === 'coa') {
    await lookupStore.ensureCoaLabels(ids)
    return
  }

  await lookupStore.ensureAnyDocumentLabels(lookup.documentTypes, ids)
}

export async function searchResolvedLookupItems<TItem extends FilterLookupItem>(
  lookupStore: LookupStoreApi<TItem>,
  lookup: ResolvedLookupSource,
  query: string,
): Promise<TItem[]> {
  if (lookup.kind === 'catalog') {
    return await lookupStore.searchCatalog(
      lookup.catalogType,
      query,
      'filters' in lookup && lookup.filters ? { filters: lookup.filters } : undefined,
    )
  }

  if (lookup.kind === 'coa') return await lookupStore.searchCoa(query)
  return await lookupStore.searchDocuments(lookup.documentTypes, query)
}

export async function hydrateResolvedLookupItems<TItem extends FilterLookupItem>(
  lookupStore: LookupStoreApi<TItem>,
  lookup: ResolvedLookupSource,
  ids: string[],
): Promise<TItem[]> {
  if (ids.length === 0) return []

  await ensureResolvedLookupLabels(lookupStore, lookup, ids)

  return ids.map((id) => ({
    id,
    label: labelForResolvedLookup(lookupStore, lookup, id),
  }) as TItem)
}
