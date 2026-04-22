import type { Awaitable, LookupItem } from '../metadata/types'

export type LookupSearchOptions = {
  filters?: Record<string, string>
}

export type ResolvedDocumentLookupItem = LookupItem & {
  documentType: string
}

export type LookupFrameworkConfig = {
  loadCatalogItemsByIds: (catalogType: string, ids: string[]) => Awaitable<LookupItem[]>
  searchCatalog: (catalogType: string, query: string, options?: LookupSearchOptions) => Awaitable<LookupItem[]>
  loadCoaItem: (id: string) => Awaitable<LookupItem | null>
  loadCoaItemsByIds: (ids: string[]) => Awaitable<LookupItem[]>
  searchCoa: (query: string) => Awaitable<LookupItem[]>
  loadDocumentItem: (documentType: string, id: string) => Awaitable<LookupItem | null>
  loadDocumentItemsByIds: (documentTypes: string[], ids: string[]) => Awaitable<ResolvedDocumentLookupItem[]>
  searchDocument: (documentType: string, query: string) => Awaitable<LookupItem[]>
  searchDocumentsAcrossTypes: (documentTypes: string[], query: string) => Awaitable<ResolvedDocumentLookupItem[]>
  buildCatalogUrl: (catalogType: string, id: string) => string
  buildCoaUrl: (id: string) => string
  buildDocumentUrl: (documentType: string, id: string) => string
}

let lookupFrameworkConfig: LookupFrameworkConfig | null = null

export function configureNgbLookup(config: LookupFrameworkConfig): void {
  lookupFrameworkConfig = config
}

export function getConfiguredNgbLookup(): LookupFrameworkConfig {
  if (!lookupFrameworkConfig) {
    throw new Error('NGB lookup framework is not configured. Call configureNgbLookup(...) during app bootstrap.')
  }

  return lookupFrameworkConfig
}

export function maybeGetConfiguredNgbLookup(): LookupFrameworkConfig | null {
  return lookupFrameworkConfig
}
