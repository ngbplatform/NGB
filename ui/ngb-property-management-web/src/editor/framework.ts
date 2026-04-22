import type { EditorFrameworkConfig, DocumentEffects, EffectDimensionValue } from 'ngb-ui-framework'
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

import { getLookupHint } from '../lookup/hints'
import { resolvePmEditorDocumentActions } from './documentActions'
import { resolvePmEditorEntityProfile } from './entityProfile'

const PM_EFFECT_DIMENSION_DOCUMENT_TYPES = [
  'pm.lease',
  'pm.rent_charge',
  'pm.receivable_charge',
  'pm.late_fee_charge',
  'pm.receivable_payment',
  'pm.receivable_returned_payment',
  'pm.receivable_credit_memo',
  'pm.receivable_apply',
  'pm.payable_charge',
  'pm.payable_payment',
  'pm.payable_credit_memo',
  'pm.payable_apply',
  'pm.maintenance_request',
  'pm.work_order',
  'pm.work_order_completion',
]

function normalizePathSegment(value: string | null | undefined): string {
  return String(value ?? '').trim()
}

function defaultBuildDocumentFullPageUrl(documentType: string, id?: string | null): string {
  const type = encodeURIComponent(normalizePathSegment(documentType))
  const normalizedId = normalizePathSegment(id)
  if (!normalizedId) return `/documents/${type}/new`
  return `/documents/${type}/${encodeURIComponent(normalizedId)}`
}

function looksLikeGuidLabel(value: string | null | undefined): boolean {
  const s = String(value ?? '').trim()
  return isNonEmptyGuid(s) && !isEmptyGuid(s)
}

function looksLikeSyntheticDocumentLabel(display: string | null | undefined, valueId: string | null | undefined): boolean {
  const label = String(display ?? '').trim()
  const id = String(valueId ?? '').trim()
  if (!label || !isNonEmptyGuid(id) || isEmptyGuid(id)) return false

  const short = id.slice(0, 8).toLowerCase()
  const normalized = label.toLowerCase()
  return normalized.endsWith(short) || normalized.endsWith(`…${id.slice(-4).toLowerCase()}`)
}

async function prefetchDimensionDocumentLabels(snapshot: DocumentEffects, lookupStore: ReturnType<typeof useLookupStore>): Promise<void> {
  const ids = new Set<string>()

  const collect = (values: EffectDimensionValue[] | null | undefined) => {
    for (const item of values ?? []) {
      const display = String(item?.display ?? '').trim()
      const valueId = String(item?.valueId ?? '').trim()
      if (
        isNonEmptyGuid(valueId)
        && !isEmptyGuid(valueId)
        && (!display || looksLikeGuidLabel(display) || looksLikeSyntheticDocumentLabel(display, valueId))
      ) {
        ids.add(valueId)
      }
    }
  }

  for (const entry of snapshot.accountingEntries ?? []) {
    collect(entry.debitDimensions)
    collect(entry.creditDimensions)
  }

  for (const movement of snapshot.operationalRegisterMovements ?? []) collect(movement.dimensions)
  for (const write of snapshot.referenceRegisterWrites ?? []) collect(write.dimensions)

  if (ids.size === 0) return
  await lookupStore.ensureAnyDocumentLabels(PM_EFFECT_DIMENSION_DOCUMENT_TYPES, Array.from(ids))
}

function resolveDimensionDisplay(
  item: EffectDimensionValue | null | undefined,
  lookupStore: ReturnType<typeof useLookupStore>,
): string {
  const display = String(item?.display ?? '').trim()
  const valueId = String(item?.valueId ?? '').trim()

  if (isNonEmptyGuid(valueId) && !isEmptyGuid(valueId)) {
    const resolved = lookupStore.labelForAnyDocument(PM_EFFECT_DIMENSION_DOCUMENT_TYPES, valueId)
    if (resolved && !looksLikeGuidLabel(resolved) && !looksLikeSyntheticDocumentLabel(resolved, valueId)) {
      if (!display || looksLikeGuidLabel(display) || looksLikeSyntheticDocumentLabel(display, valueId)) return resolved
    }
  }

  if (display && !looksLikeGuidLabel(display)) return display
  return valueId ? shortGuid(valueId) : '—'
}

export function createPmEditorConfig(): EditorFrameworkConfig {
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
        'tenant_balances_register_id',
        'receivables_open_items_register_id',
        'payables_open_items_register_id',
      ],
      explicitFieldLabels: {
        cash_account_id: 'Default Cash Control Account',
        bank_account_id: 'Bank Account',
        gl_account_id: 'GL Account',
        ar_tenants_account_id: 'Tenant Receivables (A/R) Account',
        ap_vendors_account_id: 'Vendor Payables (A/P) Account',
        rent_income_account_id: 'Rental Income Account',
      },
    },
    effects: {
      prefetchRelatedLabels: ({ effects }) => prefetchDimensionDocumentLabels(effects, lookupStore),
      resolveDimensionDisplay: ({ item }) => resolveDimensionDisplay(item, lookupStore),
    },
    print: {
      resolveLookupHint: ({ documentType, fieldKey, lookup }) => getLookupHint(documentType, fieldKey, lookup),
    },
    resolveDocumentActions: resolvePmEditorDocumentActions,
    resolveEntityProfile: resolvePmEditorEntityProfile,
  }
}
