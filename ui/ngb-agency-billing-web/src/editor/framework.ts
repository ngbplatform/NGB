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

import { getAgencyBillingLookupHint } from '../lookup/hints'
import { resolveAgencyBillingEditorEntityProfile } from './entityProfile'

const AGENCY_BILLING_EFFECT_DOCUMENT_TYPES = [
  'ab.client_contract',
  'ab.timesheet',
  'ab.sales_invoice',
  'ab.customer_payment',
]

const AGENCY_BILLING_EFFECT_DOCUMENT_FIELD_KEYS = new Set([
  'contract_id',
  'source_timesheet_id',
  'sales_invoice_id',
])

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

async function prefetchAgencyBillingEffectDocumentLabels(args: {
  effects: DocumentEffects
  lookupStore: ReturnType<typeof useLookupStore>
}): Promise<void> {
  const ids = new Set<string>()

  for (const write of args.effects.referenceRegisterWrites ?? []) {
    for (const [fieldKey, value] of Object.entries(write.fields ?? {})) {
      if (!fieldKey.endsWith('_document_id') && !AGENCY_BILLING_EFFECT_DOCUMENT_FIELD_KEYS.has(fieldKey)) continue

      const id = tryExtractEffectDocumentId(value)
      if (id) ids.add(id)
    }
  }

  if (ids.size === 0) return
  await args.lookupStore.ensureAnyDocumentLabels(AGENCY_BILLING_EFFECT_DOCUMENT_TYPES, [...ids])
}

function resolveAgencyBillingEffectFieldValue(args: {
  documentId: string
  documentDisplay?: string | null
  fieldKey: string
  value: unknown
  lookupStore: ReturnType<typeof useLookupStore>
}): string | null {
  if (!args.fieldKey.endsWith('_document_id') && !AGENCY_BILLING_EFFECT_DOCUMENT_FIELD_KEYS.has(args.fieldKey)) return null

  const id = tryExtractEffectDocumentId(args.value)
  if (!id) return null

  if (id === args.documentId) {
    const currentDisplay = String(args.documentDisplay ?? '').trim()
    if (currentDisplay) return currentDisplay
  }

  const resolved = args.lookupStore.labelForAnyDocument(AGENCY_BILLING_EFFECT_DOCUMENT_TYPES, id).trim()
  if (resolved) return looksLikeGuidLabel(resolved) ? shortGuid(id) : resolved
  return shortGuid(id)
}

function defaultBuildDocumentFullPageUrl(documentType: string, id?: string | null): string {
  const type = encodeURIComponent(normalizePathSegment(documentType))
  const normalizedId = normalizePathSegment(id)
  if (!normalizedId) return `/documents/${type}/new`
  return `/documents/${type}/${encodeURIComponent(normalizedId)}`
}

export function createAgencyBillingEditorConfig(): EditorFrameworkConfig {
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
        'project_time_ledger_register_id',
        'unbilled_time_register_id',
        'project_billing_status_register_id',
      ],
      explicitFieldLabels: {
        cash_account_id: 'Cash / Bank Account',
        ar_account_id: 'Accounts Receivable Account',
        service_revenue_account_id: 'Service Revenue Account',
      },
    },
    effects: {
      prefetchRelatedLabels: ({ effects }) => prefetchAgencyBillingEffectDocumentLabels({ effects, lookupStore }),
      resolveFieldValue: ({ documentId, document, fieldKey, value }) =>
        resolveAgencyBillingEffectFieldValue({
          documentId,
          documentDisplay: document?.display,
          fieldKey,
          value,
          lookupStore,
        }),
    },
    print: {
      resolveLookupHint: ({ documentType, fieldKey, lookup }) =>
        getAgencyBillingLookupHint(documentType, fieldKey, lookup) ?? null,
    },
    resolveEntityProfile: resolveAgencyBillingEditorEntityProfile,
  }
}
