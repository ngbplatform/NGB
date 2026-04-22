import { describe, expect, it, vi } from 'vitest'
import type { LookupStoreApi, PartMetadata, RecordPartRow } from 'ngb-ui-framework'

import {
  buildTradeDocumentPartsPayload,
  calculateTradeDocumentAmount,
  calculateTradeDocumentPartAmount,
  ensureTradeDocumentPartRowKey,
  hydrateTradeDocumentPartLookupRows,
  normalizeTradeDocumentPartRows,
  syncTradeDocumentAmountField,
} from '../../../src/editor/documentParts'

const itemIdA = '11111111-1111-4111-8111-111111111111'
const itemIdB = '22222222-2222-4222-8222-222222222222'
const documentIdA = '33333333-3333-4333-8333-333333333333'
const coaIdA = '44444444-4444-4444-8444-444444444444'

const linesPart = {
  partCode: 'lines',
  title: 'Lines',
  list: {
    columns: [
      { key: 'ordinal', label: '#', dataType: 'Int32', isSortable: true, align: 1 },
      { key: 'item_id', label: 'Item', dataType: 'Guid', isSortable: true, align: 1, lookup: { kind: 'catalog', catalogType: 'trd.item' } },
      { key: 'source_document_id', label: 'Source document', dataType: 'Guid', isSortable: false, align: 1, lookup: { kind: 'document', documentTypes: ['trd.sales_invoice'] } },
      { key: 'revenue_account_id', label: 'Revenue account', dataType: 'Guid', isSortable: false, align: 1, lookup: { kind: 'coa' } },
      { key: 'line_amount', label: 'Line Amount', dataType: 'Money', isSortable: true, align: 2 },
    ],
  },
} satisfies PartMetadata

const adjustmentsPart = {
  partCode: 'adjustments',
  title: 'Adjustments',
  list: {
    columns: [
      { key: 'ordinal', label: '#', dataType: 'Int32', isSortable: true, align: 1 },
      { key: 'amount', label: 'Amount', dataType: 'Money', isSortable: true, align: 2 },
    ],
  },
} satisfies PartMetadata

function createLookupStore(): LookupStoreApi {
  return {
    searchCatalog: vi.fn(async () => []),
    searchCoa: vi.fn(async () => []),
    searchDocuments: vi.fn(async () => []),
    ensureCatalogLabels: vi.fn(async () => undefined),
    ensureCoaLabels: vi.fn(async () => undefined),
    ensureAnyDocumentLabels: vi.fn(async () => undefined),
    labelForCatalog: vi.fn((catalogType: string, id: unknown) => `${catalogType}:${String(id)}`),
    labelForCoa: vi.fn((id: unknown) => `coa:${String(id)}`),
    labelForAnyDocument: vi.fn((_documentTypes: string[], id: unknown) => `document:${String(id)}`),
  }
}

describe('trade document parts', () => {
  it('keeps stable row keys and normalizes ordinals', () => {
    const firstRow: RecordPartRow = { item_id: itemIdA }
    const secondRow: RecordPartRow = { __row_key: 'persisted-key', item_id: itemIdB }

    const generatedKey = ensureTradeDocumentPartRowKey(firstRow)

    expect(ensureTradeDocumentPartRowKey(firstRow)).toBe(generatedKey)
    expect(ensureTradeDocumentPartRowKey(secondRow)).toBe('persisted-key')

    const normalized = normalizeTradeDocumentPartRows([firstRow, secondRow])
    expect(normalized[0]).toMatchObject({ __row_key: generatedKey, ordinal: 1, item_id: itemIdA })
    expect(normalized[1]).toMatchObject({ __row_key: 'persisted-key', ordinal: 2, item_id: itemIdB })
  })

  it('calculates part and document amounts from mixed numeric inputs', () => {
    expect(calculateTradeDocumentPartAmount(linesPart, [
      { line_amount: '1,200.1055' },
      { line_amount: 9.00456 },
      { line_amount: 'oops' },
    ])).toBe(1209.1101)

    expect(calculateTradeDocumentAmount(
      [linesPart, adjustmentsPart],
      {
        lines: { rows: [{ line_amount: '120.1' }, { line_amount: '9.9' }] },
        adjustments: { rows: [{ amount: '4.55555' }] },
      },
    )).toBe(134.5556)
  })

  it('syncs the document amount only when the model exposes that field', () => {
    const withAmount = { amount: 0, notes: 'draft' }
    const withoutAmount = { notes: 'draft' }

    syncTradeDocumentAmountField({
      partsMeta: [linesPart],
      partsModel: {
        lines: { rows: [{ line_amount: '12.5' }, { line_amount: '7.5' }] },
      },
      model: withAmount,
    })
    syncTradeDocumentAmountField({
      partsMeta: [linesPart],
      partsModel: {
        lines: { rows: [{ line_amount: '99' }] },
      },
      model: withoutAmount,
    })

    expect(withAmount.amount).toBe(20)
    expect(withoutAmount).not.toHaveProperty('amount')
  })

  it('builds payloads without synthetic row keys and with normalized ordinals', () => {
    const payload = buildTradeDocumentPartsPayload(
      [linesPart],
      {
        lines: {
          rows: [
            { __row_key: 'local-1', ordinal: 90, item_id: itemIdA, line_amount: '10.25' },
            { item_id: itemIdB, line_amount: '5.75' },
          ],
        },
      },
    )

    expect(payload).toEqual({
      lines: {
        rows: [
          { ordinal: 1, item_id: itemIdA, source_document_id: null, revenue_account_id: null, line_amount: 10.25 },
          { ordinal: 2, item_id: itemIdB, source_document_id: null, revenue_account_id: null, line_amount: 5.75 },
        ],
      },
    })
  })

  it('hydrates catalog, document, and coa references in one pass with deduplicated batches', async () => {
    const lookupStore = createLookupStore()
    const rows: RecordPartRow[] = [
      {
        item_id: itemIdA,
        source_document_id: documentIdA,
        revenue_account_id: coaIdA,
        line_amount: 12,
      },
      {
        item_id: itemIdA,
        source_document_id: documentIdA,
        revenue_account_id: coaIdA,
        line_amount: 18,
      },
    ]

    await hydrateTradeDocumentPartLookupRows({
      entityTypeCode: 'trd.sales_invoice',
      partsMeta: [linesPart],
      partsModel: {
        lines: { rows },
      },
      lookupStore,
    })

    expect(lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('trd.item', [itemIdA])
    expect(lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledWith(['trd.sales_invoice'], [documentIdA])
    expect(lookupStore.ensureCoaLabels).toHaveBeenCalledWith([coaIdA])

    expect(rows[0]).toMatchObject({
      item_id: { id: itemIdA, display: `trd.item:${itemIdA}` },
      source_document_id: { id: documentIdA, display: `document:${documentIdA}` },
      revenue_account_id: { id: coaIdA, display: `coa:${coaIdA}` },
    })
    expect(rows[1]).toMatchObject({
      item_id: { id: itemIdA, display: `trd.item:${itemIdA}` },
      source_document_id: { id: documentIdA, display: `document:${documentIdA}` },
      revenue_account_id: { id: coaIdA, display: `coa:${coaIdA}` },
      line_amount: 18,
    })
  })
})
