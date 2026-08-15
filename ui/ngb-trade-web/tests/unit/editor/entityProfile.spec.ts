import { describe, expect, it, vi } from 'vitest'

vi.mock('@ngbplatform/ui', () => ({
  asTrimmedString: (value: unknown) => value == null ? null : String(value).trim() || null,
}))

import { resolveTradeEditorEntityProfile } from '../../../src/editor/entityProfile'

function sync(kind: 'catalog' | 'document', typeCode: string, model: Record<string, unknown>) {
  const profile = resolveTradeEditorEntityProfile({ kind, typeCode } as never)
  profile?.syncComputedDisplay?.({ model } as never)
  return { profile, model }
}

describe('trade editor entity profiles', () => {
  it('returns null for unsupported contexts', () => {
    expect(sync('catalog', 'trd.price_type', {}).profile).toBeNull()
    expect(sync('document', 'trd.unknown', {}).profile).toBeNull()
  })

  it.each(['trd.item', 'trd.unit_of_measure', 'trd.party'])('syncs %s name from display', (typeCode) => {
    const populated = sync('catalog', typeCode, { display: ' Display ', name: 'old' })
    expect(populated.profile).toMatchObject({ computedDisplayMode: 'always', computedDisplayWatchFields: ['display'] })
    expect(populated.model.name).toBe('Display')
    const blank = sync('catalog', typeCode, { display: ' ', name: 'keep' })
    expect(blank.model.name).toBe('keep')
  })

  it.each([
    [{}, null],
    [{ name: ' South Hub ' }, 'South Hub'],
    [{ address: ' 14 Logistics Way ' }, '14 Logistics Way'],
    [{ name: ' South Hub ', address: ' 14 Logistics Way ' }, 'South Hub — 14 Logistics Way'],
  ])('computes warehouse display %#', (model, expected) => {
    expect(sync('catalog', 'trd.warehouse', model).model.display).toBe(expected)
  })

  it.each([
    ['trd.purchase_receipt', 'Purchase Receipt', 'document_date_utc'],
    ['trd.sales_invoice', 'Sales Invoice', 'document_date_utc'],
    ['trd.customer_payment', 'Customer Payment', 'document_date_utc'],
    ['trd.vendor_payment', 'Vendor Payment', 'document_date_utc'],
    ['trd.inventory_transfer', 'Inventory Transfer', 'document_date_utc'],
    ['trd.inventory_adjustment', 'Inventory Adjustment', 'document_date_utc'],
    ['trd.customer_return', 'Customer Return', 'document_date_utc'],
    ['trd.vendor_return', 'Vendor Return', 'document_date_utc'],
    ['trd.item_price_update', 'Item Price Update', 'effective_date'],
  ])('computes %s display', (typeCode, title, dateField) => {
    const result = sync('document', typeCode, { number: ' N-1 ', [dateField]: '2026-07-31' })
    expect(result.profile).toMatchObject({ computedDisplayMode: 'new_or_draft', computedDisplayWatchFields: ['number', dateField] })
    expect(result.model.display).toBe(`${title} N-1 7/31/2026`)
  })

  it.each([
    [{}, null],
    [{ number: ' N-1 ' }, 'Sales Invoice N-1'],
    [{ document_date_utc: '2026-01-02' }, 'Sales Invoice 1/2/2026'],
    [{ document_date_utc: '2026-00-01' }, null],
    [{ document_date_utc: '2026-13-01' }, null],
    [{ document_date_utc: '2026-01-00' }, null],
    [{ document_date_utc: '2026-01-32' }, null],
    [{ document_date_utc: 'not-a-date' }, null],
    [{ document_date_utc: 20260731 }, null],
  ])('handles optional and invalid document display parts %#', (model, expected) => {
    expect(sync('document', 'trd.sales_invoice', model).model.display).toBe(expected)
  })
})
