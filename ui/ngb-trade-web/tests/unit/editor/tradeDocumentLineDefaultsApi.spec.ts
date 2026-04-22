import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  httpPost: vi.fn(),
}))

vi.mock('ngb-ui-framework', () => ({
  httpPost: mocks.httpPost,
}))

import { resolveTradeDocumentLineDefaults } from '../../../src/editor/tradeDocumentLineDefaultsApi'

describe('trade document line defaults api', () => {
  beforeEach(() => {
    mocks.httpPost.mockReset()
  })

  it('posts the resolver request to the trade endpoint', async () => {
    const request = {
      documentType: 'trd.sales_invoice',
      asOfDate: '2026-04-18',
      warehouseId: 'warehouse-1',
      priceTypeId: 'price-type-1',
      rows: [{ rowKey: 'row-1', itemId: 'item-1' }],
    }
    const signal = new AbortController().signal
    mocks.httpPost.mockResolvedValue({ rows: [] })

    await resolveTradeDocumentLineDefaults(request, signal)

    expect(mocks.httpPost).toHaveBeenCalledWith('/api/trade/document-line-defaults/resolve', request, { signal })
  })

  it('returns the resolved defaults payload', async () => {
    const response = {
      rows: [{ rowKey: 'row-1', unitPrice: 20, unitCost: 12, currency: 'USD', priceType: null }],
    }
    mocks.httpPost.mockResolvedValue(response)

    await expect(resolveTradeDocumentLineDefaults({
      documentType: 'trd.sales_invoice',
      rows: [{ rowKey: 'row-1', itemId: 'item-1' }],
    })).resolves.toEqual(response)
  })

  it('propagates upstream transport failures', async () => {
    mocks.httpPost.mockRejectedValue(new Error('network timeout'))

    await expect(resolveTradeDocumentLineDefaults({
      documentType: 'trd.sales_invoice',
      rows: [{ rowKey: 'row-1', itemId: 'item-1' }],
    })).rejects.toThrow('network timeout')
  })
})
