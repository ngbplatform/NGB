import type { LookupHint, LookupSource } from 'ngb-ui-framework'

const PM_TENANT_PARTY_DOCUMENTS = new Set([
  'pm.rent_charge',
  'pm.receivable_charge',
  'pm.late_fee_charge',
  'pm.receivable_payment',
  'pm.receivable_returned_payment',
  'pm.receivable_credit_memo',
])

const PM_VENDOR_PARTY_DOCUMENTS = new Set([
  'pm.payable_charge',
  'pm.payable_payment',
  'pm.payable_credit_memo',
])

const PM_UNIT_PROPERTY_DOCUMENTS = new Set([
  'pm.lease',
  ...PM_TENANT_PARTY_DOCUMENTS,
])

const PM_PAYABLE_CHARGE_TYPE_DOCUMENTS = new Set([
  'pm.payable_charge',
  'pm.payable_credit_memo',
])

const PM_SUFFIX_CATALOG_TYPES = {
  unit: 'pm.unit',
  building: 'pm.building',
  vendor: 'pm.vendor',
  bank_account: 'pm.bank_account',
  payable_charge_type: 'pm.payable_charge_type',
} as const

const PM_SUFFIX_DOCUMENT_TYPES = {
  lease: ['pm.lease'],
} as const satisfies Record<string, readonly string[]>

const PM_RECEIVABLE_APPLY_DOCUMENT_TYPES = {
  credit_document: ['pm.receivable_payment', 'pm.receivable_credit_memo'],
  charge_document: ['pm.receivable_charge', 'pm.late_fee_charge', 'pm.rent_charge'],
} as const satisfies Record<string, readonly string[]>

const PM_PAYABLE_APPLY_DOCUMENT_TYPES = {
  credit_document: ['pm.payable_payment', 'pm.payable_credit_memo'],
  charge_document: ['pm.payable_charge'],
} as const satisfies Record<string, readonly string[]>

function normalizeEntityTypeCode(entityTypeCode: string): string {
  return entityTypeCode.trim().toLowerCase()
}

function isPmEntity(entityTypeCode: string): boolean {
  return normalizeEntityTypeCode(entityTypeCode).startsWith('pm.')
}

function toCatalogHint(catalogType: string, filters?: Record<string, string>): LookupHint {
  return filters ? { kind: 'catalog', catalogType, filters } : { kind: 'catalog', catalogType }
}

function toDocumentHint(documentTypes: readonly string[]): LookupHint {
  return { kind: 'document', documentTypes: [...documentTypes] }
}

function toExplicitLookupHint(metaLookup?: LookupSource | null): LookupHint | null {
  if (!metaLookup) return null

  switch (metaLookup.kind) {
    case 'catalog':
      return { kind: 'catalog', catalogType: metaLookup.catalogType }
    case 'document':
      return { kind: 'document', documentTypes: metaLookup.documentTypes }
    case 'coa':
      return { kind: 'coa' }
    default:
      return null
  }
}

function getPmCatalogFilters(entityTypeCode: string, key: string, catalogType: string): Record<string, string> | undefined {
  if (!isPmEntity(entityTypeCode)) return undefined

  const normalizedEntityType = normalizeEntityTypeCode(entityTypeCode)
  if (catalogType === 'pm.party' && key === 'party_id') {
    if (PM_VENDOR_PARTY_DOCUMENTS.has(normalizedEntityType)) return { is_vendor: 'true' }
    if (PM_TENANT_PARTY_DOCUMENTS.has(normalizedEntityType)) return { is_tenant: 'true' }
  }

  if (catalogType === 'pm.property' && key === 'property_id' && PM_UNIT_PROPERTY_DOCUMENTS.has(normalizedEntityType)) {
    return { kind: 'Unit' }
  }

  return undefined
}

function getPmDirectLookupHint(entityTypeCode: string, key: string): LookupHint | null {
  const normalizedEntityType = normalizeEntityTypeCode(entityTypeCode)

  switch (key) {
    case 'party_id':
      return toCatalogHint('pm.party', getPmCatalogFilters(normalizedEntityType, key, 'pm.party'))
    case 'property_id':
      return toCatalogHint('pm.property', getPmCatalogFilters(normalizedEntityType, key, 'pm.property'))
    case 'parent_property_id':
      return toCatalogHint('pm.property')
    case 'charge_type_id':
      return toCatalogHint(
        PM_PAYABLE_CHARGE_TYPE_DOCUMENTS.has(normalizedEntityType)
          ? 'pm.payable_charge_type'
          : 'pm.receivable_charge_type',
      )
    default:
      return null
  }
}

function getPmSuffixLookupHint(entityTypeCode: string, key: string): LookupHint | null {
  if (!key.endsWith('_id')) return null

  const base = key.slice(0, -3)
  const catalogType = PM_SUFFIX_CATALOG_TYPES[base as keyof typeof PM_SUFFIX_CATALOG_TYPES]
  if (catalogType) return toCatalogHint(catalogType)

  const documentTypes = PM_SUFFIX_DOCUMENT_TYPES[base as keyof typeof PM_SUFFIX_DOCUMENT_TYPES]
  if (documentTypes) return toDocumentHint(documentTypes)

  const normalizedEntityType = normalizeEntityTypeCode(entityTypeCode)
  if (normalizedEntityType === 'pm.receivable_apply') {
    const applyDocumentTypes = PM_RECEIVABLE_APPLY_DOCUMENT_TYPES[base as keyof typeof PM_RECEIVABLE_APPLY_DOCUMENT_TYPES]
    if (applyDocumentTypes) return toDocumentHint(applyDocumentTypes)
  }

  if (normalizedEntityType === 'pm.payable_apply') {
    const applyDocumentTypes = PM_PAYABLE_APPLY_DOCUMENT_TYPES[base as keyof typeof PM_PAYABLE_APPLY_DOCUMENT_TYPES]
    if (applyDocumentTypes) return toDocumentHint(applyDocumentTypes)
  }

  if (normalizedEntityType === 'pm.receivable_returned_payment' && base === 'original_payment') {
    return toDocumentHint(['pm.receivable_payment'])
  }

  return null
}

function getCompatibilityLookupHint(entityTypeCode: string, key: string): LookupHint | null {
  if (key === 'bank_account_id') return toCatalogHint('pm.bank_account')

  if (key.endsWith('_account_id') || key === 'account_id') return { kind: 'coa' }

  if (!isPmEntity(entityTypeCode)) return null

  const directHint = getPmDirectLookupHint(entityTypeCode, key)
  if (directHint) return directHint

  return getPmSuffixLookupHint(entityTypeCode, key)
}

export function getLookupHint(entityTypeCode: string, fieldKey: string, metaLookup?: LookupSource | null): LookupHint | null {
  const key = fieldKey.toLowerCase()
  const explicit = toExplicitLookupHint(metaLookup)

  if (explicit?.kind === 'catalog') {
    return toCatalogHint(explicit.catalogType, getPmCatalogFilters(entityTypeCode, key, explicit.catalogType))
  }

  if (explicit) return explicit

  return getCompatibilityLookupHint(entityTypeCode, key)
}
