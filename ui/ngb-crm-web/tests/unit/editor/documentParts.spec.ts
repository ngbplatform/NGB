import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PartMetadata, RecordParts } from '@ngbplatform/ui'

const GUID = '11111111-2222-3333-4444-555555555555'
const GUID_2 = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'

const mocks = vi.hoisted(() => ({
  randomUUID: vi.fn(() => 'generated-row-key'),
  resolveLookupHint: vi.fn(({ field }) => field.lookup ?? null),
}))

vi.mock('@ngbplatform/ui', () => ({
  buildFieldsPayload: (_form: unknown, row: Record<string, unknown>) => Object.fromEntries(
    Object.entries(row).filter(([key]) => key !== '__row_key' && key !== 'ordinal'),
  ),
  dataTypeKind: (value: unknown) => String(value),
  isNonEmptyGuid: (value: unknown) => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f-]{27}$/i.test(value),
  isReferenceValue: (value: unknown) => !!value && typeof value === 'object' && 'id' in value,
  resolveLookupHint: mocks.resolveLookupHint,
}))

import {
  buildCRMDocumentPartsPayload,
  calculateCRMDocumentAmount,
  calculateCRMDocumentPartAmount,
  ensureCRMDocumentPartRowKey,
  hydrateCRMDocumentPartLookupRows,
  listCRMDocumentPartFields,
  normalizeCRMDocumentPartRows,
  resolveCRMDocumentAmountSourceField,
  syncCRMDocumentAmountField,
} from '../../../src/editor/documentParts'

const quoteLinesPart: PartMetadata = {
  partCode: 'lines',
  displayName: 'Lines',
  allowAddRemoveRows: true,
  list: {
    columns: [
      { key: 'ordinal', label: '#', dataType: 'Int32', lookup: null },
      { key: 'enabled', label: 'Enabled', dataType: 'Boolean', lookup: null },
      { key: 'count', label: 'Count', dataType: 'Int32', lookup: null },
      { key: 'product_id', label: 'Product', dataType: 'Guid', lookup: { kind: 'catalog', catalogType: 'crm.product' } },
      { key: 'quantity', label: 'Quantity', dataType: 'Decimal', lookup: null },
      { key: 'unit_price', label: 'Unit Price', dataType: 'Money', lookup: null },
      { key: 'service_on', label: 'Service On', dataType: 'Date', lookup: null },
      { key: 'created_at', label: 'Created At', dataType: 'DateTime', lookup: null },
      { key: 'description', label: 'Description', dataType: 'String', lookup: null },
      { key: 'line_amount', label: 'Line Amount', dataType: 'Decimal', lookup: null },
    ],
  },
}

function part(partCode: string, amountKey: 'line_amount' | 'amount' | null = 'line_amount'): PartMetadata {
  return {
    partCode,
    displayName: partCode,
    allowAddRemoveRows: true,
    list: {
      columns: [
        { key: 'ordinal', label: '#', dataType: 'Int32', lookup: null },
        ...(amountKey ? [{ key: amountKey, label: 'Amount', dataType: 'Decimal', lookup: null }] : []),
      ],
    },
  }
}

