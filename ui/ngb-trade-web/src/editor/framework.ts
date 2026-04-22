import type { DocumentEffects, EditorFrameworkConfig } from 'ngb-ui-framework'
import {
  buildGeneralJournalEntriesPath,
  getDocumentById,
  getDocumentEffects,
  getDocumentGraph,
  getEntityAuditLog,
  isEmptyGuid,
  isGeneralJournalEntryDocumentType,
  isNonEmptyGuid,
  shortGuid,
  useLookupStore,
} from 'ngb-ui-framework'

import { getTradeLookupHint } from '../lookup/hints'
import { resolveTradeEditorEntityProfile } from './entityProfile'

const TRADE_EFFECT_DOCUMENT_TYPES = [
  'trd.purchase_receipt',
  'trd.sales_invoice',
  'trd.customer_payment',
  'trd.vendor_payment',
  'trd.inventory_transfer',
  'trd.inventory_adjustment',
  'trd.customer_return',
  'trd.vendor_return',
  'trd.item_price_update',
]

function normalizePathSegment(value: string | null | undefined): string {
  return String(value ?? '').trim()
}

function looksLikeGuidLabel(value: string | null | undefined): boolean {
  const s = String(value ?? '').trim()
  return isNonEmptyGuid(s) && !isEmptyGuid(s)
}

function tryExtractEffectDocumentId(value: unknown): string | null {
  const normalized = String(value ?? '').trim()
  if (!isNonEmptyGuid(normalized) || isEmptyGuid(normalized)) return null
  return normalized
}

async function prefetchTradeEffectDocumentLabels(args: {
  effects: DocumentEffects
  lookupStore: ReturnType<typeof useLookupStore>
}): Promise<void> {
  const ids = new Set<string>()

  for (const write of args.effects.referenceRegisterWrites ?? []) {
    for (const [fieldKey, value] of Object.entries(write.fields ?? {})) {
      if (!fieldKey.endsWith('_document_id')) continue

      const id = tryExtractEffectDocumentId(value)
      if (id) ids.add(id)
    }
  }

  if (ids.size === 0) return
  await args.lookupStore.ensureAnyDocumentLabels(TRADE_EFFECT_DOCUMENT_TYPES, [...ids])
}

function resolveTradeEffectFieldValue(args: {
  documentId: string
  documentDisplay?: string | null
  fieldKey: string
  value: unknown
  lookupStore: ReturnType<typeof useLookupStore>
}): string | null {
  if (!args.fieldKey.endsWith('_document_id')) return null

  const id = tryExtractEffectDocumentId(args.value)
  if (!id) return null

  if (id === args.documentId) {
    const currentDisplay = String(args.documentDisplay ?? '').trim()
    if (currentDisplay) return currentDisplay
  }

  const resolved = args.lookupStore.labelForAnyDocument(TRADE_EFFECT_DOCUMENT_TYPES, id).trim()
  if (resolved) return looksLikeGuidLabel(resolved) ? shortGuid(id) : resolved
  return shortGuid(id)
}

function defaultBuildDocumentFullPageUrl(documentType: string, id?: string | null): string {
  const type = encodeURIComponent(normalizePathSegment(documentType))
  const normalizedId = normalizePathSegment(id)
  if (!normalizedId) return `/documents/${type}/new`
  return `/documents/${type}/${encodeURIComponent(normalizedId)}`
}

export function createTradeEditorConfig(): EditorFrameworkConfig {
  const lookupStore = useLookupStore()

  return {
    routing: {
      buildDocumentFullPageUrl(documentType, id) {
        if (isGeneralJournalEntryDocumentType(documentType)) {
          return buildGeneralJournalEntriesPath(id)
        }

        return defaultBuildDocumentFullPageUrl(documentType, id)
      },
    },
    loadDocumentById: getDocumentById,
    loadDocumentEffects: getDocumentEffects,
    loadDocumentGraph: getDocumentGraph,
    loadEntityAuditLog: getEntityAuditLog,
    lookupStore,
    audit: {
      hiddenFieldNames: [
        'inventory_movements_register_id',
        'item_prices_register_id',
      ],
      explicitFieldLabels: {
        cash_account_id: 'Cash / Bank Account',
        ar_account_id: 'Accounts Receivable Account',
        inventory_account_id: 'Inventory Account',
        ap_account_id: 'Accounts Payable Account',
        sales_revenue_account_id: 'Sales Revenue Account',
        cogs_account_id: 'Cost of Goods Sold Account',
        inventory_adjustment_account_id: 'Inventory Adjustment Account',
      },
    },
    effects: {
      prefetchRelatedLabels: ({ effects }) => prefetchTradeEffectDocumentLabels({ effects, lookupStore }),
      resolveFieldValue: ({ documentId, document, fieldKey, value }) =>
        resolveTradeEffectFieldValue({
          documentId,
          documentDisplay: document?.display,
          fieldKey,
          value,
          lookupStore,
        }),
    },
    print: {
      resolveLookupHint: ({ documentType, fieldKey, lookup }) =>
        getTradeLookupHint(documentType, fieldKey, lookup) ?? null,
    },
    resolveEntityProfile: resolveTradeEditorEntityProfile,
  }
}
