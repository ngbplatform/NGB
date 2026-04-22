import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { LookupItem } from '../metadata/types'
import { isNonEmptyGuid, shortGuid } from '../utils/guid'
import {
  getConfiguredNgbLookup,
  type LookupSearchOptions,
  type LookupFrameworkConfig,
  type ResolvedDocumentLookupItem,
} from './config'

export type UiLookupItem = LookupItem

function normalizeDocumentTypes(documentTypes: string[]): string[] {
  return Array.from(new Set(documentTypes.map((entry) => String(entry ?? '').trim()).filter((entry) => entry.length > 0)))
}

async function loadCoaItems(config: LookupFrameworkConfig, ids: string[]): Promise<LookupItem[]> {
  if (ids.length === 0) return []
  return await config.loadCoaItemsByIds(ids)
}

async function loadResolvedDocumentItems(
  config: LookupFrameworkConfig,
  documentTypes: string[],
  ids: string[],
): Promise<ResolvedDocumentLookupItem[]> {
  if (documentTypes.length === 0 || ids.length === 0) return []
  return await config.loadDocumentItemsByIds(documentTypes, ids)
}

async function searchResolvedDocumentItems(
  config: LookupFrameworkConfig,
  documentTypes: string[],
  query: string,
): Promise<ResolvedDocumentLookupItem[]> {
  if (documentTypes.length === 0) return []
  return await config.searchDocumentsAcrossTypes(documentTypes, query)
}

