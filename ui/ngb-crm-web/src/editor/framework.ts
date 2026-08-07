import type { DocumentEffects, EditorFrameworkConfig, LookupHint, LookupStoreApi } from '@ngbplatform/ui'
import {
  executeDocumentAction,
  getDocumentById,
  getDocumentEditorState,
  getDocumentEffects,
  getDocumentGraph,
  getEntityAuditLog,
  isNonEmptyGuid,
  isReferenceValue,
  shortGuid,
  useLookupStore,
} from '@ngbplatform/ui'

import { getCRMLookupHint } from '../lookup/hints'
import { resolveCRMEditorEntityProfile } from './entityProfile'

type CRMEffectLookupHint =
  | Extract<LookupHint, { kind: 'catalog' }>
  | Extract<LookupHint, { kind: 'document' }>

function normalizePathSegment(value: string | null | undefined): string {
  return String(value ?? '').trim()
}

function buildDocumentFullPageUrl(documentType: string, id?: string | null): string {
  const type = encodeURIComponent(normalizePathSegment(documentType))
  const normalizedId = normalizePathSegment(id)
  if (!normalizedId) return `/documents/${type}/new`
  return `/documents/${type}/${encodeURIComponent(normalizedId)}`
}

const CRM_EFFECT_DOCUMENT_TYPES = [
  'crm.lead_intake',
  'crm.lead_qualification',
  'crm.lead_conversion',
  'crm.opportunity_update',
  'crm.quote',
  'crm.activity_log',
]

function extractGuidValue(value: unknown): string | null {
  if (typeof value === 'string') return isNonEmptyGuid(value) ? value : null
  if (isReferenceValue(value)) return isNonEmptyGuid(value.id) ? value.id : null
  return null
}

function resolveCRMEffectLookupHint(fieldKey: string): CRMEffectLookupHint | null {
  switch (fieldKey.toLowerCase()) {
    case 'source_document_id':
      return { kind: 'document', documentTypes: CRM_EFFECT_DOCUMENT_TYPES }
    case 'lead_intake_id':
      return { kind: 'document', documentTypes: ['crm.lead_intake'] }
    case 'opportunity_id':
      return { kind: 'document', documentTypes: ['crm.lead_conversion'] }
    case 'quote_id':
      return { kind: 'document', documentTypes: ['crm.quote'] }
    case 'activity_id':
      return { kind: 'document', documentTypes: ['crm.activity_log'] }
    case 'account_id':
    case 'converted_account_id':
      return { kind: 'catalog', catalogType: 'crm.account' }
    case 'contact_id':
    case 'converted_contact_id':
      return { kind: 'catalog', catalogType: 'crm.contact' }
    case 'stage_id':
      return { kind: 'catalog', catalogType: 'crm.opportunity_stage' }
    case 'product_id':
      return { kind: 'catalog', catalogType: 'crm.product' }
    default:
      return null
  }
}

function resolveCRMEffectLookupLabel(lookupStore: LookupStoreApi, hint: CRMEffectLookupHint, id: string): string {
  const label = hint.kind === 'catalog'
    ? lookupStore.labelForCatalog(hint.catalogType, id)
    : lookupStore.labelForAnyDocument(hint.documentTypes, id)

  const normalized = label.trim()
  return normalized && normalized !== id ? normalized : shortGuid(id)
}

async function prefetchCRMEffectLabels(args: {
  effects: DocumentEffects
  lookupStore: LookupStoreApi | null
}): Promise<void> {
  if (!args.lookupStore) return

  const catalogIdsByType = new Map<string, Set<string>>()
  const documentIdsByTypesKey = new Map<string, { documentTypes: string[]; ids: Set<string> }>()

  for (const write of args.effects.referenceRegisterWrites ?? []) {
    for (const [fieldKey, value] of Object.entries(write.fields ?? {})) {
      const id = extractGuidValue(value)
      if (!id) continue

      const hint = resolveCRMEffectLookupHint(fieldKey)
      if (!hint) continue

      if (hint.kind === 'catalog') {
        const ids = catalogIdsByType.get(hint.catalogType) ?? new Set<string>()
        ids.add(id)
        catalogIdsByType.set(hint.catalogType, ids)
        continue
      }

      const key = hint.documentTypes.join('|')
      const group = documentIdsByTypesKey.get(key) ?? { documentTypes: hint.documentTypes, ids: new Set<string>() }
      group.ids.add(id)
      documentIdsByTypesKey.set(key, group)
    }
  }

  const tasks: Promise<void>[] = []
  for (const [catalogType, ids] of catalogIdsByType) {
    tasks.push(args.lookupStore.ensureCatalogLabels(catalogType, [...ids]))
  }
  for (const group of documentIdsByTypesKey.values()) {
    tasks.push(args.lookupStore.ensureAnyDocumentLabels(group.documentTypes, [...group.ids]))
  }

  await Promise.all(tasks)
}

export function createCRMEditorConfig(): EditorFrameworkConfig {
  const lookupStore = useLookupStore()

  return {
    documentActions: {
      loadEditorState: getDocumentEditorState,
      execute: executeDocumentAction,
    },
    routing: {
      buildDocumentFullPageUrl,
    },
    loadDocumentById: getDocumentById,
    loadDocumentEffects: getDocumentEffects,
    loadDocumentGraph: getDocumentGraph,
    loadEntityAuditLog: getEntityAuditLog,
    lookupStore,
    effects: {
      prefetchRelatedLabels: ({ effects, lookupStore }) => prefetchCRMEffectLabels({ effects, lookupStore }),
      resolveFieldValue: ({ fieldKey, value, lookupStore }) => {
        if (!lookupStore) return null

        const id = extractGuidValue(value)
        if (!id) return null

        const hint = resolveCRMEffectLookupHint(fieldKey)
        if (!hint) return null

        return resolveCRMEffectLookupLabel(lookupStore, hint, id)
      },
    },
    audit: {
      hiddenFieldNames: [],
      explicitFieldLabels: {
        lead_intake_id: 'Lead',
        opportunity_id: 'Opportunity',
        account_id: 'Account',
        contact_id: 'Contact',
        product_id: 'Product',
        stage_id: 'Stage',
      },
    },
    print: {
      resolveLookupHint: ({ documentType, fieldKey, lookup }) =>
        getCRMLookupHint(documentType, fieldKey, lookup) ?? null,
    },
    resolveEntityProfile: resolveCRMEditorEntityProfile,
  }
}
