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
    vi.resetAllMocks()
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
      { id: null, label: 'Missing id' },
      { id: unresolvedCoaId, label: null },
      { id: coaId, label: '1010 — Cash' },
    ])

    const store = useLookupStore()

    await store.ensureCoaLabels([coaId, unresolvedCoaId, unresolvedCoaId])

    expect(lookupConfigMocks.loadCoaItemsByIds).toHaveBeenCalledWith([coaId, unresolvedCoaId])
    expect(lookupConfigMocks.loadCoaItem).not.toHaveBeenCalled()
    expect(store.labelForCoa(coaId)).toBe('1010 — Cash')
    expect(store.labelForCoa(unresolvedCoaId)).toBe(shortGuid(unresolvedCoaId))
    expect(store.labelForCoa(harborId)).toBe(shortGuid(harborId))
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

  it('handles empty, invalid, duplicate, and already cached catalog inputs', async () => {
    lookupConfigMocks.loadCatalogItemsByIds.mockResolvedValue([
      { id: null, label: 'Missing id' },
      { id: propertyId, label: null },
      { id: propertyId, label: 'Riverfront Tower' },
    ])

    const store = useLookupStore()

    await store.ensureCatalogLabels('pm.property', ['invalid'])
    expect(lookupConfigMocks.loadCatalogItemsByIds).not.toHaveBeenCalled()

    await store.ensureCatalogLabels('pm.property', [propertyId])
    await store.ensureCatalogLabels('pm.property', [propertyId])
    expect(lookupConfigMocks.loadCatalogItemsByIds).toHaveBeenCalledTimes(1)
    expect(store.labelForCatalog('pm.property', null)).toBe('—')
    expect(store.labelForCatalog('pm.property', 'invalid')).toBe('invalid')
  })

  it('covers coa empty/cache/error fallbacks and search normalization', async () => {
    const unresolvedCoaId = '55555555-5555-5555-5555-555555555555'
    lookupConfigMocks.loadCoaItemsByIds.mockRejectedValueOnce(new Error('COA unavailable'))
    lookupConfigMocks.searchCoa.mockResolvedValueOnce([
      { id: null, label: 'Missing id' },
      { id: coaId, label: null },
      { id: coaId, label: '1010 — Cash' },
    ])

    const store = useLookupStore()

    await store.ensureCoaLabels([])
    await store.ensureCoaLabels([unresolvedCoaId])
    await store.ensureCoaLabels([unresolvedCoaId])
    expect(lookupConfigMocks.loadCoaItemsByIds).toHaveBeenCalledTimes(1)
    expect(store.labelForCoa(unresolvedCoaId)).toBe(shortGuid(unresolvedCoaId))
    expect(store.labelForCoa(null)).toBe('—')
    expect(store.labelForCoa('invalid')).toBe('invalid')

    const results = await store.searchCoa('cash')
    expect(results).toHaveLength(3)
    expect(store.labelForCoa(coaId)).toBe('1010 — Cash')
  })

  it('covers document ensure early exits, failed resolution, cache hits, and per-type loading', async () => {
    const perTypeResolvedId = '55555555-5555-5555-5555-555555555555'
    const perTypeMissingId = '66666666-6666-6666-6666-666666666666'
    const store = useLookupStore()

    await store.ensureAnyDocumentLabels([], [invoiceId])
    await store.ensureAnyDocumentLabels([null as never, '  '], [invoiceId])
    await store.ensureAnyDocumentLabels(['pm.invoice'], ['invalid'])
    expect(lookupConfigMocks.loadDocumentItemsByIds).not.toHaveBeenCalled()

    lookupConfigMocks.loadDocumentItemsByIds.mockRejectedValueOnce(new Error('Documents unavailable'))
    await store.ensureAnyDocumentLabels(['pm.invoice'], [invoiceId])
    await store.ensureAnyDocumentLabels(['pm.invoice'], [invoiceId])
    expect(lookupConfigMocks.loadDocumentItemsByIds).toHaveBeenCalledTimes(1)
    expect(store.labelForAnyDocument(['pm.invoice'], invoiceId)).toBe(shortGuid(invoiceId))

    await store.ensureDocumentLabels('pm.adjustment', [])
    lookupConfigMocks.loadDocumentItemsByIds.mockResolvedValueOnce([
      { id: perTypeResolvedId, label: 'Invoice INV-002', documentType: 'pm.adjustment' },
    ])
    await store.ensureDocumentLabels('pm.adjustment', [perTypeResolvedId, perTypeMissingId])
    await store.ensureDocumentLabels('pm.adjustment', [perTypeResolvedId, perTypeMissingId])

    expect(store.labelForDocument('pm.adjustment', perTypeResolvedId)).toBe('Invoice INV-002')
    expect(store.labelForDocument('pm.adjustment', perTypeMissingId)).toBe(shortGuid(perTypeMissingId))
    expect(store.labelForAnyDocument(['pm.credit_note', 'pm.adjustment'], perTypeResolvedId)).toBe('Invoice INV-002')
    expect(store.labelForAnyDocument([], perTypeResolvedId)).toBe(shortGuid(perTypeResolvedId))
    expect(store.labelForAnyDocument([], null)).toBe('—')
    expect(store.labelForDocument('pm.invoice', null)).toBe('—')
    expect(store.labelForDocument('pm.invoice', harborId)).toBe(shortGuid(harborId))

    lookupConfigMocks.loadDocumentItemsByIds.mockRejectedValueOnce(new Error('Single document type unavailable'))
    await store.ensureDocumentLabels('pm.credit_note', [harborId])
    expect(store.labelForDocument('pm.credit_note', harborId)).toBe(shortGuid(harborId))
  })

  it('handles empty and malformed cross-type searches and the single-type search API', async () => {
    lookupConfigMocks.searchDocumentsAcrossTypes.mockResolvedValueOnce([
      { id: null, label: 'Missing id', documentType: 'pm.invoice' },
      { id: invoiceId, label: 'Invoice INV-003', documentType: 'pm.invoice', meta: 'Open' },
      { id: invoiceId, label: 'Duplicate invoice', documentType: 'pm.credit_note' },
    ])
    lookupConfigMocks.searchDocument.mockResolvedValueOnce([
      { id: null, label: 'Missing id' },
      { id: invoiceId, label: null },
      { id: invoiceId, label: 'Invoice INV-003' },
    ])

    const store = useLookupStore()

    await expect(store.searchDocuments([], 'invoice')).resolves.toEqual([])
    const acrossTypes = await store.searchDocuments(['pm.invoice', 'pm.invoice', '  '], 'invoice')
    expect(lookupConfigMocks.searchDocumentsAcrossTypes).toHaveBeenCalledWith(['pm.invoice'], 'invoice')
    expect(acrossTypes).toEqual([{ id: invoiceId, label: 'Invoice INV-003', meta: 'Open' }])

    const singleType = await store.searchDocument('pm.invoice', 'INV-003')
    expect(singleType).toHaveLength(3)
    expect(store.labelForDocument('pm.invoice', invoiceId)).toBe('Invoice INV-003')
  })

  it('coalesces concurrent label resolution for catalog, coa, and document ids', async () => {
    let resolveCatalog!: (items: Array<{ id: string; label: string }>) => void
    let resolveCoa!: (items: Array<{ id: string; label: string }>) => void
    let resolveDocuments!: (items: Array<{ id: string; label: string; documentType: string }>) => void
    lookupConfigMocks.loadCatalogItemsByIds.mockImplementationOnce(() => new Promise((resolve) => {
      resolveCatalog = resolve
    }))
    lookupConfigMocks.loadCoaItemsByIds.mockImplementationOnce(() => new Promise((resolve) => {
      resolveCoa = resolve
    }))
    lookupConfigMocks.loadDocumentItemsByIds.mockImplementationOnce(() => new Promise((resolve) => {
      resolveDocuments = resolve
    }))
    const store = useLookupStore()

    const catalogRequests = [
      store.ensureCatalogLabels('pm.property', [propertyId]),
      store.ensureCatalogLabels('pm.property', [propertyId]),
    ]
    const coaRequests = [store.ensureCoaLabels([coaId]), store.ensureCoaLabels([coaId])]
    const documentRequests = [
      store.ensureDocumentLabels('pm.invoice', [invoiceId]),
      store.ensureAnyDocumentLabels(['pm.invoice'], [invoiceId]),
    ]

    await vi.waitFor(() => {
      expect(lookupConfigMocks.loadCatalogItemsByIds).toHaveBeenCalledTimes(1)
      expect(lookupConfigMocks.loadCoaItemsByIds).toHaveBeenCalledTimes(1)
      expect(lookupConfigMocks.loadDocumentItemsByIds).toHaveBeenCalledTimes(1)
    })
    resolveCatalog([{ id: propertyId, label: 'Riverfront Tower' }])
    resolveCoa([{ id: coaId, label: '1010 — Cash' }])
    resolveDocuments([{ id: invoiceId, label: 'Invoice INV-004', documentType: 'pm.invoice' }])
    await Promise.all([...catalogRequests, ...coaRequests, ...documentRequests])

    expect(store.labelForCatalog('pm.property', propertyId)).toBe('Riverfront Tower')
    expect(store.labelForCoa(coaId)).toBe('1010 — Cash')
    expect(store.labelForDocument('pm.invoice', invoiceId)).toBe('Invoice INV-004')
  })

  it('bounds long-lived label caches while retaining the newest entries', async () => {
    const id = (index: number) => `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`
    const catalogItems = Array.from({ length: 1_001 }, (_, index) => ({
      id: id(index),
      label: `Property ${index}`,
    }))
    const documentItems = catalogItems.map((item) => ({
      ...item,
      documentType: 'pm.invoice',
    }))
    const coaItems = Array.from({ length: 2_001 }, (_, index) => ({
      id: id(index),
      label: `Account ${index}`,
    }))
    lookupConfigMocks.searchCatalog.mockResolvedValueOnce(catalogItems)
    lookupConfigMocks.searchDocumentsAcrossTypes.mockResolvedValueOnce(documentItems)
    lookupConfigMocks.searchCoa.mockResolvedValueOnce(coaItems)
    const store = useLookupStore()

    await store.searchCatalog('pm.property', '')
    await store.searchDocuments(['pm.invoice'], '')
    await store.searchCoa('')

    expect(store.labelForCatalog('pm.property', id(0))).toBe(shortGuid(id(0)))
    expect(store.labelForCatalog('pm.property', id(1_000))).toBe('Property 1000')
    expect(store.labelForDocument('pm.invoice', id(0))).toBe(shortGuid(id(0)))
    expect(store.labelForDocument('pm.invoice', id(1_000))).toBe('Property 1000')
    expect(store.labelForCoa(id(0))).toBe(shortGuid(id(0)))
    expect(store.labelForCoa(id(2_000))).toBe('Account 2000')
  })

  it('covers empty merges, malformed resolved documents, and cancellable searches', async () => {
    const options = { signal: new AbortController().signal }
    lookupConfigMocks.searchCatalog.mockResolvedValueOnce([])
    lookupConfigMocks.searchCoa.mockResolvedValueOnce([])
    lookupConfigMocks.searchDocumentsAcrossTypes.mockResolvedValueOnce([
      { id: invoiceId, label: 'Ignored', documentType: null },
      { id: harborId, label: null, documentType: 'pm.invoice' },
    ])
    lookupConfigMocks.searchDocument.mockResolvedValueOnce([])
    const store = useLookupStore()

    await store.searchCatalog('pm.property', '')
    await store.searchCoa('', options)
    await store.searchDocuments(['pm.invoice'], '', options)
    await store.searchDocument('pm.invoice', '', options)

    expect(lookupConfigMocks.searchCoa).toHaveBeenCalledWith('', options)
    expect(lookupConfigMocks.searchDocumentsAcrossTypes).toHaveBeenCalledWith(['pm.invoice'], '', options)
    expect(lookupConfigMocks.searchDocument).toHaveBeenCalledWith('pm.invoice', '', options)
  })

  it('keeps a request scope alive until every independent batch finishes', async () => {
    let resolveFirst!: (items: Array<{ id: string; label: string }>) => void
    let resolveSecond!: (items: Array<{ id: string; label: string }>) => void
    lookupConfigMocks.loadCatalogItemsByIds
      .mockImplementationOnce(() => new Promise((resolve) => { resolveFirst = resolve }))
      .mockImplementationOnce(() => new Promise((resolve) => { resolveSecond = resolve }))
    const store = useLookupStore()
    const first = store.ensureCatalogLabels('pm.property', [propertyId])
    const second = store.ensureCatalogLabels('pm.property', [harborId])
    await vi.waitFor(() => expect(lookupConfigMocks.loadCatalogItemsByIds).toHaveBeenCalledTimes(2))

    resolveFirst([{ id: propertyId, label: 'Riverfront' }])
    await first
    resolveSecond([{ id: harborId, label: 'Harbor' }])
    await second
  })
})