describe('CRM document parts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('crypto', { randomUUID: mocks.randomUUID })
  })

  afterEach(() => vi.unstubAllGlobals())

  it('maps metadata columns to controls and excludes ordinal', () => {
    const fields = listCRMDocumentPartFields(quoteLinesPart)
    expect(fields.map((field) => [field.key, field.uiControl])).toEqual([
      ['enabled', 5],
      ['count', 3],
      ['product_id', 1],
      ['quantity', 3],
      ['unit_price', 4],
      ['service_on', 6],
      ['created_at', 7],
      ['description', 1],
      ['line_amount', 3],
    ])
    expect(fields[2]).toMatchObject({
      isRequired: true,
      isReadOnly: false,
      validation: null,
      helpText: null,
      lookup: { kind: 'catalog', catalogType: 'crm.product' },
    })
    expect(listCRMDocumentPartFields({ ...quoteLinesPart, list: null as never })).toEqual([])
  })

  it('reuses explicit and cached row keys and generates UUID keys', () => {
    expect(ensureCRMDocumentPartRowKey({ __row_key: ' explicit ' })).toBe('explicit')
    const row = {}
    expect(ensureCRMDocumentPartRowKey(row)).toBe('generated-row-key')
    expect(ensureCRMDocumentPartRowKey(row)).toBe('generated-row-key')
    expect(mocks.randomUUID).toHaveBeenCalledOnce()
  })

  it('falls back to a timestamp/random row key without crypto.randomUUID', () => {
    vi.stubGlobal('crypto', {})
    const key = ensureCRMDocumentPartRowKey({})
    expect(key).toMatch(/^row_[a-z0-9]+_[a-z0-9]+$/)
  })

  it('normalizes absent and populated rows with sequential ordinals', () => {
    expect(normalizeCRMDocumentPartRows(null)).toEqual([])
    expect(normalizeCRMDocumentPartRows(undefined)).toEqual([])
    const rows = normalizeCRMDocumentPartRows([
      { product_id: 'p1', ordinal: 99 },
      { product_id: 'p2', ordinal: -1 },
    ])
    expect(rows.map((row) => row.ordinal)).toEqual([1, 2])
    expect(rows.every((row) => row.__row_key === 'generated-row-key')).toBe(true)
  })

  it('resolves amount source priority and absence', () => {
    expect(resolveCRMDocumentAmountSourceField(quoteLinesPart)).toBe('line_amount')
    expect(resolveCRMDocumentAmountSourceField(part('amounts', 'amount'))).toBe('amount')
    expect(resolveCRMDocumentAmountSourceField(part('notes', null))).toBeNull()
    expect(resolveCRMDocumentAmountSourceField({ ...quoteLinesPart, list: null as never })).toBeNull()
  })

  it('parses, sums, ignores invalid amounts, and rounds to four decimals', () => {
    expect(calculateCRMDocumentPartAmount(part('notes', null), [])).toBeNull()
    expect(calculateCRMDocumentPartAmount(quoteLinesPart, null)).toBe(0)
    expect(calculateCRMDocumentPartAmount(quoteLinesPart, [
      { line_amount: null },
      { line_amount: 1.11111 },
      { line_amount: Number.POSITIVE_INFINITY },
      { line_amount: ' 2,000.22222 ' },
      { line_amount: ' ' },
      { line_amount: 'not-a-number' },
    ])).toBe(2001.3333)
  })

  it('calculates totals across amount parts and ignores non-amount parts', () => {
    expect(calculateCRMDocumentAmount(null, null)).toBeNull()
    expect(calculateCRMDocumentAmount([], {})).toBeNull()
    expect(calculateCRMDocumentAmount([part('notes', null)], { notes: { rows: [{}] } })).toBeNull()
    expect(calculateCRMDocumentAmount(
      [part('lines'), part('fees', 'amount'), part('notes', null)],
      {
        lines: { rows: [{ line_amount: 20 }] },
        fees: { rows: [{ amount: '15.50' }] },
      },
    )).toBe(35.5)
  })

  it('synchronizes model amount only when the destination and source exist', () => {
    syncCRMDocumentAmountField({ partsMeta: [quoteLinesPart], partsModel: {}, model: null })
    const withoutAmount = { memo: 'x' }
    syncCRMDocumentAmountField({ partsMeta: [quoteLinesPart], partsModel: {}, model: withoutAmount })
    expect(withoutAmount).toEqual({ memo: 'x' })
    const noAmountPart = { amount: 99 }
    syncCRMDocumentAmountField({ partsMeta: [part('notes', null)], partsModel: {}, model: noAmountPart })
    expect(noAmountPart.amount).toBe(99)
    const model = { amount: 0 }
    syncCRMDocumentAmountField({
      partsMeta: [quoteLinesPart],
      partsModel: { lines: { rows: [{ line_amount: 12.5 }] } },
      model,
    })
    expect(model.amount).toBe(12.5)
  })

  it('passes through parts without metadata and returns null without either input', () => {
    const existing: RecordParts = { custom: { rows: [{ value: 1 }] } }
    expect(buildCRMDocumentPartsPayload(null, existing)).toBe(existing)
    expect(buildCRMDocumentPartsPayload([], null)).toBeNull()
  })

  it('builds a clean payload, normalizes ordinals, and handles absent rows', () => {
    const payload = buildCRMDocumentPartsPayload(
      [quoteLinesPart, part('fees', 'amount')],
      {
        lines: { rows: [{ product_id: 'p1', line_amount: 20, ordinal: null }] },
      },
    )
    expect(payload?.lines.rows).toEqual([expect.objectContaining({
      ordinal: 1,
      product_id: 'p1',
      line_amount: 20,
    })])
    expect(payload?.lines.rows[0]).not.toHaveProperty('__row_key')
    expect(payload?.fees.rows).toEqual([])
  })

  it('skips lookup hydration without metadata/model, rows, GUIDs, or hints', async () => {
    const lookupStore = {
      ensureCatalogLabels: vi.fn(),
      ensureCoaLabels: vi.fn(),
      ensureAnyDocumentLabels: vi.fn(),
    }
    await hydrateCRMDocumentPartLookupRows({ entityTypeCode: 'crm.quote', partsMeta: null, partsModel: {}, lookupStore } as never)
    await hydrateCRMDocumentPartLookupRows({ entityTypeCode: 'crm.quote', partsMeta: [quoteLinesPart], partsModel: null, lookupStore } as never)
    await hydrateCRMDocumentPartLookupRows({ entityTypeCode: 'crm.quote', partsMeta: [quoteLinesPart], partsModel: {}, lookupStore } as never)
    mocks.resolveLookupHint.mockReturnValueOnce(null)
    await hydrateCRMDocumentPartLookupRows({
      entityTypeCode: 'crm.quote',
      partsMeta: [quoteLinesPart],
      partsModel: { lines: { rows: [{ product_id: { id: GUID }, description: GUID, quantity: 'invalid' }] } },
      lookupStore,
    } as never)
    expect(lookupStore.ensureCatalogLabels).not.toHaveBeenCalled()
  })

  it('prefetches and hydrates catalog, coa, and document references with label fallback', async () => {
    const lookupStore = {
      ensureCatalogLabels: vi.fn().mockResolvedValue(undefined),
      ensureCoaLabels: vi.fn().mockResolvedValue(undefined),
      ensureAnyDocumentLabels: vi.fn().mockResolvedValue(undefined),
      labelForCatalog: vi.fn(() => ' Product '),
      labelForCoa: vi.fn(() => ' '),
      labelForAnyDocument: vi.fn(() => ' Quote '),
    }
    const hydrationPart: PartMetadata = {
      partCode: 'lines',
      displayName: 'Lines',
      allowAddRemoveRows: true,
      list: {
        columns: [
          { key: 'catalog_id', label: 'Catalog', dataType: 'Guid', lookup: { kind: 'catalog', catalogType: 'crm.product' } },
          { key: 'account_id', label: 'Account', dataType: 'Guid', lookup: { kind: 'coa' } },
          { key: 'document_id', label: 'Document', dataType: 'Guid', lookup: { kind: 'document', documentTypes: ['crm.quote'] } },
        ],
      },
    }
    const rows = [
      { catalog_id: GUID, account_id: GUID, document_id: GUID },
      { catalog_id: GUID_2, account_id: GUID_2, document_id: GUID_2 },
    ]
    await hydrateCRMDocumentPartLookupRows({
      entityTypeCode: 'crm.quote',
      partsMeta: [hydrationPart],
      partsModel: { lines: { rows } },
      lookupStore,
      behavior: {},
    } as never)
    expect(lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('crm.product', [GUID, GUID_2])
    expect(lookupStore.ensureCoaLabels).toHaveBeenCalledWith([GUID, GUID_2])
    expect(lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledWith(['crm.quote'], [GUID, GUID_2])
    expect(rows[0]).toEqual({
      catalog_id: { id: GUID, display: 'Product' },
      account_id: { id: GUID, display: GUID },
      document_id: { id: GUID, display: 'Quote' },
    })

    await hydrateCRMDocumentPartLookupRows({
      entityTypeCode: 'crm.quote',
      partsMeta: [{
        ...hydrationPart,
        list: { columns: [hydrationPart.list.columns[0]!] },
      }],
      partsModel: { lines: { rows: [{ catalog_id: GUID }] } },
      lookupStore,
    } as never)
  })
})
