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
  buildTradeDocumentPartsPayload: mocks.buildPayload,
  hydrateTradeDocumentPartLookupRows: mocks.hydrate,
  syncTradeDocumentAmountField: mocks.synchronize,
}))

import { useCatalogEntityEditorPersistence } from '../../../src/editor/useCatalogEntityEditorPersistence'
import { useDocumentEntityEditorPersistence } from '../../../src/editor/useDocumentEntityEditorPersistence'

describe('Trade persistence composition', () => {
  beforeEach(() => vi.clearAllMocks())

  it('delegates catalog persistence to the platform adapter', () => {
    const context = { vertical: 'trade' }
    const result = useCatalogEntityEditorPersistence(context as never)
    expect(mocks.createCatalogPersistence).toHaveBeenCalledWith(context)
    expect(result).toBe(mocks.createCatalogPersistence.mock.results[0]?.value)
  })

  it('binds Trade document-part policies to the platform adapter', async () => {
    const context = { vertical: 'trade' }
    const result = useDocumentEntityEditorPersistence(context as never)
    const policy = mocks.createDocumentPersistence.mock.calls[0]?.[1]
    const args = {
      documentType: 'trade.order',
      entityTypeCode: 'trade.order',
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
      entityTypeCode: 'trade.order',
      lookupStore: args.lookupStore,
    }))
    expect(mocks.synchronize).toHaveBeenCalledWith({
      partsMeta: args.partsMeta,
      partsModel: args.partsModel,
      model: args.model,
    })
  })
})
