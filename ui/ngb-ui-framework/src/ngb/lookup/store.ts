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
const MAX_LABELS_PER_ENTITY_TYPE = 1_000
const MAX_COA_LABELS = 2_000

function boundLabels(labels: Record<string, string>, limit: number): Record<string, string> {
  const keys = Object.keys(labels)
  if (keys.length <= limit) return labels

  const next = { ...labels }
  for (const key of keys.slice(0, keys.length - limit)) delete next[key]
  return next
}

function normalizeDocumentTypes(documentTypes: string[]): string[] {
  return Array.from(new Set(documentTypes.map((entry) => String(entry ?? '').trim()).filter((entry) => entry.length > 0)))
}

type PendingLabelRequests = Map<string, Map<string, Promise<void>>>

async function coalesceLabelRequests(
  requests: PendingLabelRequests,
  scope: string,
  ids: readonly string[],
  load: (ids: string[]) => Promise<void>,
): Promise<void> {
  let pendingById = requests.get(scope)
  if (!pendingById) {
    pendingById = new Map<string, Promise<void>>()
    requests.set(scope, pendingById)
  }

  const waitFor = new Set<Promise<void>>()
  const freshIds: string[] = []
  for (const id of ids) {
    const pending = pendingById.get(id)
    if (pending) waitFor.add(pending)
    else freshIds.push(id)
  }

  if (freshIds.length > 0) {
    let request!: Promise<void>
    request = Promise.resolve()
      .then(() => load(freshIds))
      .finally(() => {
        for (const id of freshIds) {
          pendingById.delete(id)
        }
        if (pendingById?.size === 0) requests.delete(scope)
      })

    for (const id of freshIds) pendingById.set(id, request)
    waitFor.add(request)
  }

  await Promise.all(waitFor)
}

async function loadCoaItems(config: LookupFrameworkConfig, ids: string[]): Promise<LookupItem[]> {
  return await config.loadCoaItemsByIds(ids)
}

async function loadResolvedDocumentItems(
  config: LookupFrameworkConfig,
  documentTypes: string[],
  ids: string[],
): Promise<ResolvedDocumentLookupItem[]> {
  return await config.loadDocumentItemsByIds(documentTypes, ids)
}

async function searchResolvedDocumentItems(
  config: LookupFrameworkConfig,
  documentTypes: string[],
  query: string,
  options?: { signal?: AbortSignal },
): Promise<ResolvedDocumentLookupItem[]> {
  return options
    ? await config.searchDocumentsAcrossTypes(documentTypes, query, options)
    : await config.searchDocumentsAcrossTypes(documentTypes, query)
}