export const useLookupStore = defineStore('lookup', () => {
  const catalogLabels = ref<Record<string, Record<string, string>>>({})
  const coaLabels = ref<Record<string, string>>({})
  const documentLabels = ref<Record<string, Record<string, string>>>({})

  function mergeCatalogItems(catalogType: string, items: readonly LookupItem[]) {
    const existing = catalogLabels.value[catalogType] ?? {}
    const next = { ...existing }

    for (const item of items) {
      const id = String(item.id ?? '').trim()
      const label = String(item.label ?? '').trim()
      if (!id || !label) continue
      next[id] = label
    }

    catalogLabels.value = { ...catalogLabels.value, [catalogType]: next }
  }

  function mergeDocumentItems(documentType: string, items: readonly LookupItem[]) {
    const existing = documentLabels.value[documentType] ?? {}
    const next = { ...existing }

    for (const item of items) {
      const id = String(item.id ?? '').trim()
      const label = String(item.label ?? '').trim()
      if (!id || !label) continue
      next[id] = label
    }

    documentLabels.value = { ...documentLabels.value, [documentType]: next }
  }

  function mergeResolvedDocumentItems(items: readonly ResolvedDocumentLookupItem[]) {
    for (const item of items) {
      mergeDocumentItems(item.documentType, [item])
    }
  }

  async function ensureCatalogLabels(catalogType: string, ids: string[]) {
    const uniq = Array.from(new Set(ids.filter(isNonEmptyGuid)))
    if (uniq.length === 0) return

    const existing = catalogLabels.value[catalogType] ?? {}
    const missing = uniq.filter((id) => !existing[id])
    if (missing.length === 0) return

    const config = getConfiguredNgbLookup()
    const items = await config.loadCatalogItemsByIds(catalogType, missing)
    mergeCatalogItems(catalogType, items)
  }

  function labelForCatalog(catalogType: string, id: unknown): string {
    if (!isNonEmptyGuid(id)) return String(id ?? '—')
    return catalogLabels.value[catalogType]?.[id] ?? shortGuid(id)
  }

  async function searchCatalog(
    catalogType: string,
    query: string,
    options?: LookupSearchOptions,
  ): Promise<UiLookupItem[]> {
    const config = getConfiguredNgbLookup()
    const items = await config.searchCatalog(catalogType, query, options)
    mergeCatalogItems(catalogType, items)
    return items.map((item) => ({ ...item }))
  }

  async function ensureCoaLabels(ids: string[]) {
    const uniq = Array.from(new Set(ids.filter(isNonEmptyGuid)))
    if (uniq.length === 0) return

    const missing = uniq.filter((id) => !coaLabels.value[id])
    if (missing.length === 0) return

    const config = getConfiguredNgbLookup()
    const items = await loadCoaItems(config, missing).catch(() => [])
    const next = { ...coaLabels.value }

    for (const item of items) {
      const id = String(item.id ?? '').trim()
      const label = String(item.label ?? '').trim()
      if (!id || !label) continue
      next[id] = label
    }

    for (const id of missing) {
      if (!next[id]) {
        next[id] = shortGuid(id)
      }
    }

    coaLabels.value = next
  }

  function labelForCoa(id: unknown): string {
    if (!isNonEmptyGuid(id)) return String(id ?? '—')
    return coaLabels.value[id] ?? shortGuid(id)
  }

  async function searchCoa(query: string): Promise<UiLookupItem[]> {
    const config = getConfiguredNgbLookup()
    const items = await config.searchCoa(query)
    const next = { ...coaLabels.value }

    for (const item of items) {
      const id = String(item.id ?? '').trim()
      const label = String(item.label ?? '').trim()
      if (!id || !label) continue
      next[id] = label
    }

    coaLabels.value = next
    return items.map((item) => ({ ...item }))
  }

  async function ensureAnyDocumentLabels(documentTypes: string[], ids: string[]) {
    const types = normalizeDocumentTypes(documentTypes)
    const uniq = Array.from(new Set(ids.filter(isNonEmptyGuid)))
    if (types.length === 0 || uniq.length === 0) return

    const missing = uniq.filter((id) => !types.some((documentType) => !!documentLabels.value[documentType]?.[id]))
    if (missing.length === 0) return

    const config = getConfiguredNgbLookup()
    const items = await loadResolvedDocumentItems(config, types, missing).catch(() => [])
    mergeResolvedDocumentItems(items)

    const resolvedIds = new Set(items.map((item) => item.id))
    const fallbackType = types[0]!

    for (const id of missing) {
      if (!resolvedIds.has(id)) {
        mergeDocumentItems(fallbackType, [{ id, label: shortGuid(id) }])
      }
    }
  }

  async function ensureDocumentLabels(documentType: string, ids: string[]) {
    const uniq = Array.from(new Set(ids.filter(isNonEmptyGuid)))
    if (uniq.length === 0) return

    const existing = documentLabels.value[documentType] ?? {}
    const missing = uniq.filter((id) => !existing[id])
    if (missing.length === 0) return

    const config = getConfiguredNgbLookup()
    const items = await loadResolvedDocumentItems(config, [documentType], missing).catch(() => [])
    mergeResolvedDocumentItems(items)

    const resolvedIds = new Set(items.map((item) => item.id))
    for (const id of missing) {
      if (!resolvedIds.has(id)) {
        mergeDocumentItems(documentType, [{ id, label: shortGuid(id) }])
      }
    }
  }

  function labelForAnyDocument(documentTypes: string[], id: unknown): string {
    if (!isNonEmptyGuid(id)) return String(id ?? '—')

    for (const documentType of documentTypes) {
      const label = documentLabels.value[documentType]?.[id]
      if (label) return label
    }

    return shortGuid(id)
  }

  function labelForDocument(documentType: string, id: unknown): string {
    if (!isNonEmptyGuid(id)) return String(id ?? '—')
    return documentLabels.value[documentType]?.[id] ?? shortGuid(id)
  }

  async function searchDocuments(documentTypes: string[], query: string): Promise<UiLookupItem[]> {
    const types = normalizeDocumentTypes(documentTypes)
    if (types.length === 0) return []

    const config = getConfiguredNgbLookup()
    const items = await searchResolvedDocumentItems(config, types, query)
    mergeResolvedDocumentItems(items)

    const seen = new Set<string>()
    const merged: UiLookupItem[] = []

    for (const item of items) {
      const id = String(item.id ?? '').trim()
      if (!id || seen.has(id)) continue
      seen.add(id)
      merged.push({
        id: item.id,
        label: item.label,
        meta: item.meta,
      })
    }

    return merged
  }

  async function searchDocument(documentType: string, query: string): Promise<UiLookupItem[]> {
    const config = getConfiguredNgbLookup()
    const items = await config.searchDocument(documentType, query)
    mergeDocumentItems(documentType, items)
    return items.map((item) => ({ ...item }))
  }

  return {
    ensureCatalogLabels,
    searchCatalog,
    labelForCatalog,
    ensureCoaLabels,
    searchCoa,
    labelForCoa,
    ensureAnyDocumentLabels,
    ensureDocumentLabels,
    searchDocuments,
    searchDocument,
    labelForAnyDocument,
    labelForDocument,
  }
})
