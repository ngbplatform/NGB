import { describe, expect, it } from 'vitest'
import type { PartMetadata, RecordParts } from 'ngb-ui-framework'

import {
  buildCRMDocumentPartsPayload,
  calculateCRMDocumentAmount,
  normalizeCRMDocumentPartRows,
} from '../../../src/editor/documentParts'

const quoteLinesPart: PartMetadata = {
  partCode: 'lines',
  displayName: 'Lines',
  allowAddRemoveRows: true,
  list: {
    columns: [
      { key: 'ordinal', label: '#', dataType: 'Int32', lookup: null },
      { key: 'product_id', label: 'Product', dataType: 'Guid', lookup: { kind: 'catalog', catalogType: 'crm.product' } },
      { key: 'quantity', label: 'Quantity', dataType: 'Decimal', lookup: null },
      { key: 'unit_price', label: 'Unit Price', dataType: 'Decimal', lookup: null },
      { key: 'line_amount', label: 'Line Amount', dataType: 'Decimal', lookup: null },
    ],
  },
}

describe('CRM document parts', () => {
  it('normalizes row keys and ordinal values without mutating amount fields', () => {
    const rows = normalizeCRMDocumentPartRows([
      { product_id: 'p1', quantity: 2, unit_price: 10, line_amount: 20 },
      { product_id: 'p2', quantity: 1, unit_price: 15, line_amount: 15 },
    ])

    expect(rows.map((row) => row.ordinal)).toEqual([1, 2])
    expect(rows.every((row) => typeof row.__row_key === 'string' && row.__row_key.length > 0)).toBe(true)
  })

  it('calculates document amount from quote line amounts', () => {
    const parts: RecordParts = {
      lines: {
        rows: [
          { line_amount: 20 },
          { line_amount: '15.50' },
        ],
      },
    }

    expect(calculateCRMDocumentAmount([quoteLinesPart], parts)).toBe(35.5)
  })

  it('builds a clean payload with ordinal values', () => {
    const payload = buildCRMDocumentPartsPayload([quoteLinesPart], {
      lines: {
        rows: [
          { product_id: 'p1', quantity: 2, unit_price: 10, line_amount: 20 },
        ],
      },
    })

    expect(payload?.lines.rows).toEqual([
      expect.objectContaining({
        ordinal: 1,
        product_id: 'p1',
        quantity: 2,
        unit_price: 10,
        line_amount: 20,
      }),
    ])
  })
})
