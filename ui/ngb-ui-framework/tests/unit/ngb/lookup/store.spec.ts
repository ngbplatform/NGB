import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const lookupConfigMocks = vi.hoisted(() => ({
  loadCatalogItemsByIds: vi.fn(),
  searchCatalog: vi.fn(),
  loadCoaItemsByIds: vi.fn(),
  loadCoaItem: vi.fn(),
  searchCoa: vi.fn(),
  loadDocumentItemsByIds: vi.fn(),
  searchDocumentsAcrossTypes: vi.fn(),
  loadDocumentItem: vi.fn(),
  searchDocument: vi.fn(),
}))

vi.mock('../../../../src/ngb/lookup/config', () => ({
  getConfiguredNgbLookup: () => lookupConfigMocks,
}))

import { useLookupStore } from '../../../../src/ngb/lookup/store'
import { shortGuid } from '../../../../src/ngb/utils/guid'

const propertyId = '11111111-1111-1111-1111-111111111111'
const harborId = '22222222-2222-2222-2222-222222222222'
const invoiceId = '33333333-3333-3333-3333-333333333333'
const missingDocumentId = '44444444-4444-4444-4444-444444444444'
const coaId = '77777777-7777-7777-7777-777777777777'

describe('lookup store', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())

    lookupConfigMocks.loadCatalogItemsByIds.mockResolvedValue([])
    lookupConfigMocks.searchCatalog.mockResolvedValue([])
    lookupConfigMocks.loadCoaItemsByIds.mockResolvedValue([])
    lookupConfigMocks.loadCoaItem.mockResolvedValue({ id: 'coa-1', label: 'Cash' })
    lookupConfigMocks.searchCoa.mockResolvedValue([])
    lookupConfigMocks.loadDocumentItemsByIds.mockResolvedValue([])
    lookupConfigMocks.searchDocumentsAcrossTypes.mockResolvedValue([])
    lookupConfigMocks.loadDocumentItem.mockRejectedValue(new Error('Not found'))
    lookupConfigMocks.searchDocument.mockResolvedValue([])
  })

  it('loads and caches catalog labels while keeping short-guid fallbacks for missing ids', async () => {
    lookupConfigMocks.loadCatalogItemsByIds.mockResolvedValue([
      { id: propertyId, label: 'Riverfront Tower' },
    ])
    lookupConfigMocks.searchCatalog.mockResolvedValue([
      { id: harborId, label: 'Harbor Point' },
    ])

    const store = useLookupStore()

    await store.ensureCatalogLabels('pm.property', [propertyId, propertyId, 'not-a-guid'])
    expect(lookupConfigMocks.loadCatalogItemsByIds).toHaveBeenCalledWith('pm.property', [propertyId])
    expect(store.labelForCatalog('pm.property', propertyId)).toBe('Riverfront Tower')
    expect(store.labelForCatalog('pm.property', harborId)).toBe(shortGuid(harborId))

    const searchResults = await store.searchCatalog('pm.property', 'harbor')
    expect(searchResults).toEqual([{ id: harborId, label: 'Harbor Point' }])
    expect(store.labelForCatalog('pm.property', harborId)).toBe('Harbor Point')
  })

  it('uses bulk coa resolution and falls back to short guid for unresolved accounts', async () => {
    const unresolvedCoaId = '55555555-5555-5555-5555-555555555555'
    lookupConfigMocks.loadCoaItemsByIds.mockResolvedValue([
      { id: coaId, label: '1010 — Cash' },
    ])

    const store = useLookupStore()

    await store.ensureCoaLabels([coaId, unresolvedCoaId, unresolvedCoaId])

    expect(lookupConfigMocks.loadCoaItemsByIds).toHaveBeenCalledWith([coaId, unresolvedCoaId])
    expect(lookupConfigMocks.loadCoaItem).not.toHaveBeenCalled()
    expect(store.labelForCoa(coaId)).toBe('1010 — Cash')
    expect(store.labelForCoa(unresolvedCoaId)).toBe(shortGuid(unresolvedCoaId))
  })

  it('resolves labels across candidate document types with one bulk call and falls back to the first type short guid when none match', async () => {
    lookupConfigMocks.loadDocumentItemsByIds.mockResolvedValue([
      {
        id: invoiceId,
        label: 'Credit Memo CM-001',
        documentType: 'pm.credit_note',
      },
    ])

    const store = useLookupStore()

    await store.ensureAnyDocumentLabels(['pm.invoice', 'pm.credit_note'], [invoiceId, missingDocumentId])

    expect(lookupConfigMocks.loadDocumentItemsByIds).toHaveBeenCalledWith(
      ['pm.invoice', 'pm.credit_note'],
      [invoiceId, missingDocumentId],
    )
    expect(lookupConfigMocks.loadDocumentItem).not.toHaveBeenCalled()
    expect(store.labelForAnyDocument(['pm.invoice', 'pm.credit_note'], invoiceId)).toBe('Credit Memo CM-001')
    expect(store.labelForDocument('pm.credit_note', invoiceId)).toBe('Credit Memo CM-001')
    expect(store.labelForDocument('pm.invoice', missingDocumentId)).toBe(shortGuid(missingDocumentId))
  })

  it('uses cross-type search results, dedupes by id, and stores labels under the resolved source type', async () => {
    lookupConfigMocks.searchDocumentsAcrossTypes.mockResolvedValue([
      { id: invoiceId, label: 'Invoice INV-001', documentType: 'pm.invoice' },
      { id: '55555555-5555-5555-5555-555555555555', label: 'Shared document', documentType: 'pm.invoice' },
      { id: '55555555-5555-5555-5555-555555555555', label: 'Shared credit memo', documentType: 'pm.credit_note' },
      { id: '66666666-6666-6666-6666-666666666666', label: 'Credit Memo CM-002', documentType: 'pm.credit_note' },
    ])

    const store = useLookupStore()
    const results = await store.searchDocuments(['pm.invoice', 'pm.credit_note'], 'cm')

    expect(lookupConfigMocks.searchDocumentsAcrossTypes).toHaveBeenCalledWith(['pm.invoice', 'pm.credit_note'], 'cm')
    expect(lookupConfigMocks.searchDocument).not.toHaveBeenCalled()
    expect(results).toEqual([
      { id: invoiceId, label: 'Invoice INV-001' },
      { id: '55555555-5555-5555-5555-555555555555', label: 'Shared document' },
      { id: '66666666-6666-6666-6666-666666666666', label: 'Credit Memo CM-002' },
    ])
    expect(store.labelForDocument('pm.invoice', '55555555-5555-5555-5555-555555555555')).toBe('Shared document')
    expect(store.labelForDocument('pm.credit_note', '55555555-5555-5555-5555-555555555555')).toBe('Shared credit memo')
  })
})
