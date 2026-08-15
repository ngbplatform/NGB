import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  createCatalogPersistence: vi.fn(() => ({ load: vi.fn() })),
  createDocumentPersistence: vi.fn(() => ({ load: vi.fn() })),
  buildPayload: vi.fn(() => ({ lines: [] })),
  hydrate: vi.fn(),
  synchronize: vi.fn(),
}))

vi.mock('@ngbplatform/ui', () => ({
  createConfiguredCatalogEntityEditorPersistence: mocks.createCatalogPersistence,
  createConfiguredDocumentEntityEditorPersistence: mocks.createDocumentPersistence,
}))

vi.mock('../../../src/editor/documentParts', () => ({
  buildCRMDocumentPartsPayload: mocks.buildPayload,
  hydrateCRMDocumentPartLookupRows: mocks.hydrate,
  syncCRMDocumentAmountField: mocks.synchronize,
}))

import { useCatalogEntityEditorPersistence } from '../../../src/editor/useCatalogEntityEditorPersistence'
import { useDocumentEntityEditorPersistence } from '../../../src/editor/useDocumentEntityEditorPersistence'

describe('CRM persistence composition', () => {
  beforeEach(() => vi.clearAllMocks())

  it('delegates catalog persistence to the platform adapter', () => {
    const context = { vertical: 'crm' }
    const result = useCatalogEntityEditorPersistence(context as never)
    expect(mocks.createCatalogPersistence).toHaveBeenCalledWith(context)
    expect(result).toBe(mocks.createCatalogPersistence.mock.results[0]?.value)
  })

  it('binds CRM document-part policies to the platform adapter', async () => {
    const context = { vertical: 'crm' }
    const result = useDocumentEntityEditorPersistence(context as never)
    const policy = mocks.createDocumentPersistence.mock.calls[0]?.[1]
    const args = {
      documentType: 'crm.lead',
      entityTypeCode: 'crm.lead',
      partsMeta: [{ key: 'lines' }],
      partsModel: { lines: [] },
      lookupStore: { id: 'lookups' },
      model: { amount: 10 },
    }

    expect(result).toBe(mocks.createDocumentPersistence.mock.results[0]?.value)
    expect(policy.buildPayload(args)).toEqual({ lines: [] })
    await policy.hydrate(args)
    policy.synchronize(args)
    expect(mocks.buildPayload).toHaveBeenCalledWith(args.partsMeta, args.partsModel)
    expect(mocks.hydrate).toHaveBeenCalledWith(expect.objectContaining({
      entityTypeCode: 'crm.lead',
      lookupStore: args.lookupStore,
    }))
    expect(mocks.synchronize).toHaveBeenCalledWith({
      partsMeta: args.partsMeta,
      partsModel: args.partsModel,
      model: args.model,
    })
  })
})
