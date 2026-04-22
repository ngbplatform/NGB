import type {
  DocumentLookupDto,
  LookupItemDto,
} from '../api/contracts'
import { getCatalogPage } from '../api/catalogs'
import {
  getDocumentById,
  getDocumentLookupByIds,
  getDocumentPage,
  lookupDocumentsAcrossTypes,
} from '../api/documents'
import { getCatalogLookupByIds, lookupCatalog } from '../api/lookups'
import {
  getChartOfAccountById,
  getChartOfAccountsByIds,
  getChartOfAccountsPage,
} from '../accounting/api'
import { buildChartOfAccountsPath } from '../accounting/navigation'
import { buildCatalogFullPageUrl } from '../editor/catalogNavigation'
import { buildDocumentFullPageUrl } from '../editor/documentNavigation'
import type { LookupItem } from '../metadata/types'
import { shortGuid } from '../utils/guid'
import type { LookupFrameworkConfig, ResolvedDocumentLookupItem } from './config'

function toMetaString(meta: LookupItemDto['meta'] | string | undefined | null): string | undefined {
  if (!meta) return undefined
  if (typeof meta === 'string') return meta
  if (typeof meta === 'object') {
    const pairs = Object.entries(meta)
      .slice(0, 4)
      .map(([key, value]) => `${key}: ${String(value)}`)
    return pairs.length > 0 ? pairs.join(' · ') : undefined
  }
  return String(meta)
}

function asLookupItem(item: { id: string; label: string; meta?: string | undefined }): LookupItem {
  return {
    id: item.id,
    label: item.label,
    meta: item.meta,
  }
}

function asResolvedDocumentLookupItem(item: DocumentLookupDto): ResolvedDocumentLookupItem {
  const label = String(item.display ?? '').trim() || shortGuid(item.id)
  return {
    id: item.id,
    label,
    documentType: item.documentType,
  }
}

function mapLookupItemDto(item: LookupItemDto): LookupItem {
  return asLookupItem({
    id: item.id,
    label: item.label,
    meta: toMetaString(item.meta),
  })
}

export function createDefaultNgbLookupConfig(): LookupFrameworkConfig {
  return {
    loadCatalogItemsByIds: async (catalogType, ids) => (await getCatalogLookupByIds(catalogType, ids)).map(mapLookupItemDto),
    searchCatalog: async (catalogType, query, options) => {
      const filters = options?.filters ?? null

      if (!filters || Object.keys(filters).length === 0) {
        return (await lookupCatalog(catalogType, query, 25)).map(mapLookupItemDto)
      }

      const page = await getCatalogPage(catalogType, {
        offset: 0,
        limit: 25,
        search: query,
        filters: {
          deleted: 'active',
          ...filters,
        },
      })

      return (page.items ?? []).map((item) =>
        asLookupItem({
          id: item.id,
          label: item.display ?? shortGuid(item.id),
        }))
    },
    loadCoaItemsByIds: async (ids) => (await getChartOfAccountsByIds(ids)).map(mapLookupItemDto),
    loadCoaItem: async (id) => {
      const account = await getChartOfAccountById(id)
      return asLookupItem({
        id: account.accountId,
        label: `${account.code} — ${account.name}`,
      })
    },
    searchCoa: async (query) => {
      const page = await getChartOfAccountsPage({ search: query, limit: 25, onlyActive: true, includeDeleted: false })
      return (page.items ?? []).map((account) =>
        asLookupItem({
          id: account.accountId,
          label: `${account.code} — ${account.name}`,
        }))
    },
    loadDocumentItemsByIds: async (documentTypes, ids) => {
      if (documentTypes.length === 0 || ids.length === 0) return []
      return (await getDocumentLookupByIds({ documentTypes, ids })).map(asResolvedDocumentLookupItem)
    },
    searchDocumentsAcrossTypes: async (documentTypes, query) => {
      if (documentTypes.length === 0) return []
      return (await lookupDocumentsAcrossTypes({
        documentTypes,
        query,
        perTypeLimit: 25,
        activeOnly: true,
      })).map(asResolvedDocumentLookupItem)
    },
    loadDocumentItem: async (documentType, id) => {
      const document = await getDocumentById(documentType, id)
      return asLookupItem({
        id: document.id,
        label: document.display ?? shortGuid(document.id),
      })
    },
    searchDocument: async (documentType, query) => {
      const page = await getDocumentPage(documentType, { offset: 0, limit: 25, search: query })
      return (page.items ?? []).map((document) =>
        asLookupItem({
          id: document.id,
          label: document.display ?? shortGuid(document.id),
        }))
    },
    buildCatalogUrl: (catalogType, id) => buildCatalogFullPageUrl(catalogType, id),
    buildCoaUrl: (id) => buildChartOfAccountsPath({ panel: 'edit', id }),
    buildDocumentUrl: (documentType, id) => buildDocumentFullPageUrl(documentType, id),
  }
}