export const useLookupStore = defineStore('lookup', () => {
  const catalogLabels = ref<Record<string, Record<string, string>>>({})
  const coaLabels = ref<Record<string, string>>({})
  const documentLabels = ref<Record<string, Record<string, string>>>({})
  const pendingCatalogLabels: PendingLabelRequests = new Map()
  const pendingCoaLabels: PendingLabelRequests = new Map()
  const pendingDocumentLabels: PendingLabelRequests = new Map()

  function mergeCatalogItems(catalogType: string, items: readonly LookupItem[]) {
    if (items.length === 0) return
    const existing = catalogLabels.value[catalogType] ?? {}
    const next = { ...existing }

    for (const item of items) {
      const id = String(item.id ?? '').trim()
      const label = String(item.label ?? '').trim()
      if (!id || !label) continue
      next[id] = label
    }

    catalogLabels.value = { ...catalogLabels.value, [catalogType]: boundLabels(next, MAX_LABELS_PER_ENTITY_TYPE) }
  }

  function mergeDocumentItems(documentType: string, items: readonly LookupItem[]) {
    if (items.length === 0) return
    const existing = documentLabels.value[documentType] ?? {}
    const next = { ...existing }

    for (const item of items) {
      const id = String(item.id ?? '').trim()
      const label = String(item.label ?? '').trim()
      if (!id || !label) continue
      next[id] = label
    }

    documentLabels.value = { ...documentLabels.value, [documentType]: boundLabels(next, MAX_LABELS_PER_ENTITY_TYPE) }
  }

  function mergeResolvedDocumentItems(items: readonly ResolvedDocumentLookupItem[]) {
    const byType = new Map<string, LookupItem[]>()
    for (const item of items) {
      const documentType = String(item.documentType ?? '').trim()
      if (!documentType) continue
      const group = byType.get(documentType) ?? []
      group.push(item)
      byType.set(documentType, group)
    }

    if (byType.size === 0) return
    const nextLabels = { ...documentLabels.value }
    for (const [documentType, group] of byType) {
      const next = { ...(nextLabels[documentType] ?? {}) }
      for (const item of group) {
        const id = String(item.id ?? '').trim()
        const label = String(item.label ?? '').trim()
        if (id && label) next[id] = label
      }
      nextLabels[documentType] = boundLabels(next, MAX_LABELS_PER_ENTITY_TYPE)
    }
    documentLabels.value = nextLabels
  }

  async function ensureCatalogLabels(catalogType: string, ids: string[]) {
    const uniq = Array.from(new Set(ids.filter(isNonEmptyGuid)))
    if (uniq.length === 0) return

    const existing = catalogLabels.value[catalogType] ?? {}
    const missing = uniq.filter((id) => !existing[id])
    if (missing.length === 0) return

    await coalesceLabelRequests(pendingCatalogLabels, catalogType, missing, async (freshIds) => {
      const config = getConfiguredNgbLookup()
      const items = await config.loadCatalogItemsByIds(catalogType, freshIds)
      mergeCatalogItems(catalogType, items)
    })
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

    await coalesceLabelRequests(pendingCoaLabels, 'coa', missing, async (freshIds) => {
      const config = getConfiguredNgbLookup()
      const items = await loadCoaItems(config, freshIds).catch(() => [])
      const next = { ...coaLabels.value }

      for (const item of items) {
        const id = String(item.id ?? '').trim()
        const label = String(item.label ?? '').trim()
        if (!id || !label) continue
        next[id] = label
      }

      for (const id of freshIds) {
        if (!next[id]) next[id] = shortGuid(id)
      }

      coaLabels.value = boundLabels(next, MAX_COA_LABELS)
    })
  }

  function labelForCoa(id: unknown): string {
    if (!isNonEmptyGuid(id)) return String(id ?? '—')
    return coaLabels.value[id] ?? shortGuid(id)
  }

  async function searchCoa(query: string, options?: { signal?: AbortSignal }): Promise<UiLookupItem[]> {
    const config = getConfiguredNgbLookup()
    const items = options ? await config.searchCoa(query, options) : await config.searchCoa(query)
    const next = { ...coaLabels.value }

    for (const item of items) {
      const id = String(item.id ?? '').trim()
      const label = String(item.label ?? '').trim()
      if (!id || !label) continue
      next[id] = label
    }

    coaLabels.value = boundLabels(next, MAX_COA_LABELS)
    return items.map((item) => ({ ...item }))
  }

  async function ensureAnyDocumentLabels(documentTypes: string[], ids: string[]) {
    const types = normalizeDocumentTypes(documentTypes)
    const uniq = Array.from(new Set(ids.filter(isNonEmptyGuid)))
    if (types.length === 0 || uniq.length === 0) return

    const missing = uniq.filter((id) => !types.some((documentType) => !!documentLabels.value[documentType]?.[id]))
    if (missing.length === 0) return

    const requestScope = JSON.stringify(types)
    await coalesceLabelRequests(pendingDocumentLabels, requestScope, missing, async (freshIds) => {
      const config = getConfiguredNgbLookup()
      const items = await loadResolvedDocumentItems(config, types, freshIds).catch(() => [])
      mergeResolvedDocumentItems(items)

      const resolvedIds = new Set(items.map((item) => item.id))
      mergeDocumentItems(
        types[0]!,
        freshIds.filter((id) => !resolvedIds.has(id)).map((id) => ({ id, label: shortGuid(id) })),
      )
    })
  }

  async function ensureDocumentLabels(documentType: string, ids: string[]) {
    const uniq = Array.from(new Set(ids.filter(isNonEmptyGuid)))
    if (uniq.length === 0) return

    const existing = documentLabels.value[documentType] ?? {}
    const missing = uniq.filter((id) => !existing[id])
    if (missing.length === 0) return

    const types = [documentType]
    await coalesceLabelRequests(pendingDocumentLabels, JSON.stringify(types), missing, async (freshIds) => {
      const config = getConfiguredNgbLookup()
      const items = await loadResolvedDocumentItems(config, types, freshIds).catch(() => [])
      mergeResolvedDocumentItems(items)

      const resolvedIds = new Set(items.map((item) => item.id))
      mergeDocumentItems(
        documentType,
        freshIds.filter((id) => !resolvedIds.has(id)).map((id) => ({ id, label: shortGuid(id) })),
      )
    })
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

  async function searchDocuments(documentTypes: string[], query: string, options?: { signal?: AbortSignal }): Promise<UiLookupItem[]> {
    const types = normalizeDocumentTypes(documentTypes)
    if (types.length === 0) return []

    const config = getConfiguredNgbLookup()
    const items = await searchResolvedDocumentItems(config, types, query, options)
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

  async function searchDocument(documentType: string, query: string, options?: { signal?: AbortSignal }): Promise<UiLookupItem[]> {
    const config = getConfiguredNgbLookup()
    const items = options
      ? await config.searchDocument(documentType, query, options)
      : await config.searchDocument(documentType, query)
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
